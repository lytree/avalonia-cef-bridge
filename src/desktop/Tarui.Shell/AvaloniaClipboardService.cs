using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Tarui.Contracts;
using Tarui.Ipc;
using Tarui.Plugins.System;

namespace Tarui.Shell;

public sealed class AvaloniaClipboardService(WindowRegistry registry) : IClipboardService
{
    /// <summary>The standard rich-text clipboard format (<c>HTML Format</c> / CF_HTML on Windows).</summary>
    private static readonly DataFormat<string> HtmlFormat =
        DataFormat.CreateStringPlatformFormat("HTML Format");

    public async ValueTask<string> ReadTextAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await OnClipboard(async clipboard => await clipboard.TryGetTextAsync() ?? string.Empty, cancellationToken);
    }

    public async ValueTask<Unit> WriteTextAsync(string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var clipboard = ResolveClipboard();
            await clipboard.SetTextAsync(text);
        });
        return new Unit();
    }

    public async ValueTask<ClipboardReadHtmlResult> ReadHtmlAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var clipboard = ResolveClipboard();
            var data = await clipboard.TryGetDataAsync();
            if (data is null || !AsyncDataTransferExtensions.Contains(data, HtmlFormat))
            {
                return new ClipboardReadHtmlResult(false);
            }

            var html = await AsyncDataTransferExtensions.TryGetValueAsync(data, HtmlFormat);
            return new ClipboardReadHtmlResult(!string.IsNullOrEmpty(html), html);
        });
    }

    public async ValueTask<Unit> WriteHtmlAsync(string html, string? plainText, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var clipboard = ResolveClipboard();
            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.Create(HtmlFormat, html));
            if (!string.IsNullOrWhiteSpace(plainText))
            {
                transfer.Add(DataTransferItem.CreateText(plainText));
            }

            await clipboard.SetDataAsync(transfer);
        });
        return new Unit();
    }

    public async ValueTask<ClipboardReadImageResult> ReadImageAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Dispatcher.UIThread.InvokeAsync(() => ReadImageCoreAsync(ResolveClipboard()));
    }

    public async ValueTask<Unit> WriteImageAsync(byte[] png, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var clipboard = ResolveClipboard();
            using var stream = new MemoryStream(png);
            var bitmap = new Bitmap(stream);
            await clipboard.SetBitmapAsync(bitmap);
        });
        return new Unit();
    }

    private static async Task<ClipboardReadImageResult> ReadImageCoreAsync(IClipboard clipboard)
    {
        var image = await clipboard.TryGetBitmapAsync();
        if (image is not Bitmap bitmap)
        {
            return new ClipboardReadImageResult(false);
        }

        // Encode the concrete Avalonia bitmap back to PNG bytes for the wire. Bitmaps of an unsupported
        // pixel format degrade to "unavailable" instead of letting a broken frame crash the command.
        try
        {
            using var stream = new MemoryStream();
            bitmap.Save(stream, PngBitmapEncoderOptions.Default);
            return new ClipboardReadImageResult(true, stream.ToArray());
        }
        catch (Exception)
        {
            return new ClipboardReadImageResult(false);
        }
    }

    private async ValueTask<TResult> OnClipboard<TResult>(
        Func<IClipboard, Task<TResult>> action,
        CancellationToken cancellationToken) where TResult : notnull
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var clipboard = ResolveClipboard();
            return await action(clipboard);
        });
    }

    private IClipboard ResolveClipboard()
    {
        var entry = registry.Get("main");
        return entry.Window.Clipboard
            ?? throw new InvalidOperationException("The clipboard is unavailable.");
    }
}