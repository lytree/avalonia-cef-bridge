namespace Tarui.Shell;

/// <inheritdoc />
public abstract partial class MacDeepLinkBridge
{
    /// <summary>
    /// Inert bridge used on every non-macOS platform. The hosted service lifecycle is preserved
    /// so the composition root can register the bridge unconditionally without branching.
    /// </summary>
    private sealed class NoOp : MacDeepLinkBridge
    {
        public override Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}