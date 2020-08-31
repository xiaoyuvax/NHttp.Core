namespace NHttp
{
    public class HttpContext
    {
        internal HttpContext(HttpClient client)
        {
            Request = new HttpRequest(client);
            Response = new HttpResponse(this);
        }

        public HttpServer Server { get; private set; }

        public HttpRequest Request { get; private set; }

        public HttpResponse Response { get; private set; }
    }
}