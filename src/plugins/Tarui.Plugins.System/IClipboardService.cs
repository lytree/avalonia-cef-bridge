using Tarui.Contracts;

namespace Tarui.Plugins.System;

public interface IClipboardService
{
    ValueTask<string> ReadTextAsync(CancellationToken cancellationToken);

    ValueTask<Unit> WriteTextAsync(string text, CancellationToken cancellationToken);

    ValueTask<ClipboardReadHtmlResult> ReadHtmlAsync(CancellationToken cancellationToken);

    ValueTask<Unit> WriteHtmlAsync(string html, string? plainText, CancellationToken cancellationToken);

    ValueTask<ClipboardReadImageResult> ReadImageAsync(CancellationToken cancellationToken);

    ValueTask<Unit> WriteImageAsync(byte[] png, CancellationToken cancellationToken);
}