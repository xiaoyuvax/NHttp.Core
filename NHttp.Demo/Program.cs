using System;
using System.IO;
using System.Net;
using Common.Logging;
using Common.Logging.Simple;

namespace NHttp.Demo
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("NHttp Demo Server");
            Console.WriteLine("=================");
            Console.WriteLine();

            using (var server = new HttpServer(new NoOpLogger()))
            {
                // Configure server settings
                server.ReadTimeout = TimeSpan.FromSeconds(90);
                server.WriteTimeout = TimeSpan.FromSeconds(90);
                server.ShutdownTimeout = TimeSpan.FromSeconds(5);

                // Set specific port (optional)
                // server.EndPoint = new IPEndPoint(IPAddress.Loopback, 8080);

                // Handle unhandled exceptions
                server.UnhandledException += (s, e) =>
                {
                    Console.WriteLine($"Exception: {e.Exception.Message}");
                    e.Handled = true;
                    e.Response.Status = "500 Internal Server Error";
                    using (var writer = new StreamWriter(e.Response.OutputStream))
                    {
                        writer.Write("Internal Server Error");
                    }
                };

                // Handle requests
                server.RequestReceived += (s, e) =>
                {
                    var request = e.Request;
                    var response = e.Response;

                    Console.WriteLine($"{DateTime.Now:HH:mm:ss} {request.HttpMethod} {request.RawUrl}");

                    // Route requests
                    string path = request.Url?.AbsolutePath?.ToLower() ?? "/";

                    try
                    {
                        switch (path)
                        {
                            case "/":
                                HandleRoot(response);
                                break;

                            case "/form":
                                HandleForm(request, response);
                                break;

                            case "/upload":
                                HandleUpload(request, response);
                                break;

                            case "/headers":
                                HandleHeaders(request, response);
                                break;

                            case "/cookies":
                                HandleCookies(request, response);
                                break;

                            case "/json":
                                HandleJson(response);
                                break;

                            default:
                                HandleNotFound(response, path);
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error handling request: {ex.Message}");
                        response.Status = "500 Internal Server Error";
                        using (var writer = new StreamWriter(response.OutputStream))
                        {
                            writer.Write($"Error: {ex.Message}");
                        }
                    }
                };

                server.Start();

                Console.WriteLine($"Server started at: http://{server.EndPoint}");
                Console.WriteLine();
                Console.WriteLine("Available endpoints:");
                Console.WriteLine("  /          - Home page");
                Console.WriteLine("  /form      - Form submission example");
                Console.WriteLine("  /upload    - File upload example");
                Console.WriteLine("  /headers   - Show request headers");
                Console.WriteLine("  /cookies   - Cookie demonstration");
                Console.WriteLine("  /json      - JSON response example");
                Console.WriteLine();
                Console.WriteLine("Press any key to stop the server...");
                Console.ReadKey();
            }

            Console.WriteLine("Server stopped.");
        }

        static void HandleRoot(HttpResponse response)
        {
            response.ContentType = "text/html";
            using (var writer = new StreamWriter(response.OutputStream))
            {
                writer.Write(@"
<!DOCTYPE html>
<html>
<head>
    <title>NHttp Demo</title>
    <style>
        body { font-family: Arial, sans-serif; margin: 40px; }
        h1 { color: #333; }
        a { color: #0066cc; text-decoration: none; }
        a:hover { text-decoration: underline; }
        .endpoint { margin: 10px 0; padding: 10px; background: #f5f5f5; }
    </style>
</head>
<body>
    <h1>Welcome to NHttp Demo Server</h1>
    <p>This is a lightweight HTTP server written in C#.</p>
    
    <h2>Endpoints:</h2>
    <div class='endpoint'><a href='/form'>/form</a> - Form submission example</div>
    <div class='endpoint'><a href='/upload'>/upload</a> - File upload example</div>
    <div class='endpoint'><a href='/headers'>/headers</a> - Show request headers</div>
    <div class='endpoint'><a href='/cookies'>/cookies</a> - Cookie demonstration</div>
    <div class='endpoint'><a href='/json'>/json</a> - JSON response example</div>
</body>
</html>");
            }
        }

        static void HandleForm(HttpRequest request, HttpResponse response)
        {
            response.ContentType = "text/html";
            using (var writer = new StreamWriter(response.OutputStream))
            {
                if (request.HttpMethod == "POST")
                {
                    writer.Write("<h1>Form Submitted</h1>");
                    writer.Write("<h2>Form Data:</h2>");
                    writer.Write("<ul>");
                    foreach (string key in request.Form.AllKeys)
                    {
                        writer.Write($"<li><strong>{key}:</strong> {request.Form[key]}</li>");
                    }
                    writer.Write("</ul>");
                    writer.Write("<p><a href='/form'>Back to form</a></p>");
                }
                else
                {
                    writer.Write(@"
<h1>Form Example</h1>
<form method='post'>
    <div>
        <label>Name:</label>
        <input type='text' name='name' />
    </div>
    <div>
        <label>Email:</label>
        <input type='email' name='email' />
    </div>
    <div>
        <label>Message:</label>
        <textarea name='message'></textarea>
    </div>
    <button type='submit'>Submit</button>
</form>");
                }
            }
        }

        static void HandleUpload(HttpRequest request, HttpResponse response)
        {
            response.ContentType = "text/html";
            using (var writer = new StreamWriter(response.OutputStream))
            {
                if (request.HttpMethod == "POST" && request.Files.Count > 0)
                {
                    writer.Write("<h1>Files Uploaded</h1>");
                    writer.Write("<ul>");
                    foreach (string fileName in request.Files.AllKeys)
                    {
                        var file = request.Files[fileName];
                        writer.Write($"<li><strong>{fileName}:</strong> {file.FileName} ({file.ContentLength} bytes)</li>");
                    }
                    writer.Write("</ul>");
                    writer.Write("<p><a href='/upload'>Back to upload</a></p>");
                }
                else
                {
                    writer.Write(@"
<h1>File Upload Example</h1>
<form method='post' enctype='multipart/form-data'>
    <div>
        <label>Select files:</label>
        <input type='file' name='files' multiple />
    </div>
    <button type='submit'>Upload</button>
</form>");
                }
            }
        }

        static void HandleHeaders(HttpRequest request, HttpResponse response)
        {
            response.ContentType = "text/html";
            using (var writer = new StreamWriter(response.OutputStream))
            {
                writer.Write("<h1>Request Headers</h1>");
                writer.Write("<table border='1' cellpadding='5'>");
                writer.Write("<tr><th>Header</th><th>Value</th></tr>");
                foreach (string key in request.Headers.AllKeys)
                {
                    writer.Write($"<tr><td>{key}</td><td>{request.Headers[key]}</td></tr>");
                }
                writer.Write("</table>");
            }
        }

        static void HandleCookies(HttpRequest request, HttpResponse response)
        {
            response.ContentType = "text/html";
            using (var writer = new StreamWriter(response.OutputStream))
            {
                writer.Write("<h1>Cookie Example</h1>");

                // Set a cookie
                response.Cookies.Add(new HttpCookie("DemoCookie", DateTime.Now.ToString("HH:mm:ss"))
                {
                    Path = "/",
                    HttpOnly = true
                });

                writer.Write("<h2>Request Cookies:</h2>");
                writer.Write("<ul>");
                foreach (string key in request.Cookies.AllKeys)
                {
                    writer.Write($"<li><strong>{key}:</strong> {request.Cookies[key].Value}</li>");
                }
                writer.Write("</ul>");
                writer.Write("<p><em>A new cookie has been set for the next request.</em></p>");
            }
        }

        static void HandleJson(HttpResponse response)
        {
            response.ContentType = "application/json";
            using (var writer = new StreamWriter(response.OutputStream))
            {
                writer.Write($@"{{
    ""message"": ""Hello from NHttp!"",
    ""timestamp"": ""{DateTime.UtcNow:O}"",
    ""server"": ""NHttp Demo""
}}");
            }
        }

        static void HandleNotFound(HttpResponse response, string path)
        {
            response.Status = "404 Not Found";
            response.ContentType = "text/html";
            using (var writer = new StreamWriter(response.OutputStream))
            {
                writer.Write($"<h1>404 - Not Found</h1><p>The path '{path}' was not found.</p>");
            }
        }
    }
}
