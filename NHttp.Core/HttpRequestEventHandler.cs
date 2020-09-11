using System;

namespace NHttp
{
    public class HttpRequestEventArgs : EventArgs
    {
        public HttpRequestEventArgs(HttpContext context)
        {
            if (context == null)
                throw new ArgumentNullException("context");

            Context = context;
        }

        public HttpContext Context { get; private set; }

        public HttpRequest Request => Context.Request;

        public HttpResponse Response => Context.Response;

    }

    public delegate void HttpRequestEventHandler(object sender, HttpRequestEventArgs e);
}