using System;
using System.Net;

namespace NHttp
{
    public interface IHttpServer : IDisposable
    {
        IPEndPoint EndPoint { get; set; }
        bool UseSSL { get; }
        HttpServerState State { get; set; }
        void Start();

        void Stop();
    }
}