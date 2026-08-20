using Tarui.Contracts;
using Tarui.Ipc;

namespace Tarui.Plugins.Dialog;

public interface IDialogService
{
    ValueTask<OpenDialogResult> OpenAsync(
        OpenDialogOptions options,
        string windowLabel,
        CancellationToken cancellationToken);

    ValueTask<SaveDialogResult> SaveAsync(
        SaveDialogOptions options,
        string windowLabel,
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

        commands.Add(
            "plugin:dialog|save",
            TaruiJsonContext.Default.SaveDialogOptions,
            TaruiJsonContext.Default.SaveDialogResult,
            handlers.SaveAsync,
            "plugin:dialog|save");
        registerPermission("plugin:dialog|save");
    }

    private sealed class DialogCommands(IDialogService service)
    {
        [TaruiCommand("plugin:dialog|open")]
        public ValueTask<OpenDialogResult> OpenAsync(
            OpenDialogOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.OpenAsync(options, context.WindowLabel, cancellationToken);

        [TaruiCommand("plugin:dialog|save")]
        public ValueTask<SaveDialogResult> SaveAsync(
            SaveDialogOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.SaveAsync(options, context.WindowLabel, cancellationToken);
    }
}
