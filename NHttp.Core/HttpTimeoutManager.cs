using Common.Logging;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace NHttp
{
    internal class HttpTimeoutManager : IDisposable
    {
        private readonly ILog _logger;
        private Task _thread;
        private ManualResetEvent _closeEvent = new ManualResetEvent(false);

        public TimeoutQueue ReadQueue { get; private set; }
        public TimeoutQueue WriteQueue { get; private set; }

        public HttpTimeoutManager(HttpServer server, ILog logger)
        {
            if (server == null)
                throw new ArgumentNullException(nameof(server));
            if (logger == null)
                throw new ArgumentNullException(nameof(logger));

            _logger = logger;
            ReadQueue = new TimeoutQueue(server.ReadTimeout);
            WriteQueue = new TimeoutQueue(server.WriteTimeout);

            _thread = Task.Run(() =>
            {
                while (!_closeEvent.WaitOne(TimeSpan.FromSeconds(1)))
                {
                    ProcessQueue(ReadQueue);
                    ProcessQueue(WriteQueue);
                }
            });
        }

        private void ProcessQueue(TimeoutQueue queue)
        {
            while (true)
            {
                var item = queue.DequeueExpired();
                if (item == null) return;

                if (!item.AsyncResult.IsCompleted)
                {
                    try
                    {
                        item.Disposable.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug("Failed to dispose timed-out item", ex);
                    }
                }
            }
        }

        public void Dispose()
        {
            if (_thread != null)
            {
                _closeEvent.Set();
                _thread.Wait();
                _thread = null;
            }

            if (_closeEvent != null)
            {
                _closeEvent.Close();
                _closeEvent = null;
            }
        }

        public class TimeoutQueue
        {
            private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
            private readonly long _timeout;
            private readonly ConcurrentQueue<TimeoutItem> _items = new ConcurrentQueue<TimeoutItem>();

            public TimeoutQueue(TimeSpan timeout)
            {
                _timeout = (long)(timeout.TotalSeconds * Stopwatch.Frequency);
            }

            public void Add(IAsyncResult asyncResult, IDisposable disposable)
            {
                if (asyncResult == null)
                    throw new ArgumentNullException(nameof(asyncResult));
                if (disposable == null)
                    throw new ArgumentNullException(nameof(disposable));

                _items.Enqueue(new TimeoutItem(_stopwatch.ElapsedTicks + _timeout, asyncResult, disposable));
            }

            public TimeoutItem DequeueExpired()
            {
                if (_items.Count == 0) return null;

                _items.TryPeek(out TimeoutItem item);
                if (item.Expires < _stopwatch.ElapsedTicks && _items.TryDequeue(out TimeoutItem removedItem))
                    return removedItem;

                return null;
            }
        }

        public class TimeoutItem
        {
            public long Expires { get; private set; }
            public IAsyncResult AsyncResult { get; private set; }
            public IDisposable Disposable { get; private set; }

            public TimeoutItem(long expires, IAsyncResult asyncResult, IDisposable disposable)
            {
                if (asyncResult == null)
                    throw new ArgumentNullException(nameof(asyncResult));

                Expires = expires;
                AsyncResult = asyncResult;
                Disposable = disposable;
            }
        }
    }
}