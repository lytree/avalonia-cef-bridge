using Avalonia.Input.Platform;
using Avalonia.Threading;
using Tarui.Contracts;
using Tarui.Ipc;
using Tarui.Plugins.System;

namespace Tarui.Shell;

public sealed class AvaloniaClipboardService(WindowRegistry registry) : IClipboardService
{
    public async ValueTask<string> ReadTextAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entry = registry.Get("main");
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var clipboard = entry.Window.Clipboard
                ?? throw new InvalidOperationException("The clipboard is unavailable.");
            return await clipboard.TryGetTextAsync() ?? string.Empty;
        });
    }

    public async ValueTask<Unit> WriteTextAsync(string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entry = registry.Get("main");
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var clipboard = entry.Window.Clipboard
                ?? throw new InvalidOperationException("The clipboard is unavailable.");
            await clipboard.SetTextAsync(text);
        });
        return new Unit();
    }
}
