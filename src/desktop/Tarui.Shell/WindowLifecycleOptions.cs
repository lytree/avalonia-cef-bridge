namespace Tarui.Shell;

/// <summary>
/// Tunables that govern native window lifecycle behaviour shared by the shell and any plugins that
/// observe it. The values are populated from <c>Tarui:Window:*</c> configuration with a sensible
/// default so a vanilla host behaves exactly as before.
/// </summary>
public sealed class WindowLifecycleOptions
{
    /// <summary>
    /// The default fallback delay applied when the host invokes <c>window://close-requested</c> and
    /// the web layer never confirms the close. A value of <see cref="Timeout.InfiniteTimeSpan"/>
    /// (or any non-positive value) disables the fallback so the window can only be closed by an
    /// explicit <c>core:window|close force=true</c>.
    /// </summary>
    public static readonly TimeSpan DefaultCloseRequestTimeout = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Default fallback delay applied when the host invokes <c>window://close-requested</c> and the
    /// web layer never confirms the close. A value of <see cref="Timeout.InfiniteTimeSpan"/> disables
    /// the fallback so the window can only be closed by an explicit <c>core:window|close force=true</c>.
    /// </summary>
    public TimeSpan CloseRequestTimeout { get; init; } = DefaultCloseRequestTimeout;
}
