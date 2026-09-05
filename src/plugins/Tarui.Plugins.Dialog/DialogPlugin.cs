using Microsoft.Extensions.DependencyInjection;
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

    ValueTask<MessageBoxResult> MessageAsync(
        MessageBoxOptions options,
        string windowLabel,
        CancellationToken cancellationToken);

    ValueTask<ConfirmResult> ConfirmAsync(
        ConfirmOptions options,
        string windowLabel,
        CancellationToken cancellationToken);

    ValueTask<AskResult> AskAsync(
        AskOptions options,
        string windowLabel,
        CancellationToken cancellationToken);
}

public sealed class DialogPlugin(IDialogService service) : ITaruiPlugin
{
    public void ConfigureCommands(CommandRouterBuilder commands)
    {
        var handlers = new DialogCommands(service);

        commands.Add(
            "plugin:dialog|open",
            TaruiJsonContext.Default.OpenDialogOptions,
            TaruiJsonContext.Default.OpenDialogResult,
            handlers.OpenAsync,
            "plugin:dialog|open");

        commands.Add(
            "plugin:dialog|save",
            TaruiJsonContext.Default.SaveDialogOptions,
            TaruiJsonContext.Default.SaveDialogResult,
            handlers.SaveAsync,
            "plugin:dialog|save");

        commands.Add(
            "plugin:dialog|message",
            TaruiJsonContext.Default.MessageBoxOptions,
            TaruiJsonContext.Default.MessageBoxResult,
            handlers.MessageAsync,
            "plugin:dialog|message");

        commands.Add(
            "plugin:dialog|confirm",
            TaruiJsonContext.Default.ConfirmOptions,
            TaruiJsonContext.Default.ConfirmResult,
            handlers.ConfirmAsync,
            "plugin:dialog|confirm");

        commands.Add(
            "plugin:dialog|ask",
            TaruiJsonContext.Default.AskOptions,
            TaruiJsonContext.Default.AskResult,
            handlers.AskAsync,
            "plugin:dialog|ask");
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

        [TaruiCommand("plugin:dialog|message")]
        public ValueTask<MessageBoxResult> MessageAsync(
            MessageBoxOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.MessageAsync(options, context.WindowLabel, cancellationToken);

        [TaruiCommand("plugin:dialog|confirm")]
        public ValueTask<ConfirmResult> ConfirmAsync(
            ConfirmOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.ConfirmAsync(options, context.WindowLabel, cancellationToken);

        [TaruiCommand("plugin:dialog|ask")]
        public ValueTask<AskResult> AskAsync(
            AskOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.AskAsync(options, context.WindowLabel, cancellationToken);
    }
}

public static class DialogPluginServiceCollectionExtensions
{
    public static IServiceCollection AddDialogPlugin(this IServiceCollection services)
        => services.AddPlugin<DialogPlugin>();
}
