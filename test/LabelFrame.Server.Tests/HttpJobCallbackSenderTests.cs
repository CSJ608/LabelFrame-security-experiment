using System.Net;
using System.Net.Sockets;
using System.Text;
using LabelFrame.Server;

namespace LabelFrame.Server.Tests;

/// <summary>
/// HttpJobCallbackSender 真实套接字测试（安全边界的行为锚点）：
/// 2xx 判成功（请求形态 application/json + POST）、不跟随重定向（302 只收到一次请求且判失败）、
/// 非 2xx 判失败（错误消息含状态码）。投递超时为常量（10 秒），真实等待不在单测覆盖内。
/// </summary>
public sealed class HttpJobCallbackSenderTests : IDisposable
{
    private readonly MiniHttpServer _server = new();

    [Fact]
    public async Task Two_xx_response_should_be_delivered()
    {
        using var sender = new HttpJobCallbackSender();
        var result = await sender.SendAsync(_server.Url("/cb"), """{"jobId":"j-1"}""", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(result.Error);
        var request = Assert.Single(_server.Requests);
        Assert.Equal("POST /cb HTTP/1.1", request.StartLine);
        Assert.Equal("""{"jobId":"j-1"}""", request.Body);
        Assert.Contains("application/json", request.ContentType);
    }

    [Fact]
    public async Task Redirect_should_not_be_followed_and_counts_as_failure()
    {
        _server.RespondStatus = 302;
        using var sender = new HttpJobCallbackSender();

        var result = await sender.SendAsync(_server.Url("/cb"), "{}", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("302", result.Error);
        // 不跟随重定向：只收到一次请求（跟随重定向会出现第二次对 Location 的请求）
        Assert.Single(_server.Requests);
    }

    [Fact]
    public async Task Server_error_should_count_as_failure_with_status_code()
    {
        _server.RespondStatus = 500;
        using var sender = new HttpJobCallbackSender();

        var result = await sender.SendAsync(_server.Url("/cb"), "{}", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("500", result.Error);
        Assert.Single(_server.Requests);
    }

    [Fact]
    public async Task Unreachable_endpoint_should_count_as_failure()
    {
        using var sender = new HttpJobCallbackSender();

        // 端口 1 保留段：连接立即被拒绝（不产生真实外联）
        var result = await sender.SendAsync("http://127.0.0.1:1/cb", "{}", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("不可达", result.Error);
    }

    public void Dispose() => _server.Dispose();

    /// <summary>真实套接字微型 HTTP 服务（TcpListener）：按 RespondStatus 回包，记录收到的请求。</summary>
    private sealed class MiniHttpServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();

        public MiniHttpServer()
        {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        BaseUrl = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";
        _ = AcceptLoopAsync();
    }

    public string BaseUrl { get; }

        public List<RecordedRequest> Requests { get; } = [];

        /// <summary>响应状态码（默认 200；302 附 Location 头）。</summary>
        public int RespondStatus { get; set; } = 200;

        public string Url(string path) => BaseUrl + path;

        private async Task AcceptLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(_cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (SocketException)
                {
                    break;
                }

                _ = HandleAsync(client);
            }
        }

        private async Task HandleAsync(TcpClient client)
        {
            try
            {
                using (client)
                {
                    var stream = client.GetStream();
                    var received = new List<byte>();
                    var buffer = new byte[8192];
                    string headerText = string.Empty;
                    var headerEnd = -1;
                    var contentLength = 0;

                    // 读满「请求头 + Content-Length 指定的请求体」
                    while (received.Count < 64 * 1024)
                    {
                        var read = await stream.ReadAsync(buffer, _cts.Token);
                        if (read == 0)
                        {
                            break;
                        }

                        received.AddRange(buffer.AsSpan(0, read).ToArray());
                        var text = Encoding.UTF8.GetString(received.ToArray());
                        if (headerEnd < 0)
                        {
                            headerEnd = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                            if (headerEnd < 0)
                            {
                                continue;
                            }

                            headerText = text[..headerEnd];
                            contentLength = ParseContentLength(headerText);
                        }

                        if (received.Count >= headerEnd + 4 + contentLength)
                        {
                            break;
                        }
                    }

                    var startLine = headerText.Split("\r\n")[0];
                    var contentType = headerText.Split("\r\n")
                        .FirstOrDefault(l => l.StartsWith("Content-Type:", StringComparison.OrdinalIgnoreCase)) ?? "";
                    var body = headerEnd >= 0
                        ? Encoding.UTF8.GetString(received.Skip(headerEnd + 4).Take(contentLength).ToArray())
                        : string.Empty;
                    Requests.Add(new RecordedRequest(startLine, contentType, body));

                    var statusLine = RespondStatus switch
                    {
                        302 => "HTTP/1.1 302 Found",
                        500 => "HTTP/1.1 500 Internal Server Error",
                        _ => "HTTP/1.1 200 OK",
                    };
                    var headers = RespondStatus == 302
                        ? "Location: /elsewhere\r\n"
                        : string.Empty;
                    var response = Encoding.UTF8.GetBytes($"{statusLine}\r\n{headers}Content-Length: 2\r\nConnection: close\r\n\r\nok");
                    await stream.WriteAsync(response, _cts.Token);
                    await stream.FlushAsync(_cts.Token);
                }
            }
            catch
            {
                // 测试服务器：吞连接级异常（客户端超时 / 中断不影响断言）
            }
        }

        private static int ParseContentLength(string headerText)
        {
            foreach (var line in headerText.Split("\r\n"))
            {
                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(line["Content-Length:".Length..].Trim(), out var length))
                {
                    return length;
                }
            }

            return 0;
        }

        public void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
            _listener.Stop();
        }

        public sealed record RecordedRequest(string StartLine, string ContentType, string Body);
    }
}
