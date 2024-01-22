using Common.Logging;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Wima.Log;

namespace NHttp
{
    internal class HttpClient : IDisposable
    {
        private static readonly ILog Log = new WimaLogger(typeof(HttpClient));

        private static readonly Regex PrologRegex = new Regex("^([A-Z]+) ([^ ]+) (HTTP/[^ ]+)$", RegexOptions.Compiled);

        private readonly byte[] _writeBuffer;
        private HttpContext _context;
        private bool _disposed;
        private bool _errored;
        private HttpRequestParser _parser;
        /// <summary>
        /// Store the TCP Socket reference might be better than TcpClient, so to force disposing the socket.
        /// </summary>
        private Socket _socket;


        private ClientState _state;
        private Stream _stream;
        private MemoryStream _writeStream;
        private Dictionary<string, string> headers;

        public HttpClient(HttpServer server, TcpClient client)
        {
            if (server == null) throw new ArgumentNullException("server");
            if (client == null) throw new ArgumentNullException("client");

            Server = server;
            _socket = client.Client;

            ReadBuffer = new HttpReadBuffer(server.ReadBufferSize);
            _writeBuffer = new byte[server.WriteBufferSize];

            _stream = client.GetStream();

            if (server.UseSSL) try
                {
                    _stream = new SslStream(_stream, false);
                    ((SslStream)_stream).AuthenticateAsServer(server.ServerCertificate, server.ClientCertificateRequire, server.AllowedSslProtocols, true);
                }
                catch (AuthenticationException aex)
                {
                    //Suppress SSL_ERROR_SSL due to sslv3 was deprecated by OS.
                }
                catch (Exception ex) { Log.Debug("SSLStream Creation Error.", ex); }
        }

        private enum ClientState
        {
            ReadingProlog,
            ReadingHeaders,
            ReadingContent,
            WritingHeaders,
            WritingContent,
            Closed
        }

        public Dictionary<string, string> Headers { get => headers; private set => headers = value; }
        public Stream InputStream { get; set; }
        public string Method { get; private set; }
        public List<HttpMultiPartItem> MultiPartItems { get; set; }
        public NameValueCollection PostParameters { get; set; }
        public string Protocol { get; private set; }
        public HttpReadBuffer ReadBuffer { get; private set; }
        public string Request { get; private set; }
        public HttpServer Server { get; private set; }

        /// <summary>
        /// TcpClient is not threadsafe, so it's meaningless to store its reference here.This property would be depreciated in the future.
        /// Properties relevant to TcpClient should be stored in other thread-safe properties.
        /// This property might be a bug point in the original code, especially for running with .net core.
        /// </summary>
        [Obsolete]
        public TcpClient TcpClient { get; private set; }

        public EndPoint TcpClientRemoteEndPoint => _socket?.RemoteEndPoint;

        public bool UseSSL => _stream is SslStream;
        public void BeginRequest()
        {
            Reset();
            BeginRead();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;

                Server.UnregisterClient(this);

                _state = ClientState.Closed;

                _stream?.Dispose();
                _stream = null;

                _socket?.Dispose();
                _socket = null;

                Reset();
            }
        }

        public void ExecuteRequest()
        {
            _context = new HttpContext(this);

            Log.Debug(string.Format("{0}\t{1}\t{2}\t{3}", TcpClientRemoteEndPoint.ToString(), _context.Request.HttpMethod, _context.Request.RawUrl, _context.Request.Headers.Get("User-Agent")));

            Server.RaiseRequest(_context);

            WriteResponseHeaders();
        }

        public void ForceClose() => Dispose();

        public void RequestClose()
        {
            if (_state == ClientState.ReadingProlog) _stream?.Dispose();
        }

        public void UnsetParser()
        {
            //Debug.Assert(_parser != null);

            _parser = null;
        }

        private void BeginRead()
        {
            if (_disposed) return;

            try
            {
                if (_stream != null && _stream.CanRead)
                {
                    Server.TimeoutManager.ReadQueue.Add(ReadBuffer.ReadAsync(_stream).ContinueWith(t => ReadCallback(t)), this);
                }
            }
            catch (Exception ex)
            {
                Log.Info(ReadBuffer.GetHashCode() + "\tReadAsync failed - ", ex);
                Dispose();
            }
        }

        private void BeginWrite()
        {
            if (_disposed) return;
            try
            {
                // Copy the next part from the write stream.
                int read = _writeStream.Read(_writeBuffer, 0, _writeBuffer.Length);

                Server.TimeoutManager.WriteQueue.Add(
                    _stream.WriteAsync(_writeBuffer, 0, read).ContinueWith(t => WriteCallback()),
                    this);
            }
            catch (Exception ex)
            {
                Log.Info("BeginWrite failed", ex);
                Dispose();
            }
        }

        private byte[] BuildResponseHeaders()
        {
            //Debug.Assert(_context != null);
            if (_context != null)
            {
                var response = _context.Response;
                var sb = new StringBuilder();

                // Write the prolog.

                sb.Append(Protocol);
                sb.Append(' ');
                sb.Append(response.StatusCode);

                if (!string.IsNullOrEmpty(response.StatusDescription))
                {
                    sb.Append(' ');
                    sb.Append(response.StatusDescription);
                }

                sb.Append("\r\n");

                // Write all headers provided by Response.

                if (!string.IsNullOrEmpty(response.CacheControl))
                    WriteHeader(sb, "Cache-Control", response.CacheControl);

                if (!string.IsNullOrEmpty(response.ContentType))
                {
                    string contentType = response.ContentType;

                    if (!string.IsNullOrEmpty(response.CharSet))
                        contentType += "; charset=" + response.CharSet;

                    WriteHeader(sb, "Content-Type", contentType);
                }

                WriteHeader(sb, "Expires", response.ExpiresAbsolute.ToString("R"));

                if (!string.IsNullOrEmpty(response.RedirectLocation))
                    WriteHeader(sb, "Location", response.RedirectLocation);

                // Write the remainder of the headers.

                foreach (string key in response.Headers.AllKeys)
                {
                    WriteHeader(sb, key, response.Headers[key]);
                }

                // Write the content length (we override custom headers for this).

                WriteHeader(sb, "Content-Length", response.OutputStream.BaseStream.Length.ToString(CultureInfo.InvariantCulture));

                for (int i = 0; i < response.Cookies.Count; i++)
                {
                    WriteHeader(sb, "Set-Cookie", response.Cookies[i].GetHeaderValue());
                }

                sb.Append("\r\n");

                return response.HeadersEncoding.GetBytes(sb.ToString());
            }
            else return null;
        }

        private void ProcessContent()
        {
            if (_parser != null)
            {
                _parser.Parse();
                return;
            }

            if (ProcessExpectHeader()) return;

            if (ProcessContentLengthHeader()) return;

            // The request has been completely parsed now.
            ExecuteRequest();
        }

        private bool ProcessContentLengthHeader()
        {
            // Read the content.

            string contentLengthHeader;

            if (Headers.TryGetValue("Content-Length", out contentLengthHeader))
            {
                if (!int.TryParse(contentLengthHeader, out int contentLength))
                    throw new ProtocolException(string.Format("Could not parse Content-Length header '{0}'", contentLengthHeader));

                string contentTypeHeader;
                string contentType = null;
                string contentTypeExtra = null;

                if (Headers.TryGetValue("Content-Type", out contentTypeHeader))
                {
                    string[] parts = contentTypeHeader.Split(new[] { ';' }, 2);

                    contentType = parts[0].Trim().ToLowerInvariant();
                    contentTypeExtra = parts.Length == 2 ? parts[1].Trim() : null;
                }

                if (_parser != null)
                {
                    _parser.Dispose();
                    _parser = null;
                }

                switch (contentType)
                {
                    case "application/x-www-form-urlencoded":
                        _parser = new HttpUrlEncodedRequestParser(this, contentLength);
                        break;

                    case "multipart/form-data":
                        string boundary = null;

                        if (contentTypeExtra != null)
                        {
                            string[] parts = contentTypeExtra.Split(new[] { '=' }, 2);

                            if (
                                parts.Length == 2 &&
                                string.Equals(parts[0], "boundary", StringComparison.OrdinalIgnoreCase)
                            )
                                boundary = parts[1];
                        }

                        if (boundary == null)
                            throw new ProtocolException("Expected boundary with multipart content type");

                        _parser = new HttpMultiPartRequestParser(this, contentLength, boundary);
                        break;

                    default:
                        _parser = new HttpUnknownRequestParser(this, contentLength);
                        break;
                }

                // We've made a parser available. Recurs back to start processing
                // with the parser.

                ProcessContent();
                return true;
            }

            return false;
        }

        private void ProcessException(Exception exception)
        {
            if (_disposed) return;

            _errored = true;

            // If there is no request available, the error didn't occur as part
            // of a request (e.g. the client closed the connection). Just close
            // the channel in that case.

            if (Request == null)
            {
                Dispose();
                return;
            }

            try
            {
                if (_context == null) _context = new HttpContext(this);

                _context.Response.Status = "500 Internal Server Error";

                bool handled;
                try
                {
                    handled = Server.RaiseUnhandledException(_context, exception);
                }
                catch
                {
                    handled = false;
                }

                if (!handled && _context.Response.OutputStream.CanWrite)
                {
                    string resourceName = GetType().Namespace + ".Resources.InternalServerError.html";
                    using (var stream = GetType().Assembly.GetManifestResourceStream(resourceName))
                    {
                        byte[] buffer = new byte[4096];
                        int read;

                        while ((read = stream.Read(buffer, 0, buffer.Length)) != 0)
                        {
                            _context.Response.OutputStream.Write(buffer, 0, read);
                        }
                    }
                }

                WriteResponseHeaders();
            }
            catch (Exception ex)
            {
                Log.Info("Failed to process internal server error response", ex);

                Dispose();
            }
        }

        private bool ProcessExpectHeader()
        {
            // Process the Expect: 100-continue header.

            string expectHeader;

            if (Headers.TryGetValue("Expect", out expectHeader))
            {
                // Remove the expect header for the next run.

                Headers.Remove("Expect");

                int pos = expectHeader.IndexOf(';');

                if (pos != -1)
                    expectHeader = expectHeader.Substring(0, pos).Trim();

                if (!string.Equals("100-continue", expectHeader, StringComparison.OrdinalIgnoreCase))
                    throw new ProtocolException(string.Format("Could not process Expect header '{0}'", expectHeader));

                SendContinueResponse();
                return true;
            }

            return false;
        }

        private void ProcessHeaders()
        {
            string line;

            while ((line = ReadBuffer.ReadLine()) != null)
            {
                // Have we completed receiving the headers?

                if (line.Length == 0)
                {
                    // Reset the read buffer which resets the bytes read.

                    ReadBuffer.Reset();

                    // Start processing the body of the request.

                    _state = ClientState.ReadingContent;

                    ProcessContent();

                    return;
                }

                string[] parts = line.Split(new[] { ':' }, 2);

                if (parts.Length != 2)
                    throw new ProtocolException("Received header without colon");

                Headers[parts[0].Trim()] = parts[1].Trim();
            }
        }

        private void ProcessProlog()
        {
            string line = ReadBuffer.ReadLine();

            if (string.IsNullOrEmpty(line)) return;

            // Parse the prolog.

            var match = PrologRegex.Match(line);

            if (!match.Success) throw new ProtocolException(string.Format("Could not parse prolog '{0}'", line));

            Method = match.Groups[1].Value;
            Request = match.Groups[2].Value;
            Protocol = match.Groups[3].Value;

            // Continue reading the headers.

            _state = ClientState.ReadingHeaders;

            ProcessHeaders();
        }

        private void ProcessReadBuffer()
        {
            while (_writeStream == null && ReadBuffer.DataAvailable)
            {
                switch (_state)
                {
                    case ClientState.ReadingProlog:
                        ProcessProlog();
                        break;

                    case ClientState.ReadingHeaders:
                        ProcessHeaders();
                        break;

                    case ClientState.ReadingContent:
                        ProcessContent();
                        break;

                    default:
                        throw new InvalidOperationException("Invalid state");
                }
            }

            if (_writeStream == null) BeginRead();
        }

        private void ProcessRequestCompleted()
        {
            string connectionHeader;

            // Do not accept new requests when the server is stopping.

            if (!_errored &&
                Server.State == HttpServerState.Started &&
                Headers.TryGetValue("Connection", out connectionHeader) &&
                string.Equals(connectionHeader, "keep-alive", StringComparison.OrdinalIgnoreCase))
                BeginRequest();
            else
                Dispose();
        }

        private void ReadCallback(Task<int> asyncResult)
        {
            if (_disposed) return;

            // The below state matches the RequestClose state. Dispose immediately
            // when this occurs.

            if (_state == ClientState.ReadingProlog && Server.State != HttpServerState.Started)
            {
                Dispose();
                return;
            }

            try
            {
                ReadBuffer.EndRead(asyncResult);
                if (ReadBuffer.DataAvailable) ProcessReadBuffer();
                else Dispose();
            }
            catch (ObjectDisposedException ex)
            {
                Log.Info("Failed to read", ex);
                Dispose();
            }
            catch (Exception ex)
            {
                Log.Info(ReadBuffer.GetHashCode() + "\tFailed to read from the HTTP connection - ", ex);

                ProcessException(ex);
            }
        }

        private void Reset()
        {
            _state = ClientState.ReadingProlog;
            _context = null;

            _parser?.Dispose();
            _parser = null;

            _writeStream?.Dispose();
            _writeStream = null;

            InputStream?.Dispose();
            InputStream = null;

            ReadBuffer.Reset();

            Method = null;
            Protocol = null;
            Request = null;

            //Atomic replace old reference with new instance, suspect to be error prone.
            Interlocked.Exchange(ref headers, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
            PostParameters = new NameValueCollection();

            MultiPartItems?.ForEach(i => i.Stream?.Dispose());
            MultiPartItems = null;
        }

        private void SendContinueResponse()
        {
            var sb = new StringBuilder();

            sb.Append(Protocol);
            sb.Append(" 100 Continue\r\nServer: ");
            sb.Append(Server.ServerBanner);
            sb.Append("\r\nDate: ");
            sb.Append(DateTime.UtcNow.ToString("R"));
            sb.Append("\r\n\r\n");

            var bytes = Encoding.ASCII.GetBytes(sb.ToString());

            _writeStream?.Dispose();

            _writeStream = new MemoryStream();
            _writeStream.Write(bytes, 0, bytes.Length);
            _writeStream.Position = 0;

            BeginWrite();
        }

        private void WriteCallback()
        {
            if (_disposed) return;

            try
            {
                if (_writeStream != null && _writeStream.Length != _writeStream.Position)
                {
                    // Continue writing from the write stream.
                    BeginWrite();
                }
                else
                {
                    if (_writeStream != null)
                    {
                        _writeStream.Dispose();
                        _writeStream = null;
                    }

                    switch (_state)
                    {
                        case ClientState.WritingHeaders:
                            WriteResponseContent();
                            break;

                        case ClientState.WritingContent:
                            ProcessRequestCompleted();
                            break;

                        default:
                            Debug.Assert(_state != ClientState.Closed);

                            if (ReadBuffer.DataAvailable)
                            {
                                try
                                {
                                    ProcessReadBuffer();
                                }
                                catch (Exception ex)
                                {
                                    ProcessException(ex);
                                }
                            }
                            else BeginRead();
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Info("Failed to write", ex);

                Dispose();
            }
        }

        private void WriteHeader(StringBuilder sb, string key, string value)
        {
            sb.Append(key);
            sb.Append(": ");
            sb.Append(value);
            sb.Append("\r\n");
        }

        private void WriteResponseContent()
        {
            _writeStream?.Dispose();

            _writeStream = _context.Response.OutputStream.BaseStream;
            _writeStream.Position = 0;

            _state = ClientState.WritingContent;

            BeginWrite();
        }

        private void WriteResponseHeaders()
        {
            _writeStream?.Dispose();

            var headers = BuildResponseHeaders();

            if (headers != null)
            {
                _writeStream = new MemoryStream(headers);

                _state = ClientState.WritingHeaders;

                BeginWrite();
            }
            else Dispose();
        }
    }
}