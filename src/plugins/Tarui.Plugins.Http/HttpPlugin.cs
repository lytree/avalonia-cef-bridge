using Microsoft.Extensions.DependencyInjection;
using Tarui.Contracts;
using Tarui.Ipc;
using HttpRequestOptions = Tarui.Contracts.HttpRequestOptions;

namespace Tarui.Plugins.Http;

/// <summary>Configuration for the HTTP client plugin.</summary>
public sealed record HttpServiceOptions(
    int MaxRedirectCount = 10,
    long MaxInlineBytes = 8 * 1024 * 1024,
    int DefaultTimeoutMs = 100_000,
    int StreamChunkBytes = 256 * 1024);

/// <summary>Executes scoped HTTP fetches (inline or streamed) on behalf of the web layer.</summary>
public interface IHttpService
{
    /// <summary>
    /// Sends the request and returns a non-streaming result. <paramref name="allow"/>/<paramref name="deny"/>
    /// are the caller capability's URL scopes; both the initial URL and every redirect hop are authorized
    /// against them (deny wins, empty allow is a deny-by-default).
    /// </summary>
    ValueTask<HttpResponseResult> FetchAsync(
        HttpRequestOptions options,
        IReadOnlyList<PathScope> allow,
        IReadOnlyList<PathScope> deny,
        CancellationToken cancellationToken);

    /// <summary>
    /// Streams the response body to the front-end channel named by <see cref="HttpRequestOptions.Channel"/>
    /// as a leading <c>meta</c> frame followed by <c>chunk</c> frames, then resolves with the status and headers.
    /// </summary>
    ValueTask<HttpResponseResult> FetchStreamAsync(
        HttpRequestOptions options,
        IReadOnlyList<PathScope> allow,
        IReadOnlyList<PathScope> deny,
        CancellationToken cancellationToken);

    /// <summary>
    /// Sends a scoped multipart/form-data <c>POST</c>. The URL and every redirect hop are authorized against the
    /// URL scopes (default deny); the response body is capped at the inline limit like <see cref="FetchAsync"/>.
    /// </summary>
    ValueTask<HttpUploadResult> UploadAsync(
        HttpUploadOptions options,
        IReadOnlyList<PathScope> allow,
        IReadOnlyList<PathScope> deny,
        CancellationToken cancellationToken);
}

/// <summary>
/// Default HTTP service. Uses a single pooled <see cref="HttpClient"/> with auto-redirect disabled so every
/// hop can be re-checked against the capability URL scopes, and enforces an inline body size ceiling. A body
/// exceeding <see cref="HttpServiceOptions.MaxInlineBytes"/> must be streamed (channel) instead.
/// </summary>
public sealed class HttpService(HttpServiceOptions serviceOptions) : IHttpService
{
    private static readonly HttpClient Client = CreateClient();

    public async ValueTask<HttpResponseResult> FetchAsync(
        HttpRequestOptions options,
        IReadOnlyList<PathScope> allow,
        IReadOnlyList<PathScope> deny,
        CancellationToken cancellationToken)
    {
        using var response = await FollowRedirectsAsync(options, allow, deny, cancellationToken);
        var headers = CollectHeaders(response);
        var text = await ReadInlineBodyAsync(response, serviceOptions.MaxInlineBytes, cancellationToken);
        return new HttpResponseResult((int)response.StatusCode, headers, text);
    }

    public async ValueTask<HttpResponseResult> FetchStreamAsync(
        HttpRequestOptions options,
        IReadOnlyList<PathScope> allow,
        IReadOnlyList<PathScope> deny,
        CancellationToken cancellationToken)
    {
        var channel = ChannelContext.Bind<HttpStreamEvent>(options.Channel);
        using var response = await FollowRedirectsAsync(options, allow, deny, cancellationToken);
        var headers = CollectHeaders(response);
        await channel.SendAsync(new HttpStreamEvent(
            "meta", new HttpStreamMeta((int)response.StatusCode, response.ReasonPhrase, headers)), cancellationToken);

        // ResponseHeadersRead 已提前返回；body 以块为单位推送到前端，背压由 WebviewSession 的
        // ExecuteScriptAsync await 天然提供，不会在宿主侧无限堆积内存。
        using var content = response.Content;
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[serviceOptions.StreamChunkBytes];
        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
        {
            await channel.SendAsync(new HttpStreamEvent("chunk", Data: buffer.AsMemory(0, read).ToArray()),
                cancellationToken);
        }

        return new HttpResponseResult((int)response.StatusCode, headers, null);
    }

    public async ValueTask<HttpUploadResult> UploadAsync(
        HttpUploadOptions options,
        IReadOnlyList<PathScope> allow,
        IReadOnlyList<PathScope> deny,
        CancellationToken cancellationToken)
    {
        using var response = await SendUploadAsync(options, allow, deny, cancellationToken);
        var headers = CollectHeaders(response);
        var text = await ReadInlineBodyAsync(response, serviceOptions.MaxInlineBytes, cancellationToken);
        return new HttpUploadResult((int)response.StatusCode, headers, text);
    }

    /// <summary>
    /// Sends a multipart POST, manually following redirects while re-checking each hop against the URL scopes.
    /// The multipart body is rebuilt per hop (redirects preserve the POST method and body). Returns the final,
    /// caller-disposed response. Scope is checked here defensively too, though the handler requires a scope.
    /// </summary>
    private async Task<HttpResponseMessage> SendUploadAsync(
        HttpUploadOptions options,
        IReadOnlyList<PathScope> allow,
        IReadOnlyList<PathScope> deny,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(options.Url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("Only http:// and https:// URLs are supported.");
        }

        if (!UrlScopeMatcher.AllowsUrl(allow, deny, options.Url))
        {
            throw new ScopeDeniedException(HttpPlugin.UploadCommand);
        }

        using var cts = new CancellationTokenSource();
        var timeout = options.TimeoutMs ?? serviceOptions.DefaultTimeoutMs;
        if (timeout > 0)
        {
            cts.CancelAfter(timeout);
        }

        using var link = cancellationToken.Register(cts.Cancel);
        var token = cts.Token;
        var currentUri = uri;

        for (var hop = 0; ; hop++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, currentUri)
            {
                Content = BuildMultipart(options),
            };
            ApplyHeaders(request, options.Headers);

            HttpResponseMessage response;
            try
            {
                response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("The HTTP upload exceeded its timeout.");
            }

            if (!IsRedirect(response, currentUri, out var next))
            {
                return response;
            }

            using (response)
            {
                if (hop >= serviceOptions.MaxRedirectCount)
                {
                    throw new InvalidOperationException("The HTTP upload exceeded the redirect limit.");
                }

                if (!UrlScopeMatcher.AllowsUrl(allow, deny, next.AbsoluteUri))
                {
                    throw new ScopeDeniedException(HttpPlugin.UploadCommand);
                }

                currentUri = next;
            }
        }
    }

    private static MultipartFormDataContent BuildMultipart(HttpUploadOptions options)
    {
        var content = new MultipartFormDataContent();
        foreach (var field in options.Fields ?? [])
        {
            content.Add(new StringContent(field.Value ?? string.Empty), field.Name);
        }

        foreach (var file in options.Files ?? [])
        {
            var part = new ByteArrayContent(file.Data);
            if (!string.IsNullOrWhiteSpace(file.ContentType) &&
                System.Net.Http.Headers.MediaTypeHeaderValue.TryParse(file.ContentType, out var mediaType))
            {
                part.Headers.ContentType = mediaType;
            }

            content.Add(part, file.Name, file.FileName);
        }

        return content;
    }

    private static void ApplyHeaders(HttpRequestMessage request, HttpHeader[]? headers)
    {
        foreach (var header in headers ?? [])
        {
            if (!request.Headers.TryAddWithoutValidation(header.Name, header.Value) && request.Content is not null)
            {
                request.Content.Headers.TryAddWithoutValidation(header.Name, header.Value);
            }
        }
    }

    /// <summary>
    /// Sends the request and manually follows redirects, re-checking each hop against the URL scopes.
    /// Returns the final, caller-disposed response. The scheme whitelist is enforced defensively here too,
    /// though scope matching already restricts candidates to http/https.
    /// </summary>
    private async Task<HttpResponseMessage> FollowRedirectsAsync(
        HttpRequestOptions options,
        IReadOnlyList<PathScope> allow,
        IReadOnlyList<PathScope> deny,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(options.Url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("Only http:// and https:// URLs are supported.");
        }

        if (!UrlScopeMatcher.AllowsUrl(allow, deny, options.Url))
        {
            throw new ScopeDeniedException(HttpPlugin.FetchCommand);
        }

        using var cts = new CancellationTokenSource();
        var timeout = options.TimeoutMs ?? serviceOptions.DefaultTimeoutMs;
        if (timeout > 0)
        {
            cts.CancelAfter(timeout);
        }

        using var link = cancellationToken.Register(cts.Cancel);
        var token = cts.Token;

        var method = options.Method ?? "GET";
        var body = options.Body;
        var currentUri = uri;

        for (var hop = 0; ; hop++)
        {
            using var request = BuildRequest(method, currentUri, options.Headers, body);
            HttpResponseMessage response;
            try
            {
                response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("The HTTP request exceeded its timeout.");
            }

            if (!IsRedirect(response, currentUri, out var next))
            {
                return response; // 所有权转移给调用方，由调用方 using 释放。
            }

            using (response)
            {
                if (hop >= serviceOptions.MaxRedirectCount)
                {
                    throw new InvalidOperationException("The HTTP request exceeded the redirect limit.");
                }

                if (!UrlScopeMatcher.AllowsUrl(allow, deny, next.AbsoluteUri))
                {
                    throw new ScopeDeniedException(HttpPlugin.FetchCommand);
                }

                (method, body) = RedirectMethodAndBody(method, body, response.StatusCode);
                currentUri = next;
            }
        }
    }

    private static async Task<string?> ReadInlineBodyAsync(HttpResponseMessage response, long maxBytes, CancellationToken token)
    {
        var bytes = await response.Content.ReadAsByteArrayAsync(token);
        if (bytes.Length > maxBytes)
        {
            throw new InvalidOperationException(
                $"The response body exceeds the {maxBytes} inline limit; use a channel to stream it.");
        }

        return bytes.Length == 0 ? null : System.Text.Encoding.UTF8.GetString(bytes);
    }

    private static HttpHeader[] CollectHeaders(HttpResponseMessage response)
    {
        return [.. response.Headers
            .Concat(response.Content.Headers)
            .SelectMany(header => header.Value.Select(value => new HttpHeader(header.Key, value)))];
    }

    private static HttpRequestMessage BuildRequest(string method, Uri uri, HttpHeader[]? headers, string? body)
    {
        var hasBody = body is not null && method is not "GET" and not "HEAD";
        var content = hasBody ? new StringContent(body!, System.Text.Encoding.UTF8) : null;
        var request = new HttpRequestMessage(new HttpMethod(method), uri) { Content = content };

        foreach (var header in headers ?? [])
        {
            if (!request.Headers.TryAddWithoutValidation(header.Name, header.Value) && content is not null)
            {
                content.Headers.TryAddWithoutValidation(header.Name, header.Value);
            }
        }

        return request;
    }

    private static bool IsRedirect(HttpResponseMessage response, Uri currentUri, out Uri next)
    {
        next = null!;
        var status = (int)response.StatusCode;
        // 只跟随带有明确 Location 的 301/302/303/307/308；其余 3xx（304 等）按终态返回。
        if (status is not (301 or 302 or 303 or 307 or 308) || response.Headers.Location is null)
        {
            return false;
        }

        var target = response.Headers.Location.IsAbsoluteUri
            ? response.Headers.Location.AbsoluteUri
            : new Uri(currentUri, response.Headers.Location).AbsoluteUri;
        if (!Uri.TryCreate(target, UriKind.Absolute, out var parsed))
        {
            return false;
        }

        next = parsed;
        return true;
    }

    private static (string Method, string? Body) RedirectMethodAndBody(string method, string? body, System.Net.HttpStatusCode status)
    {
        // 301/302/303 → GET（丢弃 body，遵循常见客户端语义）；307/308 保留方法与 body。
        var code = (int)status;
        if (code is 301 or 302 or 303)
        {
            return ("GET", null);
        }

        return (method, body);
    }

    private static HttpClient CreateClient()
    {
        // 代理关闭避免测试内网与系统代理干扰；自动重定向由 FetchAsync 手动跟随以保证每跳 scope 校验。
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };
        return new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }
}

/// <summary>Registers <c>plugin:http|fetch</c> behind URL-scope authorization.</summary>
public sealed class HttpPlugin(IHttpService service) : ITaruiPlugin
{
    public const string FetchCommand = "plugin:http|fetch";
    public const string UploadCommand = "plugin:http|upload";

    public void ConfigureCommands(CommandRouterBuilder commands)
    {
        commands.Add(
            FetchCommand,
            TaruiJsonContext.Default.HttpRequestOptions,
            TaruiJsonContext.Default.HttpResponseResult,
            (options, context, ct) =>
            {
                // HTTP 默认拒绝：未带 URL 作用域的裸权限不得静默放开全部 URL。
                if (!context.Capabilities.TryGetScope(FetchCommand, out var scope))
                {
                    throw new ScopeDeniedException(FetchCommand);
                }

                return string.IsNullOrEmpty(options.Channel)
                    ? service.FetchAsync(options, scope.Allow, scope.Deny, ct)
                    : service.FetchStreamAsync(options, scope.Allow, scope.Deny, ct);
            },
            FetchCommand,
            HttpScopeAuthorizer.AllowsUrl);

        commands.Add(
            UploadCommand,
            TaruiJsonContext.Default.HttpUploadOptions,
            TaruiJsonContext.Default.HttpUploadResult,
            (options, context, ct) =>
            {
                // HTTP 默认拒绝：未带 URL 作用域的裸权限不得静默放开全部 URL。
                if (!context.Capabilities.TryGetScope(UploadCommand, out var scope))
                {
                    throw new ScopeDeniedException(UploadCommand);
                }

                return service.UploadAsync(options, scope.Allow, scope.Deny, ct);
            },
            UploadCommand);
    }
}

public static class HttpPluginServiceCollectionExtensions
{
    public static IServiceCollection AddHttpPlugin(this IServiceCollection services) => services
        .AddSingleton<HttpServiceOptions>(new HttpServiceOptions())
        .AddSingleton<IHttpService, HttpService>()
        .AddPlugin<HttpPlugin>();
}