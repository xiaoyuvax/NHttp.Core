using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using NHttp.Test.Support;
using NUnit.Framework;

namespace NHttp.Test.WebRequestFixtures
{
    [TestFixture]
    public class BasicRequest : FixtureBase
    {
        private const string ResponseText = "Response text";

        [Test]
        public void SingleRequest()
        {
            using (var server = new HttpServer(Logger))
            {
                server.RequestReceived += (s, e) =>
                {
                    Assert.That(e.Request.QueryString.AllKeys, Is.EquivalentTo(new[] { "key" }));
                    Assert.That(e.Request.QueryString["key"], Is.EqualTo("value"));

                    using (var writer = new StreamWriter(e.Response.OutputStream))
                    {
                        writer.Write(ResponseText);
                    }
                };

                server.Start();

                var request = (HttpWebRequest)WebRequest.Create(
                    String.Format("http://{0}/?key=value", server.EndPoint)
                );

                Assert.That(GetResponseFromRequest(request), Is.EqualTo(ResponseText));
            }
        }

        [Test]
        public void QueryStringKeyWithMultipleValues()
        {
            using (var server = new HttpServer(Logger))
            {
                server.RequestReceived += (s, e) =>
                {
                    Assert.That(e.Request.QueryString.AllKeys, Is.EquivalentTo(new[] { "key" }));
                    Assert.That(e.Request.QueryString.GetValues("key"), Is.EquivalentTo(new[] { "first", "second", "third" }));
                    Assert.That("first,second,third", Is.EqualTo(e.Request.QueryString["key"]));

                    using (var writer = new StreamWriter(e.Response.OutputStream))
                    {
                        writer.Write(ResponseText);
                    }
                };

                server.Start();

                var request = (HttpWebRequest)WebRequest.Create(
                    String.Format("http://{0}/?key=first&key=second&key=third", server.EndPoint)
                );

                Assert.That(GetResponseFromRequest(request), Is.EqualTo(ResponseText));
            }
        }

        [Test]
        public void NoInputStreamWithGet()
        {
            using (var server = new HttpServer(Logger))
            {
                server.RequestReceived += (s, e) => Assert.That(e.Request.InputStream, Is.Null);

                server.Start();

                var request = (HttpWebRequest)WebRequest.Create(
                    String.Format("http://{0}/", server.EndPoint)
                );

                GetResponseFromRequest(request);
            }
        }
    }
}
