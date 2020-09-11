using System;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Text;

namespace NHttp
{
    public class HttpResponse
    {
        private readonly HttpContext _context;
        internal HttpResponse(HttpContext context)
        {
            if (context == null)
                throw new ArgumentNullException("context");

            _context = context;

            CacheControl = "private";
            CharSet = "utf-8";
            ContentType = "text/html";
            ExpiresAbsolute = DateTime.MinValue;
            HeadersEncoding = Encoding.UTF8;
            Headers = new NameValueCollection();
            OutputStream = new HttpOutputStream(new MemoryStream());
            StatusCode = 200;
            StatusDescription = "OK";
            Cookies = new HttpCookieCollection();
        }

        public string CacheControl { get; set; }

        public string CharSet { get; set; }

        public string ContentType { get; set; }

        public HttpCookieCollection Cookies { get; private set; }

        public DateTime ExpiresAbsolute { get; set; }

        public NameValueCollection Headers { get; private set; }
        public Encoding HeadersEncoding { get; set; }
        public bool IsClientConnected { get { return true; } }

        public bool IsRequestBeingRedirected => !string.IsNullOrEmpty(RedirectLocation);

        public HttpOutputStream OutputStream { get; private set; }

        public string RedirectLocation { get; set; }

        public string Status
        {
            get => StatusDescription == null ?
                     StatusCode.ToString(CultureInfo.InvariantCulture)
                    : StatusCode.ToString(CultureInfo.InvariantCulture) + " " + StatusDescription;
            set
            {
                StatusCode = 0;
                StatusDescription = null;

                if (value != null)
                {
                    string[] parts = value.Split(new[] { ' ' }, 2);

                    if (int.TryParse(parts[0], out int statusCode))
                    {
                        StatusCode = statusCode;
                        StatusDescription = parts.Length == 2 ? parts[1] : StatusDescription = null;
                    }
                }
            }
        }

        public int StatusCode { get; set; }

        public string StatusDescription { get; set; }
        public void Redirect(string location) => Redirect(location, false);

        public void RedirectPermanent(string location) => Redirect(location, true);

        private void Redirect(string location, bool permanent)
        {
            if (location == null)
                throw new ArgumentNullException("location");

            if (!location.Contains(":"))
            {
                var sb = new StringBuilder();
                var url = _context.Request.Url;

                sb.Append(url.Scheme);
                sb.Append("://");
                sb.Append(url.Host);

                if (!url.IsDefaultPort)
                {
                    sb.Append(':');
                    sb.Append(url.Port);
                }

                if (location.Length == 0) location = _context.Request.Path;

                if (location[0] == '/') sb.Append(location);
                else
                {
                    string path = _context.Request.Path;
                    int pos = path.LastIndexOf('/');

                    if (pos == -1)
                        sb.Append('/');
                    else
                        sb.Append(path.Substring(0, pos + 1));

                    sb.Append(location);
                }

                location = sb.ToString();
            }

            RedirectLocation = location;
            StatusCode = permanent ? 301 : 302;
            StatusDescription = "Moved";
        }
    }
}