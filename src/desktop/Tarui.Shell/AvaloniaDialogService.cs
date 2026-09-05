using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Tarui.Contracts;
using Tarui.Plugins.Dialog;

namespace Tarui.Shell;

public sealed class AvaloniaDialogService(WindowRegistry registry) : IDialogService
{
    public async ValueTask<OpenDialogResult> OpenAsync(
        OpenDialogOptions options,
        string windowLabel,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entry = registry.Get(windowLabel);
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var storage = entry.Window.StorageProvider;
            if (options.Directory)
            {
                var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    AllowMultiple = options.Multiple,
                });
                return new OpenDialogResult([.. folders.Select(static item => ToPath(item))]);
            }

            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                AllowMultiple = options.Multiple,
                FileTypeFilter = BuildFilters(options.Extensions),
            });
            return new OpenDialogResult([.. files.Select(static item => ToPath(item))]);
        });
    }

    public async ValueTask<SaveDialogResult> SaveAsync(
        SaveDialogOptions options,
        string windowLabel,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entry = registry.Get(windowLabel);
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var storage = entry.Window.StorageProvider;
            var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                SuggestedFileName = options.DefaultName,
                FileTypeChoices = BuildFilters(options.Extensions),
            });
            return new SaveDialogResult(file is null ? null : ToPath(file));
        });
    }

    public async ValueTask<MessageBoxResult> MessageAsync(
        MessageBoxOptions options,
        string windowLabel,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var owner = registry.Get(windowLabel).Window;
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var box = new MessageBoxWindow(options);
            var result = await box.ShowDialog<MessageBoxResult>(owner);
            // Closing via the window chrome returns default; treat it as a cancel.
            return result ?? new MessageBoxResult(MessageBoxResultNames.Cancel);
        });
    }

    public async ValueTask<ConfirmResult> ConfirmAsync(
        ConfirmOptions options,
        string windowLabel,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var owner = registry.Get(windowLabel).Window;
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var box = new MessageBoxWindow(
                new MessageBoxOptions(
                    options.Title,
                    options.Content,
                    options.Icon,
                    MessageBoxButtonNames.OkCancel),
                okLabel: options.OkLabel,
                cancelLabel: options.CancelLabel);
            var result = await box.ShowDialog<MessageBoxResult>(owner);
            return new ConfirmResult(result?.Result == MessageBoxResultNames.Ok);
        });
    }

    public async ValueTask<AskResult> AskAsync(
        AskOptions options,
        string windowLabel,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var owner = registry.Get(windowLabel).Window;
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            // 显式 cancel 按钮时用三键组合；否则 Yes/No + 关闭即取消。
            var button = string.IsNullOrEmpty(options.CancelLabel)
                ? MessageBoxButtonNames.YesNo
                : MessageBoxButtonNames.YesNoCancel;
            var box = new MessageBoxWindow(
                new MessageBoxOptions(options.Title, options.Content, options.Icon, button),
                yesLabel: options.YesLabel,
                noLabel: options.NoLabel,
                cancelLabel: options.CancelLabel);
            var result = await box.ShowDialog<MessageBoxResult>(owner);
            bool? answer = result?.Result switch
            {
                MessageBoxResultNames.Yes => true,
                MessageBoxResultNames.No => false,
                _ => null,
            };
            return new AskResult(answer);
        });
    }

    private static string ToPath(IStorageItem item) =>
        item.TryGetLocalPath() ?? item.Path.ToString();

    private static IReadOnlyList<FilePickerFileType>? BuildFilters(string[]? extensions) =>
        extensions is null || extensions.Length == 0
            ? null
            : [new FilePickerFileType("Supported files")
            {
                Patterns = [.. extensions.Select(static extension => $"*.{extension}")],
            }];
}
