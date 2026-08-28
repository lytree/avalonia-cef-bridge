using System.Collections.Specialized;
using Xilium.CefGlue;
using Xilium.CefGlue.Common.Handlers;
using Xilium.CefGlue.Common.Shared;

namespace CefGlue.Next.Avalonia;

internal sealed class CefGlueNextAvaloniaSchemeHandlerFactory(
    CefGlueNextAvaloniaSchemeOptions options) : CefSchemeHandlerFactory
{
    protected override CefResourceHandler Create(
        CefBrowser browser,
        CefFrame frame,
        string schemeName,
        CefRequest request)
    {
        var response = CefGlueNextAvaloniaProviderBoundary.SafeResolve(
            options.ResourceProvider,
            new CefGlueNextAvaloniaResourceRequest(
                request.Url,
                request.Method,
                frame?.IsMain == true,
                frame?.IsMain == true && request.ResourceType == CefResourceType.MainFrame));

        var headers = new NameValueCollection();
        if (response.Headers is not null)
        {
            foreach (var header in response.Headers)
            {
                headers[header.Key] = header.Value;
            }
        }

        if (!headers.AllKeys.Contains("Cache-Control", StringComparer.OrdinalIgnoreCase))
        {
            headers["Cache-Control"] = response.CacheControl;
        }

        // Prefer an explicit stream from the provider so large assets (videos, models, map tiles)
        // are forwarded to CEF as they are produced rather than fully buffered into managed memory.
        // When the provider only supplies a byte[] (small responses, error pages) we still wrap it
        // in a non-writable MemoryStream because DefaultResourceHandler requires a seekable stream.
        var stream = response.ContentStream
            ?? (response.Content is null ? null : new MemoryStream(response.Content, writable: false));
        return new DefaultResourceHandler
        {
            Status = response.Status,
            StatusText = response.StatusText,
            MimeType = response.MimeType,
            Response = stream ?? Stream.Null,
            ResponseLength = response.ResponseLength,
            Headers = headers
        };
    }
}

/// <summary>
/// Invokes a user-supplied <see cref="ICefGlueNextAvaloniaResourceProvider"/> under a hard
/// exception boundary. A provider exception must never propagate across the native callback
/// (CEF would tear down the request pipeline and, in some configurations, the process); we
/// convert it into a deterministic 500 response with an explanatory body so the front-end can
/// surface the failure and the host process stays alive.
/// </summary>
internal static class CefGlueNextAvaloniaProviderBoundary
{
    public static CefGlueNextAvaloniaResourceResponse SafeResolve(
        ICefGlueNextAvaloniaResourceProvider provider,
        CefGlueNextAvaloniaResourceRequest request)
    {
        ArgumentNullException.ThrowIfNull(provider);
        try
        {
            return provider.Resolve(request);
        }
        catch (Exception exception)
        {
            var body = System.Text.Encoding.UTF8.GetBytes(
                $"Resource provider threw {exception.GetType().Name}: {exception.Message}");
            return new CefGlueNextAvaloniaResourceResponse(
                Status: 500,
                StatusText: "Internal Resource Provider Error",
                MimeType: "text/plain; charset=utf-8",
                CacheControl: "no-store",
                ResponseLength: body.LongLength,
                Content: body);
        }
    }
}

internal static class CefGlueNextAvaloniaSchemeMapper
{
    public static CustomScheme[] Create(
        IReadOnlyList<CefGlueNextAvaloniaSchemeOptions> options,
        out IReadOnlyList<CefGlueNextAvaloniaSchemeHandlerFactory> factories)
    {
        var createdFactories = options
            .Select(static option => new CefGlueNextAvaloniaSchemeHandlerFactory(option))
            .ToArray();
        factories = createdFactories;

        return options
            .Select(
                (option, index) => new CustomScheme
                {
                    SchemeName = option.SchemeName,
                    DomainName = option.DomainName,
                    IsStandard = option.IsStandard,
                    IsLocal = option.IsLocal,
                    IsDisplayIsolated = option.IsDisplayIsolated,
                    IsSecure = option.IsSecure,
                    IsCorsEnabled = option.IsCorsEnabled,
                    IsCSPBypassing = option.IsCspBypassing,
                    IsFetchEnabled = option.IsFetchEnabled,
                    SchemeHandlerFactory = createdFactories[index]
                })
            .ToArray();
    }
}
