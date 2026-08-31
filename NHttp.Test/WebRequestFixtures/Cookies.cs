using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using NHttp.Test.Support;
using NUnit.Framework;

namespace NHttp.Test.WebRequestFixtures
{
    [TestFixture]
    public class Cookies : FixtureBase
    {
        private const string DateFormat = "ddd, dd-MMM-yyyy HH':'mm':'ss 'GMT'";
        private static readonly CultureInfo DateCulture = new CultureInfo("en-US");

        [Test]
        public void SetCookie()
        {
            using (var server = new HttpServer(Logger))
            {
                server.RequestReceived += (s, e) =>
                {
                    e.Response.Cookies["a"].Value = "b";
                };

                server.Start();

                var request = (HttpWebRequest)WebRequest.Create(
                    String.Format("http://{0}/", server.EndPoint)
                );

                request.CookieContainer = new CookieContainer();

                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    Assert.That(response.Headers["Set-Cookie"], Is.EqualTo("a=b; path=/"));
                    Assert.That(response.Cookies.Count, Is.EqualTo(1));

                    var cookie = response.Cookies[0];

                    Assert.That(cookie.Name, Is.EqualTo("a"));
                    Assert.That(cookie.Value, Is.EqualTo("b"));
                    Assert.That(cookie.Path, Is.EqualTo("/"));
                    Assert.That(cookie.Domain, Is.EqualTo(server.EndPoint.Address.ToString()));
                    Assert.That(cookie.Expires, Is.EqualTo(DateTime.MinValue));
                    Assert.That(cookie.HttpOnly, Is.EqualTo(false));
                    Assert.That(cookie.Secure, Is.EqualTo(false));
                }
            }
        }

        [Test]
        public void SetCookieWithDetails()
        {
            using (var server = new HttpServer(Logger))
            {
                var expires = DateTime.Now + TimeSpan.FromDays(1);

                expires = expires - TimeSpan.FromMilliseconds(expires.Millisecond);

                server.RequestReceived += (s, e) =>
                {
                    var cookie = new HttpCookie("a", "b");

                    cookie.Path = "/path";
                    cookie.HttpOnly = true;
                    cookie.Secure = true;
                    cookie.Expires = expires;

                    e.Response.Cookies.Add(cookie);
                };

                server.Start();

                var request = (HttpWebRequest)WebRequest.Create(
                    String.Format("http://{0}/path", server.EndPoint)
                );

                request.CookieContainer = new CookieContainer();

                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    Assert.That(response.Headers["Set-Cookie"], Is.EqualTo("a=b; expires=" + expires.ToString(DateFormat, DateCulture) + "; path=/path; secure; HttpOnly"));
 
                    Assert.That(response.Cookies.Count, Is.EqualTo(1));

                    var cookie = response.Cookies[0];

                    Assert.That(cookie.Name, Is.EqualTo("a"));
                    Assert.That(cookie.Value, Is.EqualTo("b"));
                    Assert.That(cookie.Path, Is.EqualTo("/path"));
                    Assert.That(cookie.Expires.ToUniversalTime().ToString(CultureInfo.InvariantCulture), Is.EqualTo(expires.ToString(CultureInfo.InvariantCulture)));
                    Assert.That(cookie.HttpOnly, Is.EqualTo(true));
                    Assert.That(cookie.Secure, Is.EqualTo(true));
                }
            }
        }

        [Test]
        public void SetCookieWithDomain()
        {
            using (var server = new HttpServer(Logger))
            {
                var expires = DateTime.Now + TimeSpan.FromDays(1);

                expires = expires - TimeSpan.FromMilliseconds(expires.Millisecond);

                server.RequestReceived += (s, e) =>
                {
                    var cookie = new HttpCookie("a", "b");

                    cookie.Domain = "example.com";

                    e.Response.Cookies.Add(cookie);
                };

                server.Start();

                var request = (HttpWebRequest)WebRequest.Create(
                    String.Format("http://{0}/path", server.EndPoint)
                );

                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    Assert.That(response.Headers["Set-Cookie"], Is.EqualTo("a=b; domain=example.com; path=/"));
                }
            }
        }

        [Test]
        public void SetMultipleCookies()
        {
            using (var server = new HttpServer(Logger))
            {
                server.RequestReceived += (s, e) =>
                {
                    e.Response.Cookies["a"].Value = "b";
                    e.Response.Cookies["c"].Value = "d";
                };

                server.Start();

                var request = (HttpWebRequest)WebRequest.Create(
                    String.Format("http://{0}/", server.EndPoint)
                );

                request.CookieContainer = new CookieContainer();

                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    Assert.That(response.Headers["Set-Cookie"], Is.EqualTo("a=b; path=/, c=d; path=/"));

                    Assert.That(response.Cookies.Count, Is.EqualTo(2));

                    for (int i = 0; i < response.Cookies.Count; i++)
                    {
                        var cookie = response.Cookies[i];

                        switch (i)
                        {
                            case 0:
                                Assert.That(cookie.Name, Is.EqualTo("a"));
                                Assert.That(cookie.Value, Is.EqualTo("b"));
                                break;

                            case 1:
                                Assert.That(cookie.Name, Is.EqualTo("c"));
                                Assert.That(cookie.Value, Is.EqualTo("d"));
                                break;
                        }
                    }
                }
            }
        }

        [Test]
        public void CookieRoundTrip()
        {
            TestCookieRoundtrip(
                p =>
                {
                    p.Response.Cookies["a"].Value = "b";
                },
                p =>
                {
                    Assert.That(p.Request.Cookies.AllKeys, Is.EquivalentTo(new[] { "a" }));
                    Assert.That(p.Request.Cookies["a"].Value, Is.EqualTo("b"));
                }
            );
        }

        [Test]
        public void DuplicateCookiesRoundTrip()
        {
            TestCookieRoundtrip(
                p =>
                {
                    p.Response.Cookies.Add(new HttpCookie("a", "b"));
                    p.Response.Cookies.Add(new HttpCookie("a", "c"));
                },
                p =>
                {
                    Assert.That(p.Request.Cookies.AllKeys, Is.EquivalentTo(new[] { "a" }));
                    Assert.That(p.Request.Cookies["a"].Value, Is.EqualTo("c"));
                }
            );
        }

        [Test]
        public void ValueToCollectionRoundTrip()
        {
            TestCookieRoundtrip(
                p =>
                {
                    p.Response.Cookies["a"].Value = "a=b&&c";
                },
                p =>
                {
                    Assert.That(p.Request.Cookies.AllKeys, Is.EquivalentTo(new[] { "a" }));
                    Assert.That(p.Request.Cookies["a"].Values.AllKeys, Is.EquivalentTo(new[] { "a", null, "c" }));
                    Assert.That(p.Request.Cookies["a"].Values["a"], Is.EqualTo("b"));
                    Assert.That(p.Request.Cookies["a"].Values["c"], Is.EqualTo(null));
                }
            );
        }

        [Test]
        public void CollectionToCollectionRoundTrip()
        {
            TestCookieRoundtrip(
                p =>
                {
                    p.Response.Cookies["a"].Values["a"] = "b";
                    p.Response.Cookies["a"].Values["c"] = "d";
                },
                p =>
                {
                    Assert.That(p.Request.Cookies.AllKeys, Is.EquivalentTo(new[] { "a" }));
                    Assert.That(p.Request.Cookies["a"].Values.AllKeys, Is.EquivalentTo(new[] { "a", "c" }));
                    Assert.That(p.Request.Cookies["a"].Values["a"], Is.EqualTo("b"));
                    Assert.That(p.Request.Cookies["a"].Values["c"], Is.EqualTo("d"));
                }
            );
        }

        [Test]
        public void InvalidlyParsedCollectionRoundTrip()
        {
            TestCookieRoundtrip(
                p =>
                {
                    p.Response.Cookies["a"].Values["a"] = "b&c=d";
                },
                p =>
                {
                    Assert.That(p.Request.Cookies.AllKeys, Is.EquivalentTo(new[] { "a" }));
                    Assert.That(p.Request.Cookies["a"].Values.AllKeys, Is.EquivalentTo(new[] { "a", "c" }));
                    Assert.That(p.Request.Cookies["a"].Values["a"], Is.EqualTo("b"));
                    Assert.That(p.Request.Cookies["a"].Values["c"], Is.EqualTo("d"));
                }
            );
        }

        private void TestCookieRoundtrip(Action<HttpContext> fromCallback, Action<HttpContext> toCallback)
        {
            using (var server = new HttpServer(Logger))
            {
                server.RequestReceived += (s, e) =>
                {
                    switch (e.Request.Path)
                    {
                        case "/from":
                            fromCallback(e.Context);

                            e.Response.RedirectPermanent("/to");
                            break;

                        case "/to":
                            toCallback(e.Context);
                            break;
                    }
                };

                server.Start();

                var request = (HttpWebRequest)WebRequest.Create(
                    String.Format("http://{0}/from", server.EndPoint)
                );

                request.CookieContainer = new CookieContainer();

                GetResponseFromRequest(request);
            }
        }
    }
}
