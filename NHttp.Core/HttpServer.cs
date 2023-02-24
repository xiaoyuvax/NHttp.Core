using Common.Logging;
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Wima.Log;

namespace NHttp
{
    public class HttpServer : IHttpServer
    {
        private static readonly ILog Log = new WimaLogger(typeof(HttpServer));

        private bool _disposed;
        private TcpListener _listener;
        private readonly object _syncLock = new object();
        private readonly ConcurrentDictionary<HttpClient, bool> _clients = new ConcurrentDictionary<HttpClient, bool>();
        private HttpServerState _state = HttpServerState.Stopped;
        private AutoResetEvent _clientsChangedEvent = new AutoResetEvent(false);

        public X509Certificate ServerCertificate { get; set; }

        public bool UseSSL => ServerCertificate != null;

        public bool ClientCertificateRequire { get; set; } = false;

        public SslProtocols AllowedSslProtocols { get; set; } = (SslProtocols)12288 | SslProtocols.Tls12 | SslProtocols.Tls11;

        public bool SocketReuseAddress { get; set; } = false;

        public HttpServerState State
        {
            get => _state;
            set
            {
                if (_state != value)
                {
                    _state = value;

                    OnStateChanged(EventArgs.Empty);
                }
            }
        }

        public event HttpRequestEventHandler RequestReceived;

        protected virtual void OnRequestReceived(HttpRequestEventArgs e) => RequestReceived?.Invoke(this, e);

        public event HttpExceptionEventHandler UnhandledException;

        protected virtual void OnUnhandledException(HttpExceptionEventArgs e) => UnhandledException?.Invoke(this, e);

        public event EventHandler StateChanged;

        protected virtual void OnStateChanged(EventArgs e) => StateChanged?.Invoke(this, e);

        public IPEndPoint EndPoint { get; set; }

        public int ReadBufferSize { get; set; }

        public int WriteBufferSize { get; set; }

        public string ServerBanner { get; set; }

        public TimeSpan ReadTimeout { get; set; }

        public TimeSpan WriteTimeout { get; set; }

        public TimeSpan ShutdownTimeout { get; set; }

        internal HttpTimeoutManager TimeoutManager { get; private set; }

        public HttpServer()
        {
            EndPoint = new IPEndPoint(IPAddress.Loopback, 0);

            ReadBufferSize = 4096;
            WriteBufferSize = 4096;
            ShutdownTimeout = TimeSpan.FromSeconds(5);
            ReadTimeout = TimeSpan.FromSeconds(90);
            WriteTimeout = TimeSpan.FromSeconds(90);

            ServerBanner = string.Format("NHttp/{0}", GetType().Assembly.GetName().Version);
        }

        public void Start()
        {
            if (!VerifyState(HttpServerState.Stopped)) return;

            State = HttpServerState.Starting;

            Log.Debug(string.Format("Starting HTTP server at {0}", EndPoint));

            TimeoutManager = new HttpTimeoutManager(this);

            // Start the listener.

            var listener = new TcpListener(EndPoint);
            //set the REUSE ADDRESS option value on the underlying socket,to allow port being release immediately.
            if (SocketReuseAddress) listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, 1);

            try
            {
                listener.Start();

                EndPoint = (IPEndPoint)listener.LocalEndpoint;

                _listener = listener;

                Log.Info(string.Format("HTTP server running at {0}", EndPoint));
            }
            catch (Exception ex)
            {
                State = HttpServerState.Stopped;

                Log.Error("Failed to start HTTP server", ex);

                throw new NHttpException("Failed to start HTTP server", ex);
            }

            State = HttpServerState.Started;

            Task.Run(() =>
            {
                while (!_disposed && _state == HttpServerState.Started)
                {
                    try
                    {
                        var t = _listener?.AcceptTcpClient();
                        AcceptTcpClientCallback(t);
                    }
                    catch (SocketException sex) when (sex.SocketErrorCode == SocketError.Interrupted)
                    {
                        //Provider friendlier output for SocketError.Interrupted.
                        Log.Error("Socket interrupted by unexpected reason, such as User pressed Ctl+C.");
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex);
                    }
                }
            });
        }

        public void Stop()
        {
            if (!VerifyState(HttpServerState.Started)) return;

            Log.Debug("Stopping HTTP server...");

            State = HttpServerState.Stopping;

            try
            {
                // Prevent any new connections.
                _listener.Stop();

                // Wait for all clients to complete.
                StopClients();
            }
            catch (Exception ex)
            {
                Log.Error("Failed to stop HTTP server", ex);

                throw new NHttpException("Failed to stop HTTP server", ex);
            }
            finally
            {
                _listener = null;

                State = HttpServerState.Stopped;

                Log.Info("Stopped HTTP server");
            }
        }

        private void StopClients()
        {
            var shutdownStarted = DateTime.Now;
            bool forceShutdown = false;

            // Clients that are waiting for new requests are closed.
            foreach (var i in _clients.Keys) i.RequestClose();

            // First give all clients a chance to complete their running requests.
            while (_clients.Count > 0)
            {
                var shutdownRunning = DateTime.Now - shutdownStarted;

                if (shutdownRunning >= ShutdownTimeout)
                {
                    forceShutdown = true;
                    break;
                }

                _clientsChangedEvent.WaitOne(ShutdownTimeout - shutdownRunning);
            }

            if (!forceShutdown) return;

            // If there are still clients running after the timeout, their
            // connections will be forcibly closed.

            foreach (var i in _clients.Keys) i.ForceClose();

            // Wait for the registered clients to be cleared.

            while (_clients.Count > 0) _clientsChangedEvent.WaitOne();
        }

        private void AcceptTcpClientCallback(TcpClient tcpClient)
        {
            if (_listener == null) return;
            // If we've stopped already, close the TCP client now.

            if (_state != HttpServerState.Started)
            {
                tcpClient.Close();
                return;
            }

            try
            {
                var client = new HttpClient(this, tcpClient);

                RegisterClient(client);
                client.BeginRequest();
            }
            catch (Exception ex)
            {
                Log.Info("Failed to accept TCP client", ex);
            }
        }

        private void RegisterClient(HttpClient client)
        {
            if (client != null && _clients.TryAdd(client, true)) _clientsChangedEvent?.Set();
        }

        internal void UnregisterClient(HttpClient client)
        {
            if (client != null && _clients.TryRemove(client, out _)) _clientsChangedEvent?.Set();
        }

        private bool VerifyState(HttpServerState state) => !_disposed && _state == state;

        public void Dispose()
        {
            if (!_disposed)
            {
                if (_state == HttpServerState.Started) Stop();

                ((IDisposable)_clientsChangedEvent)?.Dispose();
                _clientsChangedEvent = null;

                TimeoutManager?.Dispose();
                TimeoutManager = null;

                _disposed = true;
            }
        }

        internal void RaiseRequest(HttpContext context)
        {
            if (context != null) OnRequestReceived(new HttpRequestEventArgs(context));
        }

        internal bool RaiseUnhandledException(HttpContext context, Exception exception)
        {
            if (context == null) throw new ArgumentNullException("context");

            var e = new HttpExceptionEventArgs(context, exception);

            OnUnhandledException(e);

            return e.Handled;
        }
    }
}