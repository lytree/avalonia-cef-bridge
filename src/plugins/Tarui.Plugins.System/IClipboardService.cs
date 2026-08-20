using Tarui.Contracts;

namespace Tarui.Plugins.System;

public interface IClipboardService
{
    ValueTask<string> ReadTextAsync(CancellationToken cancellationToken);

    ValueTask<Unit> WriteTextAsync(string text, CancellationToken cancellationToken);
}
