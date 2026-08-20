using Tarui.Contracts;
using Tarui.Ipc;

namespace Tarui.Plugins.Dialog;

public interface IDialogService
{
    ValueTask<OpenDialogResult> OpenAsync(
        OpenDialogOptions options,
        CancellationToken cancellationToken);
}

public static class DialogPlugin
{
    public static void Register(
        CommandRouterBuilder commands,
        Action<string> registerPermission,
        IDialogService service)
    {
        var handlers = new DialogCommands(service);
        commands.Add(
            "plugin:dialog|open",
            TaruiJsonContext.Default.OpenDialogOptions,
            TaruiJsonContext.Default.OpenDialogResult,
            handlers.OpenAsync,
            "plugin:dialog|open");
        registerPermission("plugin:dialog|open");
    }

    private sealed class DialogCommands(IDialogService service)
    {
        [TaruiCommand("plugin:dialog|open")]
        public ValueTask<OpenDialogResult> OpenAsync(
            OpenDialogOptions options,
            CommandContext context,
            CancellationToken cancellationToken)
        {
            return service.OpenAsync(options, cancellationToken);
        }
    }
}
