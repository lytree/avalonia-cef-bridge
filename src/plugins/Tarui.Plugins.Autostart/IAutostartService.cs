using Tarui.Contracts;

namespace Tarui.Plugins.Autostart;

/// <summary>
/// Registers the current application for launch at user login. Only the running application's own
/// executable is ever registered -- Web code cannot supply an arbitrary executable. The
/// <paramref name="options"/> for <c>enable</c> carry only fixed, pre-configured arguments.
/// </summary>
public interface IAutostartService
{
    ValueTask<AutostartState> IsEnabledAsync(CancellationToken cancellationToken);

    ValueTask<Unit> EnableAsync(AutostartEnableOptions options, CancellationToken cancellationToken);

    ValueTask<Unit> DisableAsync(CancellationToken cancellationToken);
}