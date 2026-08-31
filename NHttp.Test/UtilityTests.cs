using System;
using System.Text;
using NUnit.Framework;

namespace NHttp.Test
{
    [TestFixture]
    public class HttpServerUtilityTests
    {
        [Test]
        public void HtmlEncode_EncodesHtmlCharacters()
        {
            string input = "<script>alert('XSS');</script>";
            string expected = "&lt;script&gt;alert(&#39;XSS&#39;);&lt;/script&gt;";

            string result = HttpServerUtility.HtmlEncode(input);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        public void HtmlEncode_EncodesSpecialCharacters()
        {
            string input = "& \" < >";
            string expected = "&amp; &quot; &lt; &gt;";

            string result = HttpServerUtility.HtmlEncode(input);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        public void HtmlDecode_DecodesHtmlEntities()
        {
            string input = "&lt;script&gt;alert(&#39;XSS&#39;);&lt;/script&gt;";
            string expected = "<script>alert('XSS');</script>";

            string result = HttpServerUtility.HtmlDecode(input);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        public void HtmlDecode_DecodesNumericEntities()
        {
            string input = "&#65;&#66;&#67;";
            string expected = "ABC";

            string result = HttpServerUtility.HtmlDecode(input);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        public void UrlEncode_EncodesUrlCharacters()
        {
            string input = "hello world";
            string expected = "hello%20world";

            string result = HttpServerUtility.UrlEncode(input);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        public void UrlDecode_DecodesUrlCharacters()
        {
            string input = "hello%20world";
            string expected = "hello world";

            string result = HttpServerUtility.UrlDecode(input);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        public void UrlDecode_DecodesPlusAsSpace()
        {
            string input = "hello+world";
            string expected = "hello world";

            string result = HttpServerUtility.UrlDecode(input);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        public void UrlDecode_WithEncoding_DecodesUrlCharacters()
        {
            string input = "%E4%B8%AD%E6%96%87";
            string expected = "中文";

            string result = HttpServerUtility.UrlDecode(input, Encoding.UTF8);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        public void HtmlEncode_NullInput_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => HttpServerUtility.HtmlEncode(null));
        }

        [Test]
        public void HtmlDecode_NullInput_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => HttpServerUtility.HtmlDecode(null));
        }

        [Test]
        public void UrlDecode_NullEncoding_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => HttpServerUtility.UrlDecode("test", null));
        }

        [Test]
        public void MachineName_ReturnsEnvironmentMachineName()
        {
            string machineName = HttpServerUtility.MachineName;

            Assert.That(machineName, Is.EqualTo(Environment.MachineName));
        }
    }
}
