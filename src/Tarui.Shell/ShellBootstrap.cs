using Avalonia.Controls;
using Tarui.Ipc;
using Tarui.Plugins.Core;
using Tarui.Plugins.Dialog;
using Tarui.WebView.Abstractions;

namespace Tarui.Shell;

public static class ShellBootstrap
{
    public static Window CreateWindow(ITaruiWebViewFactory webViewFactory)
    {
        var commandBuilder = new CommandRouterBuilder();
        var registeredPermissions = new HashSet<string>(StringComparer.Ordinal);

        CorePlugin.Register(commandBuilder, permission => registeredPermissions.Add(permission));
        DialogPlugin.Register(commandBuilder, permission => registeredPermissions.Add(permission), new SampleDialogService());

        var mainPermissions = new[]
        {
            "core:app|get-info",
            "core:window|minimize",
            "plugin:dialog|open"
        };
        var missingPermissions = mainPermissions
            .Where(permission => !registeredPermissions.Contains(permission))
            .ToArray();
        if (missingPermissions.Length > 0)
        {
            throw new InvalidOperationException(
                $"Main capability references unregistered permissions: {string.Join(", ", missingPermissions)}");
        }

        var router = commandBuilder.Build();
        var capabilities = new CapabilitySet(mainPermissions);
        var context = new CommandContext("main", "main", capabilities);
        var dispatcher = new IpcDispatcher(router);
        var webViewHost = new WebViewHost(
            webViewFactory,
            dispatcher,
            context,
            new Uri(Environment.GetEnvironmentVariable("TARUI_WEB_URL") ?? "http://127.0.0.1:5173"));

        return new MainWindow(webViewHost);
    }
}

internal sealed class SampleDialogService : IDialogService
{
    public ValueTask<Tarui.Contracts.OpenDialogResult> OpenAsync(
        Tarui.Contracts.OpenDialogOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new Tarui.Contracts.OpenDialogResult([]));
    }
}
