# NHttp.Core
This is a core version of NHttp with SSL support forked from the original base,and was implemented in .net Standard 2.0, as enabled this library can be referenced in both .net framework and .net core applications.

LGPL License.

## Introduction

NHttp is a very lightweighted,simply asynchronous Web server which supports Http/Https written in C# for the .NET framework and .NET Core.

It is very ideal for using in lightweighted background program(such as a server program) to expose web admin interface.


NHttp supports the following features:

* Full request parsing similar to the ASP.net model;

* High performance asynchronous request processing using TcpListener/TcpClient;

* Complete query string parsing;

* Complete form parsing (i.e. application/x-www-form-urlencoded);

* Complete multi-part parsing including file upload (i.e. multipart/form-data);

* Support for parsing and sending cookies.

* Support Https protocol & SSL.


NHttp specifically does **not** support any kind of utilities producing output.
It for example does not provide a StreamWriter or perform routing. Besides e.g.
the Headers and Cookies collections, only the raw output stream is provided.
The rest is up to you!

## Usage

The following shows how to use NHttp:

    using (var server = new HttpServer())
    {
        server.RequestReceived += (s, e) =>
        {
            using (var writer = new StreamWriter(e.Response.OutputStream))
            {
                writer.Write("Hello world!");
            }
        };

        server.Start();

        Process.Start(String.Format("http://{0}/", server.EndPoint));

        Console.WriteLine("Press any key to continue...");
        Console.ReadKey();
    }

Processing requests in NHttp is done in the RequestReceived event. There you
have access to the request and response information the request. The example
above creates a StreamReader to be able to write text to the response and
outputs the same response for every request.

By default, NHttp listens to a random port. Use the following method to specify
the port NHttp should listen on:

    using (var server = new HttpServer())
    {
        // ...

        server.EndPoint = new IPEndPoint(IPAddress.Loopback, 80);

        server.Start();

        // ...
    }

This method can also be used to change the interface the HttpServer should be
listening to.

## Logging

This fork of NHttp references one of my other opensource projects - WMLogService for logging, which implements Common.Logging interfaces and can be configured to use 3rd party log modules which implement Common.Logging including popular Log4net. But i think they r just too heavy, and i think my own lighted-weighted log module works better, at least for my own use.

## Bugs

Bugs should be reported through github at
[https://github.com/xiaoyuvax/NHttp.Core/issues](https://github.com/xiaoyuvax/NHttp.Core/issues).

## License

NHttp is licensed under the LGPL 3.
