using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tarui.Contracts;

public sealed record InvokeRequest(
    int Protocol,
    string Id,
    string Command,
    JsonElement Payload,
    string WindowLabel = "main",
    string WebViewLabel = "main");

public sealed record InvokeResponse(
    int Protocol,
    string Id,
    bool Success,
    JsonElement? Payload,
    IpcError? Error)
{
    public static InvokeResponse Ok(string id, JsonElement payload) =>
        new(1, id, true, payload, null);

    public static InvokeResponse Fail(string id, string code, string message) =>
        new(1, id, false, null, new IpcError(code, message));
}

public sealed record IpcError(string Code, string Message);

public sealed record ThemeChanged(string Theme);

public sealed record AppHandshake(
    string Product,
    string ShellVersion,
    int BridgeVersion,
    string Platform,
    string[] Capabilities);

public sealed record OpenDialogOptions(
    bool Multiple = false,
    bool Directory = false,
    string[]? Extensions = null);

public sealed record OpenDialogResult(string[] Paths);

public sealed record EmptyArgs;

public sealed record Unit;

public sealed record EventEnvelope(
    string Type,
    string Event,
    JsonElement Payload);

/// <summary>A frame that streams one channel payload to the front-end channel identified by <see cref="Channel"/>.</summary>
public sealed record ChannelEnvelope(
    string Type,
    string Channel,
    JsonElement Payload);

/// <summary>A single progress tick emitted by the <c>core:channel|stream-echo</c> demonstration command.</summary>
public sealed record StreamProgress(int Step, int Total);

/// <summary>Arguments for the streaming echo command: a channel token plus an iteration count.</summary>
public sealed record StreamEchoArgs(string? Channel, int Count);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(InvokeRequest))]
[JsonSerializable(typeof(InvokeResponse))]
[JsonSerializable(typeof(IpcError))]
[JsonSerializable(typeof(ThemeChanged))]
[JsonSerializable(typeof(AppHandshake))]
[JsonSerializable(typeof(OpenDialogOptions))]
[JsonSerializable(typeof(OpenDialogResult))]
[JsonSerializable(typeof(EmptyArgs))]
[JsonSerializable(typeof(Unit))]
[JsonSerializable(typeof(EventEnvelope))]
[JsonSerializable(typeof(ChannelEnvelope))]
[JsonSerializable(typeof(StreamProgress))]
[JsonSerializable(typeof(StreamEchoArgs))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(LogicalPosition))]
[JsonSerializable(typeof(LogicalSize))]
[JsonSerializable(typeof(MonitorInfo))]
[JsonSerializable(typeof(MonitorInfo[]))]
[JsonSerializable(typeof(WindowOptions))]
[JsonSerializable(typeof(WindowStateInfo))]
[JsonSerializable(typeof(WindowGeometry))]
[JsonSerializable(typeof(WindowFocusChanged))]
[JsonSerializable(typeof(WindowLabelOptions))]
[JsonSerializable(typeof(CloseWindowOptions))]
[JsonSerializable(typeof(SetTitleOptions))]
[JsonSerializable(typeof(SetSizeOptions))]
[JsonSerializable(typeof(SetPositionOptions))]
[JsonSerializable(typeof(SetExtentOptions))]
[JsonSerializable(typeof(SetFlagOptions))]
[JsonSerializable(typeof(WindowLabels))]
[JsonSerializable(typeof(EventEmitOptions))]
[JsonSerializable(typeof(PathResolveOptions))]
[JsonSerializable(typeof(PathResolveResult))]
[JsonSerializable(typeof(OsInfo))]
[JsonSerializable(typeof(PlatformCapabilities))]
[JsonSerializable(typeof(ProcessExitOptions))]
[JsonSerializable(typeof(ShellOpenOptions))]
[JsonSerializable(typeof(ShellOpenResult))]
[JsonSerializable(typeof(ClipboardWriteTextOptions))]
[JsonSerializable(typeof(ClipboardReadTextResult))]
[JsonSerializable(typeof(ClipboardWriteHtmlOptions))]
[JsonSerializable(typeof(ClipboardReadHtmlResult))]
[JsonSerializable(typeof(ClipboardWriteImageOptions))]
[JsonSerializable(typeof(ClipboardReadImageResult))]
[JsonSerializable(typeof(CliArgSpec))]
[JsonSerializable(typeof(CliArgSpec[]))]
[JsonSerializable(typeof(CliParseOptions))]
[JsonSerializable(typeof(CliArgValue))]
[JsonSerializable(typeof(CliParseResult))]
[JsonSerializable(typeof(SaveDialogOptions))]
[JsonSerializable(typeof(SaveDialogResult))]
[JsonSerializable(typeof(MessageBoxOptions))]
[JsonSerializable(typeof(MessageBoxResult))]
[JsonSerializable(typeof(ConfirmOptions))]
[JsonSerializable(typeof(ConfirmResult))]
[JsonSerializable(typeof(AskOptions))]
[JsonSerializable(typeof(AskResult))]
[JsonSerializable(typeof(PathScope))]
[JsonSerializable(typeof(PathScope[]))]
[JsonSerializable(typeof(CapabilityGrant))]
[JsonSerializable(typeof(CapabilityGrant[]))]
[JsonSerializable(typeof(CapabilityManifest))]
[JsonSerializable(typeof(FsPathOptions))]
[JsonSerializable(typeof(FsReadTextResult))]
[JsonSerializable(typeof(FsWriteTextOptions))]
[JsonSerializable(typeof(FsReadDirOptions))]
[JsonSerializable(typeof(FsDirEntry[]))]
[JsonSerializable(typeof(FsStatResult))]
[JsonSerializable(typeof(FsMkdirOptions))]
[JsonSerializable(typeof(FsCopyOptions))]
[JsonSerializable(typeof(FsRenameOptions))]
[JsonSerializable(typeof(FsRemoveOptions))]
[JsonSerializable(typeof(FsReadStreamOptions))]
[JsonSerializable(typeof(FsStreamMeta))]
[JsonSerializable(typeof(FsStreamEvent))]
[JsonSerializable(typeof(FsStreamResult))]
[JsonSerializable(typeof(FsWriteBeginOptions))]
[JsonSerializable(typeof(FsWriteBeginResult))]
[JsonSerializable(typeof(FsWriteChunkOptions))]
[JsonSerializable(typeof(FsWriteCommitOptions))]
[JsonSerializable(typeof(FsWriteCancelOptions))]
[JsonSerializable(typeof(FsWatchOptions))]
[JsonSerializable(typeof(FsWatchResult))]
[JsonSerializable(typeof(FsUnwatchOptions))]
[JsonSerializable(typeof(FsWatchEvent))]
[JsonSerializable(typeof(MenuItemDefinition))]
[JsonSerializable(typeof(MenuItemDefinition[]))]
[JsonSerializable(typeof(SetWindowMenuOptions))]
[JsonSerializable(typeof(ContextMenuOptions))]
[JsonSerializable(typeof(MenuUpdateItemOptions))]
[JsonSerializable(typeof(MenuItemClicked))]
[JsonSerializable(typeof(TrayCreateOptions))]
[JsonSerializable(typeof(TraySetMenuOptions))]
[JsonSerializable(typeof(TraySetIconOptions))]
[JsonSerializable(typeof(TraySetTooltipOptions))]
[JsonSerializable(typeof(TraySetVisibleOptions))]
[JsonSerializable(typeof(TrayRemoveOptions))]
[JsonSerializable(typeof(TrayClicked))]
[JsonSerializable(typeof(TrayMenuItemClicked))]
[JsonSerializable(typeof(SecondInstanceArgs))]
[JsonSerializable(typeof(WindowStateSaveOptions))]
[JsonSerializable(typeof(WindowStateRestoreOptions))]
[JsonSerializable(typeof(WindowStateClearOptions))]
[JsonSerializable(typeof(WindowStateSnapshot))]
[JsonSerializable(typeof(WindowStateRestoreResult))]
[JsonSerializable(typeof(NotificationOptions))]
[JsonSerializable(typeof(NotificationPermissionStateResult))]
[JsonSerializable(typeof(NotificationCancelOptions))]
[JsonSerializable(typeof(NotificationEvent))]
[JsonSerializable(typeof(AutostartEnableOptions))]
[JsonSerializable(typeof(AutostartState))]
[JsonSerializable(typeof(GlobalShortcutOptions))]
[JsonSerializable(typeof(GlobalShortcutState))]
[JsonSerializable(typeof(GlobalShortcutTriggered))]
[JsonSerializable(typeof(WebViewFileDropEvent))]
[JsonSerializable(typeof(WebViewDownloadRequestEvent))]
[JsonSerializable(typeof(WebViewNavigationRequestEvent))]
[JsonSerializable(typeof(WebviewLabelOptions))]
[JsonSerializable(typeof(WebviewNavigateOptions))]
[JsonSerializable(typeof(WebviewDevToolsOptions))]
[JsonSerializable(typeof(WebviewStateInfo))]
[JsonSerializable(typeof(WebviewLabels))]
[JsonSerializable(typeof(StoreFileOptions))]
[JsonSerializable(typeof(StoreKeyOptions))]
[JsonSerializable(typeof(StoreSetOptions))]
[JsonSerializable(typeof(StoreGetResult))]
[JsonSerializable(typeof(StoreHasResult))]
[JsonSerializable(typeof(StoreKeysResult))]
[JsonSerializable(typeof(HttpHeader))]
[JsonSerializable(typeof(HttpHeader[]))]
[JsonSerializable(typeof(HttpRequestOptions))]
[JsonSerializable(typeof(HttpResponseResult))]
[JsonSerializable(typeof(HttpStreamMeta))]
[JsonSerializable(typeof(HttpStreamEvent))]
[JsonSerializable(typeof(HttpField))]
[JsonSerializable(typeof(HttpFilePart))]
[JsonSerializable(typeof(HttpUploadOptions))]
[JsonSerializable(typeof(HttpUploadResult))]
[JsonSerializable(typeof(Cookie))]
[JsonSerializable(typeof(Cookie[]))]
[JsonSerializable(typeof(CookieListOptions))]
[JsonSerializable(typeof(CookieListResult))]
[JsonSerializable(typeof(CookieSetOptions))]
[JsonSerializable(typeof(CookieSetResult))]
[JsonSerializable(typeof(CookieDeleteOptions))]
[JsonSerializable(typeof(CookieDeleteResult))]
[JsonSerializable(typeof(ShellSpawnOptions))]
[JsonSerializable(typeof(ShellSpawnResult))]
[JsonSerializable(typeof(ShellWriteStdinOptions))]
[JsonSerializable(typeof(ShellKillOptions))]
[JsonSerializable(typeof(ShellStreamEvent))]
[JsonSerializable(typeof(LogRecordOptions))]
[JsonSerializable(typeof(LogEntry))]
[JsonSerializable(typeof(DeepLinkCurrentResult))]
[JsonSerializable(typeof(DeepLinkFeedOptions))]
[JsonSerializable(typeof(UpdateManifest))]
[JsonSerializable(typeof(UpdateCheckResult))]
[JsonSerializable(typeof(UpdateDownloadResult))]
[JsonSerializable(typeof(UpdateApplyOptions))]
[JsonSerializable(typeof(UpdateApplyResult))]
[JsonSerializable(typeof(UpdaterStatus))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
public partial class TaruiJsonContext : JsonSerializerContext;
