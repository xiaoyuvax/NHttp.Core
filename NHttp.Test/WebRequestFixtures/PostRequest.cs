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
    public class PostRequest : FixtureBase
    {
        private const string ResponseText = "Response text";
        private const int UploadSize = 1 * 1024 * 1024;

        [Test]
        public void SimplePost()
        {
            using (var server = new HttpServer(Logger))
            {
                server.RequestReceived += (s, e) =>
                {
                    Assert.That(e.Request.Form.AllKeys, Is.EquivalentTo(new[] { "key" }));
                    Assert.That(e.Request.Form["key"], Is.EqualTo("value"));
                    Assert.That(e.Request.ContentType, Is.EqualTo("application/x-www-form-urlencoded"));
                    Assert.That(e.Request.HttpMethod, Is.EqualTo("POST"));
                    Assert.That(e.Request.InputStream, Is.Null);

                    using (var writer = new StreamWriter(e.Response.OutputStream))
                    {
                        writer.Write(ResponseText);
                    }
                };

                server.Start();

                var request = (HttpWebRequest)WebRequest.Create(
                    String.Format("http://{0}/", server.EndPoint)
                );

                byte[] postBytes = Encoding.ASCII.GetBytes("key=value");

                request.Method = "POST";
                request.ContentLength = postBytes.Length;
                request.ContentType = "application/x-www-form-urlencoded";

                using (var stream = request.GetRequestStream())
                {
                    stream.Write(postBytes, 0, postBytes.Length);
                }

                Assert.That(GetResponseFromRequest(request), Is.EqualTo(ResponseText));
            }
        }

        [Test]
        public void UploadBinaryData()
        {
            using (var server = new HttpServer(Logger))
            using (var uploadStream = new MemoryStream())
            {
                WriteRandomData(uploadStream, UploadSize);

                server.RequestReceived += (s, e) =>
                {
                    uploadStream.Position = 0;

                    Assert.That(e.Request.InputStream, Is.EqualTo(uploadStream));
                    Assert.That(e.Request.ContentLength, Is.EqualTo(UploadSize));
                    Assert.That(e.Request.ContentType, Is.EqualTo("application/octet-stream"));
                    Assert.That(e.Request.HttpMethod, Is.EqualTo("POST"));
                };

                server.Start();

                var request = (HttpWebRequest)WebRequest.Create(
                    String.Format("http://{0}/", server.EndPoint)
                );

                request.Method = "POST";
                request.ContentType = "application/octet-stream";
                request.ContentLength = UploadSize;

                using (var stream = request.GetRequestStream())
                {
                    uploadStream.Position = 0;

                    uploadStream.CopyTo(stream);
                }

                GetResponseFromRequest(request);
            }
        }

        [Test]
        public void PostWithoutContent()
        {
            using (var server = new HttpServer(Logger))
            {
                server.RequestReceived += (s, e) =>
                {
                    Assert.That(e.Request.HttpMethod, Is.EqualTo("POST"));
                };

                server.Start();

                var request = (HttpWebRequest)WebRequest.Create(
                    String.Format("http://{0}/", server.EndPoint)
                );

                request.Method = "POST";

                GetResponseFromRequest(request);
            }
        }

        [Test]
        public void PostWithContentWithoutType()
        {
            using (var server = new HttpServer(Logger))
            using (var uploadStream = new MemoryStream())
            {
                WriteRandomData(uploadStream, 1 * 1024 * 1024);

                server.RequestReceived += (s, e) =>
                {
                    uploadStream.Position = 0;

                    Assert.That(e.Request.HttpMethod, Is.EqualTo("POST"));
                    Assert.That(e.Request.InputStream, Is.EqualTo(uploadStream));
                    Assert.That(e.Request.ContentType, Is.Null);
                };

                server.Start();

                var request = (HttpWebRequest)WebRequest.Create(
                    String.Format("http://{0}/", server.EndPoint)
                );

                uploadStream.Position = 0;

                request.Method = "POST";
                request.ContentLength = uploadStream.Length;

                using (var stream = request.GetRequestStream())
                {
                    uploadStream.CopyTo(stream);
                }

                GetResponseFromRequest(request);
            }
        }
    }
}
