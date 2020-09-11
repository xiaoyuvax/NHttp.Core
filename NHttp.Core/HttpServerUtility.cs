using System;
using System.Text;

namespace NHttp
{
    public static class HttpServerUtility
    {
        
        public static string MachineName => Environment.MachineName;

        public static string HtmlEncode(string value) => HttpUtil.HtmlEncode(value);

        public static string HtmlDecode(string value) => HttpUtil.HtmlDecode(value);

        public static string UrlEncode(string text) => Uri.EscapeDataString(text);

        public static string UrlDecode(string text) => UrlDecode(text, Encoding.UTF8);

        public static string UrlDecode(string text, Encoding encoding)
        {
            if (encoding == null)
                throw new ArgumentNullException("encoding");

            return HttpUtil.UriDecode(text, encoding);
        }
    }
}