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
        var response = options.ResourceProvider.Resolve(
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

        return new DefaultResourceHandler
        {
            Status = response.Status,
            StatusText = response.StatusText,
            MimeType = response.MimeType,
            Response = new MemoryStream(response.Content, writable: false),
            ResponseLength = response.ResponseLength,
            Headers = headers
        };
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
