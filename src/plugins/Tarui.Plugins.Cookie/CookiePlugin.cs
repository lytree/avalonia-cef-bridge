using System.Threading;
using Tarui.Contracts;
using Tarui.Ipc;

namespace Tarui.Plugins.Cookie;

/// <summary>
/// Registers the <c>plugin:cookie|list|set|remove|flush</c> commands. The underlying browser cookie store is
/// injected through <see cref="ICookieService"/>; each command is gated by capability permission IDs, so a window
/// only reaches the cookie store when its capability profile grants the matching <c>plugin:cookie|*</c> permission.
/// </summary>
public sealed class CookiePlugin(ICookieService service) : ITaruiPlugin
{
    public const string ListCommand = "plugin:cookie|list";
    public const string SetCommand = "plugin:cookie|set";
    public const string RemoveCommand = "plugin:cookie|remove";
    public const string FlushCommand = "plugin:cookie|flush";

    public void ConfigureCommands(CommandRouterBuilder commands)
    {
        commands.Add(
            ListCommand,
            TaruiJsonContext.Default.CookieListOptions,
            TaruiJsonContext.Default.CookieListResult,
            (options, _, ct) => service.ListAsync(options, ct),
            ListCommand);

        commands.Add(
            SetCommand,
            TaruiJsonContext.Default.CookieSetOptions,
            TaruiJsonContext.Default.CookieSetResult,
            (options, _, ct) => service.SetAsync(options, ct),
            SetCommand);

        commands.Add(
            RemoveCommand,
            TaruiJsonContext.Default.CookieDeleteOptions,
            TaruiJsonContext.Default.CookieDeleteResult,
            (options, _, ct) => service.RemoveAsync(options, ct),
            RemoveCommand);

        commands.Add(
            FlushCommand,
            TaruiJsonContext.Default.Unit,
            TaruiJsonContext.Default.Unit,
            async (_, _, ct) =>
            {
                await service.FlushAsync(ct);
                return new Unit();
            },
            FlushCommand);
    }
}