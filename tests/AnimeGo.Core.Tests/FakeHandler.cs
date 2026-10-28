using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AnimeGo.Core.Tests
{
    // Подставной сервер: отвечает функцией respond и запоминает запросы.
    sealed class FakeHandler : HttpMessageHandler
    {
        readonly Func<HttpRequestMessage, HttpResponseMessage> respond;
        public readonly List<HttpRequestMessage> Requests = new List<HttpRequestMessage>();

        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) { this.respond = respond; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }

        public static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK) =>
            new HttpResponseMessage(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        public static HttpResponseMessage Gzip(string body)
        {
            var ms = new MemoryStream();
            using (var gz = new GZipStream(ms, CompressionMode.Compress, true))
            {
                var b = Encoding.UTF8.GetBytes(body);
                gz.Write(b, 0, b.Length);
            }
            var content = new ByteArrayContent(ms.ToArray());
            content.Headers.ContentEncoding.Add("gzip");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }

        public static HttpResponseMessage Status(HttpStatusCode code) =>
            new HttpResponseMessage(code) { Content = new StringContent("") };
    }

    // Принимает одно TCP-подключение и возвращает сырые байты заголовков запроса.
    static class WireCapture
    {
        public static async Task<string> CaptureAsync(Func<int, Task> sendTo)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var accept = Task.Run(async () =>
            {
                using (var client = await listener.AcceptTcpClientAsync())
                {
                    var s = client.GetStream();
                    var sb = new StringBuilder();
                    var buf = new byte[4096];
                    while (!sb.ToString().Contains("\r\n\r\n"))
                    {
                        var n = await s.ReadAsync(buf, 0, buf.Length);
                        if (n == 0) break;
                        sb.Append(Encoding.ASCII.GetString(buf, 0, n));
                    }
                    var reply = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\n{}");
                    await s.WriteAsync(reply, 0, reply.Length);
                    return sb.ToString();
                }
            });
            try { await sendTo(port); } catch { }
            var raw = await accept;
            listener.Stop();
            return raw;
        }
    }
}
