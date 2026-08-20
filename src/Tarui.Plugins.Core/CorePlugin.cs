using Tarui.Contracts;
using Tarui.Ipc;

namespace Tarui.Plugins.Core;

public static class CorePlugin
{
    public static void Register(
        CommandRouterBuilder commands,
        Action<string> registerPermission)
    {
        commands.Add(
            "core:app|get-info",
            TaruiJsonContext.Default.EmptyArgs,
            TaruiJsonContext.Default.AppHandshake,
            static (_, context, cancellationToken) => GetInfoAsync(context, cancellationToken),
            "core:app|get-info");
        registerPermission("core:app|get-info");

        commands.Add(
            "core:window|minimize",
            TaruiJsonContext.Default.EmptyArgs,
            TaruiJsonContext.Default.Unit,
            static (_, context, cancellationToken) => MinimizeAsync(context, cancellationToken),
            "core:window|minimize");
        registerPermission("core:window|minimize");
    }

    [TaruiCommand("core:app|get-info")]
    private static ValueTask<AppHandshake> GetInfoAsync(
        CommandContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new AppHandshake(
            "tarui.net",
            "0.1.0",
            1,
            Environment.OSVersion.Platform.ToString(),
            [.. context.Capabilities.Permissions]));
    }

    [TaruiCommand("core:window|minimize")]
    private static ValueTask<Unit> MinimizeAsync(
        CommandContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new Unit());
    }
}
