using System.Text;
using System.Net.Sockets;
using System.Text.Json;
using Tarui.Contracts;
using Tarui.Ipc;
using Tarui.Plugins.Http;
using HttpRequestOptions = Tarui.Contracts.HttpRequestOptions;

namespace Tarui.Http.Tests;

internal static class Program
{
    public static async Task<int> Main()
    {
        UrlScopeMatcherEnforcesHostPortAndPathRules();
        await FetchesInlineBodyUnderLimitAsync();
        await RequiresStreamWhenBodyExceedsInlineLimitAsync();
        await DeniesFetchOutsideScopeAsync();
        await DeniesRedirectEscapingScopeAsync();
        await FollowsRedirectWithinScopeAsync();
        await DoesNotFollowRedirectToOutOfScopeTargetAsync();
        await DeniesUrlWithoutAnyAllowScopeAsync();
        await EnforcesSchemeWhitelistAsync();
        await StreamsLargeResponseThroughChannelAsync();
        await StreamsPreserveRedirectScopeAsync();
        await UploadsMultipartBodyToScopedUrlAsync();
        await DeniesUploadWithoutScopeAsync();
        Console.WriteLine("Tarui.Http self-tests passed.");
        return 0;
    }

    private static void UrlScopeMatcherEnforcesHostPortAndPathRules()
    {
        Assert(Allows("http://api.example.com/v1/**", "http://api.example.com/v1/users"),
            "A ** path glob must cover any depth.");
        Assert(!Allows("http://api.example.com/*", "http://api.example.com/v1/users"),
            "A single * must not span '/'.");
        Assert(Allows("http://*.example.com/**", "http://a.b.example.com/x"),
            "A host prefix wildcard must match any subdomain.");
        Assert(!Allows("http://*.example.com/**", "http://example.com/x"),
            "A host prefix wildcard must not match the bare domain.");
        Assert(Allows("https://api.example.com/**", "https://api.example.com/x"),
            "A pattern without an explicit port must allow the scheme default port.");
        Assert(!Allows("http://api.example.com/**", "http://api.example.com:8080/x"),
            "A pattern without a port must reject a non-default port.");
        Assert(Allows("http://127.0.0.1:8080/*", "http://127.0.0.1:8080/ping"),
            "An explicit port must match exactly.");
        Assert(!Allows("http://anything.example.com/**", "https://anything.example.com/x"),
            "Schemes must match exactly.");
        Assert(!Allows(allow: "http://*/*", deny: "http://127.0.0.1/secret*",
                url: "http://127.0.0.1/secret/x"),
            "Deny must win over allow.");
        Assert(!Allows(allow: null, deny: null, url: "http://127.0.0.1/x"),
            "An empty allow list must deny by default.");
    }

    private static async Task FetchesInlineBodyUnderLimitAsync()
    {
        await using var server = new FakeHttpServer(request => request.Path switch
        {
            "/greet" => new HttpResponse(200, [new HttpHeader("Content-Type", "text/plain")], "hello"),
            _ => new HttpResponse(404, [], "not found"),
        });
        var dispatcher = NewDispatcher(new HttpService(new HttpServiceOptions()));
        string[] allow = [$"http://127.0.0.1:{server.Port}/**"];

        var ok = await DispatchFetch(dispatcher, server, "/greet", allow);
        Assert(ok!.Success, "A scoped fetch must succeed.");
        var result = ok.Payload!.Value.Deserialize(TaruiJsonContext.Default.HttpResponseResult)!;
        Assert(result.Status == 200, $"The status must be 200, got {result.Status}.");
        Assert(result.Body == "hello", "The response body must round-trip.");
        Assert(result.Headers.Any(h => h.Name == "Content-Type"), "The content headers must be surfaced.");

        var missing = await DispatchFetch(dispatcher, server, "/missing", allow);
        Assert(missing!.Success, "A 404 response is still a successful fetch.");
        Assert(missing.Payload!.Value.Deserialize(TaruiJsonContext.Default.HttpResponseResult)!.Status == 404,
            "A 404 must be surfaced as status 404, not an error.");
    }

    private static async Task RequiresStreamWhenBodyExceedsInlineLimitAsync()
    {
        await using var server = new FakeHttpServer(_ => new HttpResponse(200, [], new string('x', 4096)));
        var dispatcher = NewDispatcher(new HttpService(new HttpServiceOptions(MaxInlineBytes: 100)));

        var response = await DispatchFetch(dispatcher, server, "/big", [$"http://127.0.0.1:{server.Port}/**"]);
        Assert(!response!.Success, "A body above the inline limit must fail without a channel.");
        Assert(response.Error?.Code == "COMMAND_FAILED", "An oversize inline body must surface as a command failure.");
    }

    private static async Task DeniesFetchOutsideScopeAsync()
    {
        await using var server = new FakeHttpServer(_ => new HttpResponse(200, [], "ok"));
        var dispatcher = NewDispatcher(new HttpService(new HttpServiceOptions()));

        // 允许作用域指向另一个端口，本端口请求必被拒。
        var response = await DispatchFetch(dispatcher, server, "/x", ["http://127.0.0.1:9/**"]);
        Assert(!response!.Success, "A URL outside the allow scope must be denied.");
        Assert(response.Error?.Code == "SCOPE_DENIED", "An out-of-scope URL must surface as SCOPE_DENIED.");
    }

    private static async Task DeniesRedirectEscapingScopeAsync()
    {
        await using var server = new FakeHttpServer(request => request.Path switch
        {
            "/allowed/start" => new HttpResponse(302, [new HttpHeader("Location", "/forbidden/target")], ""),
            _ => new HttpResponse(200, [], "leak"),
        });
        var dispatcher = NewDispatcher(new HttpService(new HttpServiceOptions()));

        var response = await DispatchFetch(dispatcher, server, "/allowed/start",
            allow: [$"http://127.0.0.1:{server.Port}/allowed/**"],
            deny: [$"http://127.0.0.1:{server.Port}/forbidden/**"]);
        Assert(!response!.Success, "A redirect that leaves the scope must be denied.");
        Assert(response.Error?.Code == "SCOPE_DENIED", "An escaping redirect must surface as SCOPE_DENIED.");
    }

    private static async Task DoesNotFollowRedirectToOutOfScopeTargetAsync()
    {
        await using var server = new FakeHttpServer(request => request.Path switch
        {
            "/landing" => new HttpResponse(302, [new HttpHeader("Location", "http://127.0.0.1:1/out")], ""),
            _ => new HttpResponse(200, [], "unexpected"),
        });
        var dispatcher = NewDispatcher(new HttpService(new HttpServiceOptions()));

        // 初始 URL 在允许范围内，但重定向跳到一个显式越界地址，必须被拦截且绝不向其发起请求。
        var response = await DispatchFetch(dispatcher, server, "/landing",
            allow: [$"http://127.0.0.1:{server.Port}/**"]);
        Assert(!response!.Success, "A redirect to an explicit out-of-scope absolute URL must be denied.");
        Assert(response.Error?.Code == "SCOPE_DENIED", "The out-of-scope redirect must surface as SCOPE_DENIED.");
    }

    private static async Task FollowsRedirectWithinScopeAsync()
    {
        await using var server = new FakeHttpServer(request => request.Path switch
        {
            "/landing" => new HttpResponse(302, [new HttpHeader("Location", "/ok")], ""),
            _ => new HttpResponse(200, [], "done"),
        });
        var dispatcher = NewDispatcher(new HttpService(new HttpServiceOptions()));

        var response = await DispatchFetch(dispatcher, server, "/landing",
            allow: [$"http://127.0.0.1:{server.Port}/**"]);
        Assert(response!.Success, "A redirect within scope must be followed to a 200.");
        var result = response.Payload!.Value.Deserialize(TaruiJsonContext.Default.HttpResponseResult)!;
        Assert(result.Body == "done", "The final redirect target's body must be returned.");
    }

    private static async Task DeniesUrlWithoutAnyAllowScopeAsync()
    {
        await using var server = new FakeHttpServer(_ => new HttpResponse(200, [], "ok"));
        var dispatcher = NewDispatcher(new HttpService(new HttpServiceOptions()));
        var caps = new CapabilitySet([HttpPlugin.FetchCommand]); // 裸权限，无 URL 作用域

        var response = await DispatchRaw(dispatcher, caps, "GET", $"{server.BaseUrl}/x");
        Assert(!response!.Success, "A bare permission with no URL scope must default to denied.");
        Assert(response.Error?.Code == "SCOPE_DENIED", "The default-deny must surface as SCOPE_DENIED.");
    }

    private static async Task EnforcesSchemeWhitelistAsync()
    {
        await using var server = new FakeHttpServer(_ => new HttpResponse(200, [], "ok"));
        var dispatcher = NewDispatcher(new HttpService(new HttpServiceOptions()));

        var response = await DispatchRaw(
            dispatcher,
            HttpCapability([$"http://127.0.0.1:{server.Port}/**"]),
            "GET",
            "file:///etc/passwd");
        Assert(!response!.Success, "A non-http(s) scheme must be rejected.");
        Assert(response.Error is not null, "A disallowed scheme must surface an error code.");
        Assert(response.Error!.Code is "SCOPE_DENIED" or "COMMAND_FAILED",
            "A file:// URL must be denied either by scope or by the scheme whitelist.");
    }

    private static async Task StreamsPreserveRedirectScopeAsync()
    {
        await using var server = new FakeHttpServer(request => request.Path switch
        {
            "/landing" => new HttpResponse(302, [new HttpHeader("Location", "/ok")], ""),
            _ => new HttpResponse(200, [], "streamed"),
        });
        var dispatcher = NewDispatcher(new HttpService(new HttpServiceOptions()));
        var channel = new RecordingChannelSink();

        var (response, sink) = await DispatchFetchStream(dispatcher, server, "/landing", "chan-redirect", channel,
            allow: [$"http://127.0.0.1:{server.Port}/**"]);
        Assert(response!.Success, "A streamed fetch across a scoped redirect must succeed.");
        var body = ReassembleStream(sink.Frames, out var status);
        Assert(status == 200, "The streamed status must reflect the final redirect target.");
        Assert(body == "streamed", "The streamed body must reflect the final redirect target.");
    }

    private static async Task StreamsLargeResponseThroughChannelAsync()
    {
        var payload = new string('z', 700_000);
        await using var server = new FakeHttpServer(_ =>
            new HttpResponse(200, [new HttpHeader("Content-Type", "application/octet-stream")], payload));
        var dispatcher = NewDispatcher(new HttpService(new HttpServiceOptions(StreamChunkBytes: 200_000)));
        var channel = new RecordingChannelSink();

        var (response, frames) = await DispatchFetchStream(dispatcher, server, "/big", "chan-http", channel,
            allow: [$"http://127.0.0.1:{server.Port}/**"]);
        Assert(response!.Success, "A streamed fetch must succeed.");

        Assert(frames.Frames.Count > 2, "A body larger than the chunk size must stream several frames.");
        Assert(frames.Frames.Skip(1).All(frame => frame.Deserialize(TaruiJsonContext.Default.HttpStreamEvent)!.Kind == "chunk"),
            "Frames after meta must all be chunks.");
        Assert(ReassembleStream(frames.Frames, out _) == payload, "The streamed bytes must round-trip exactly.");
    }

    private static async Task UploadsMultipartBodyToScopedUrlAsync()
    {
        HttpRequest? received = null;
        await using var server = new FakeHttpServer(request =>
        {
            received = request;
            return new HttpResponse(200, [new HttpHeader("Content-Type", "text/plain")], "uploaded");
        });
        var dispatcher = NewDispatcher(new HttpService(new HttpServiceOptions()));
        var options = new HttpUploadOptions(
            server.BaseUrl + "/inbox",
            Fields: [new HttpField("note", "hello-tarui")],
            Files: [new HttpFilePart("file", "readme.txt", System.Text.Encoding.UTF8.GetBytes("file-contents"))]);

        var response = await DispatchUpload(dispatcher, server, options, [$"http://127.0.0.1:{server.Port}/**"]);
        Assert(response!.Success, "A scoped upload must succeed.");
        var result = response.Payload!.Value.Deserialize(TaruiJsonContext.Default.HttpUploadResult)!;
        Assert(result.Status == 200 && result.Body == "uploaded", "The upload response must round-trip.");
        Assert(received?.Method == "POST", "The upload must be sent as a POST.");
        Assert(
            received is { Body: not null } &&
            received.Body.Contains("hello-tarui", StringComparison.Ordinal) &&
            received.Body.Contains("file-contents", StringComparison.Ordinal) &&
            received.Body.Contains("Content-Disposition", StringComparison.Ordinal),
            "The multipart body must carry the text field and the file bytes.");
    }

    private static async Task DeniesUploadWithoutScopeAsync()
    {
        await using var server = new FakeHttpServer(_ => new HttpResponse(200, [], "ok"));
        var dispatcher = NewDispatcher(new HttpService(new HttpServiceOptions()));
        var caps = new CapabilitySet([HttpPlugin.UploadCommand]); // 裸权限，无 URL 作用域
        var request = new InvokeRequest(1, "u-deny", HttpPlugin.UploadCommand,
            JsonSerializer.SerializeToElement(
                new HttpUploadOptions(server.BaseUrl + "/x", Files: [new HttpFilePart("f", "a.txt", [1])]),
                TaruiJsonContext.Default.HttpUploadOptions));
        var response = JsonSerializer.Deserialize(
            await dispatcher.DispatchJsonAsync(
                JsonSerializer.Serialize(request, TaruiJsonContext.Default.InvokeRequest),
                new CommandContext("main", "main", caps)),
            TaruiJsonContext.Default.InvokeResponse);
        Assert(!response!.Success, "A bare upload permission with no URL scope must be denied.");
        Assert(response.Error?.Code == "SCOPE_DENIED", "A scopeless upload must surface as SCOPE_DENIED.");
    }

    private static string ReassembleStream(IReadOnlyList<JsonElement> frames, out int status)
    {
        status = 0;
        using var buffer = new MemoryStream();
        foreach (var frame in frames)
        {
            var item = frame.Deserialize(TaruiJsonContext.Default.HttpStreamEvent)!;
            if (item.Kind == "meta")
            {
                status = item.Meta?.Status ?? 0;
            }
            else if (item.Kind == "chunk" && item.Data is not null)
            {
                buffer.Write(item.Data);
            }
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static async Task<(InvokeResponse? Response, RecordingChannelSink Sink)> DispatchFetchStream(
        IpcDispatcher dispatcher, FakeHttpServer server, string path, string channelId,
        RecordingChannelSink sink, string[] allow)
    {
        var caps = HttpCapability(allow);
        var request = new InvokeRequest(1, "http-stream-" + DateTime.UtcNow.Ticks, HttpPlugin.FetchCommand,
            JsonSerializer.SerializeToElement(
                new HttpRequestOptions("GET", server.BaseUrl + path, Channel: channelId),
                TaruiJsonContext.Default.HttpRequestOptions));
        var json = JsonSerializer.Serialize(request, TaruiJsonContext.Default.InvokeRequest);
        var responseText = await dispatcher.DispatchJsonAsync(
            json, new CommandContext("main", "main", caps), channelSink: sink);
        var response = JsonSerializer.Deserialize(responseText, TaruiJsonContext.Default.InvokeResponse);
        return (response, sink);
    }

    private sealed class RecordingChannelSink : IChannelSink
    {
        public List<JsonElement> Frames { get; } = [];

        public ValueTask SendAsync(string channelId, JsonElement payload, CancellationToken cancellationToken = default)
        {
            Frames.Add(payload);
            return ValueTask.CompletedTask;
        }
    }

    // ---------- helpers ----------

    private static IpcDispatcher NewDispatcher(IHttpService service)
    {
        var builder = new CommandRouterBuilder();
        new HttpPlugin(service).ConfigureCommands(builder);
        return new IpcDispatcher(builder.Build());
    }

    private static async Task<InvokeResponse?> DispatchFetch(
        IpcDispatcher dispatcher, FakeHttpServer server, string path, string[] allow, string[]? deny = null)
        => await DispatchRaw(dispatcher, HttpCapability(allow, deny), "GET", server.BaseUrl + path);

    private static async Task<InvokeResponse?> DispatchRaw(IpcDispatcher dispatcher, CapabilitySet caps, string method, string url)
    {
        var request = new InvokeRequest(1, "http-" + DateTime.UtcNow.Ticks, HttpPlugin.FetchCommand,
            JsonSerializer.SerializeToElement(new HttpRequestOptions(method, url), TaruiJsonContext.Default.HttpRequestOptions));
        var json = JsonSerializer.Serialize(request, TaruiJsonContext.Default.InvokeRequest);
        var responseText = await dispatcher.DispatchJsonAsync(json, new CommandContext("main", "main", caps));
        return JsonSerializer.Deserialize(responseText, TaruiJsonContext.Default.InvokeResponse);
    }

    private static CapabilitySet HttpCapability(string[] allow, string[]? deny = null)
    {
        var scope = new PermissionScope(
            [.. allow.Select(pattern => new PathScope(Path: pattern))],
            [.. (deny ?? []).Select(pattern => new PathScope(Path: pattern))]);
        return new CapabilitySet(
            [HttpPlugin.FetchCommand],
            events: [],
            scopedPermissions: [new KeyValuePair<string, PermissionScope>(HttpPlugin.FetchCommand, scope)]);
    }

    private static async Task<InvokeResponse?> DispatchUpload(
        IpcDispatcher dispatcher, FakeHttpServer server, HttpUploadOptions options, string[] allow)
    {
        var caps = HttpUploadCapability(allow);
        var request = new InvokeRequest(1, "up-" + DateTime.UtcNow.Ticks, HttpPlugin.UploadCommand,
            JsonSerializer.SerializeToElement(options, TaruiJsonContext.Default.HttpUploadOptions));
        var json = JsonSerializer.Serialize(request, TaruiJsonContext.Default.InvokeRequest);
        var responseText = await dispatcher.DispatchJsonAsync(json, new CommandContext("main", "main", caps));
        return JsonSerializer.Deserialize(responseText, TaruiJsonContext.Default.InvokeResponse);
    }

    private static CapabilitySet HttpUploadCapability(string[] allow)
    {
        var scope = new PermissionScope([.. allow.Select(pattern => new PathScope(Path: pattern))], []);
        return new CapabilitySet(
            [HttpPlugin.UploadCommand],
            events: [],
            scopedPermissions: [new KeyValuePair<string, PermissionScope>(HttpPlugin.UploadCommand, scope)]);
    }

    private static bool Allows(string urlPattern, string url) =>
        UrlScopeMatcher.AllowsUrl([new PathScope(Path: urlPattern)], [], url);

    private static bool Allows(string? allow, string? deny, string url) =>
        UrlScopeMatcher.AllowsUrl(
            allow is null ? [] : [new PathScope(Path: allow)],
            deny is null ? [] : [new PathScope(Path: deny)],
            url);

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed record HttpRequest(string Method, string Path, (string Name, string Value)[] Headers, string Body);

    private sealed record HttpResponse(int Status, HttpHeader[] Headers, string Body);

    /// <summary>A tiny loopback HTTP server that serves canned responses without http.sys ACLs.</summary>
    private sealed class FakeHttpServer : IAsyncDisposable
    {
        private readonly Func<HttpRequest, HttpResponse> _responder;
        private readonly TcpListener _listener = new(System.Net.IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _cts = new();
        private Task? _loop;

        public int Port { get; }
        public string BaseUrl { get; }

        public FakeHttpServer(Func<HttpRequest, HttpResponse>? responder = null)
        {
            _responder = responder ?? (_ => new HttpResponse(200, [], "ok"));
            _listener.Start();
            Port = ((System.Net.IPEndPoint)_listener.LocalEndpoint).Port;
            BaseUrl = $"http://127.0.0.1:{Port}";
            _loop = Task.Run(AcceptLoopAsync);
        }

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

                _ = Task.Run(() => ServeAsync(client));
            }
        }

        private async Task ServeAsync(TcpClient client)
        {
            using (client)
            using (var stream = client.GetStream())
            {
                var line = await ReadLineAsync(stream);
                if (line is null)
                {
                    return;
                }

                var parts = line.Split(' ');
                var method = parts.Length > 0 ? parts[0] : "GET";
                var path = parts.Length > 1 ? parts[1] : "/";

                var headers = new List<(string, string)>();
                string? headerLine;
                while (!string.IsNullOrEmpty(headerLine = await ReadLineAsync(stream)))
                {
                    var idx = headerLine.IndexOf(':');
                    if (idx > 0)
                    {
                        headers.Add((headerLine[..idx].Trim(), headerLine[(idx + 1)..].Trim()));
                    }
                }

                var body = string.Empty;
                var lengthHeader = headers.FirstOrDefault(h => h.Item1.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)).Item2;
                if (int.TryParse(lengthHeader, out var contentLength) && contentLength > 0)
                {
                    body = await ReadBodyAsync(stream, contentLength);
                }

                var response = _responder(new HttpRequest(method, path, headers.ToArray(), body));
                await WriteResponseAsync(stream, response);
            }
        }

        private static async Task WriteResponseAsync(Stream stream, HttpResponse response)
        {
            var body = Encoding.UTF8.GetBytes(response.Body);
            var invariant = System.Globalization.CultureInfo.InvariantCulture;
            var head = new string[response.Headers.Length * 2 + 5];
            var index = 0;
            head[index++] = "HTTP/1.1 ";
            head[index++] = response.Status.ToString(invariant);
            head[index++] = " " + Reason(response.Status) + "\r\n";
            foreach (var header in response.Headers)
            {
                head[index++] = header.Name + ": " + header.Value + "\r\n";
            }

            head[index++] = "Content-Length: " + body.Length.ToString(invariant) + "\r\n";
            head[index++] = "Connection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(string.Concat(head)));
            if (body.Length > 0)
            {
                await stream.WriteAsync(body);
            }

            await stream.FlushAsync();
        }

        private static string Reason(int status) => status switch
        {
            200 => "OK",
            302 => "Found",
            404 => "Not Found",
            500 => "Internal Server Error",
            _ => "Status",
        };

        private static async Task<string?> ReadLineAsync(Stream stream)
        {
            var buffer = new List<byte>(128);
            var single = new byte[1];
            for (var i = 0; i < 8192; i++)
            {
                var read = await stream.ReadAsync(single.AsMemory(0, 1));
                if (read == 0)
                {
                    break;
                }

                if (single[0] == (byte)'\n')
                {
                    return Encoding.ASCII.GetString(buffer.ToArray()).TrimEnd('\r');
                }

                buffer.Add(single[0]);
            }

            return buffer.Count == 0 ? null : Encoding.ASCII.GetString(buffer.ToArray());
        }

        private static async Task<string> ReadBodyAsync(Stream stream, int length)
        {
            var buffer = new byte[length];
            var offset = 0;
            while (offset < length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(offset, length - offset));
                if (read == 0)
                {
                    break;
                }

                offset += read;
            }

            return Encoding.UTF8.GetString(buffer, 0, offset);
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            _listener.Stop();
            if (_loop is not null)
            {
                try
                {
                    await _loop;
                }
                catch (Exception)
                {
                    // 关闭路径上的异常忽略。
                }
            }

            _cts.Dispose();
        }
    }
}