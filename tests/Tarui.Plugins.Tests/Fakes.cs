using System.Text.Json;
using Tarui.Contracts;
using Tarui.Ipc;
using Tarui.Plugins.Dialog;
using Tarui.Plugins.Events;
using Tarui.Plugins.System;
using Tarui.Plugins.Webview;
using Tarui.Plugins.Window;

namespace Tarui.Plugins.Tests;

internal sealed class FakeWindowService : IWindowService
{
    public List<string> Calls { get; } = [];

    public string[] Labels { get; set; } = ["main"];

    public MonitorInfo[] Monitors { get; set; } = [];

    public ValueTask<Unit> CreateAsync(WindowOptions options, CommandContext callerContext, CancellationToken cancellationToken)
    {
        Calls.Add($"create|{options.Label}|{options.Title}|{options.Width}x{options.Height}");
        return ValueTask.FromResult(new Unit());
    }

    public ValueTask<Unit> CloseAsync(string label, bool force, CancellationToken cancellationToken)
    {
        Calls.Add($"close|{label}|{force}");
        return ValueTask.FromResult(new Unit());
    }

    public ValueTask<Unit> MinimizeAsync(string label, CancellationToken cancellationToken) =>
        RecordUnit($"minimize|{label}");

    public ValueTask<Unit> MaximizeAsync(string label, CancellationToken cancellationToken) =>
        RecordUnit($"maximize|{label}");

    public ValueTask<Unit> UnmaximizeAsync(string label, CancellationToken cancellationToken) =>
        RecordUnit($"unmaximize|{label}");

    public ValueTask<Unit> ToggleMaximizeAsync(string label, CancellationToken cancellationToken) =>
        RecordUnit($"toggle-maximize|{label}");

    public ValueTask<Unit> HideAsync(string label, CancellationToken cancellationToken) =>
        RecordUnit($"hide|{label}");

    public ValueTask<Unit> ShowAsync(string label, CancellationToken cancellationToken) =>
        RecordUnit($"show|{label}");

    public ValueTask<Unit> FocusAsync(string label, CancellationToken cancellationToken) =>
        RecordUnit($"focus|{label}");

    public ValueTask<Unit> CenterAsync(string label, CancellationToken cancellationToken) =>
        RecordUnit($"center|{label}");

    public ValueTask<Unit> SetTitleAsync(string label, string title, CancellationToken cancellationToken) =>
        RecordUnit($"set-title|{label}|{title}");

    public ValueTask<Unit> SetSizeAsync(string label, double width, double height, CancellationToken cancellationToken) =>
        RecordUnit($"set-size|{label}|{width}x{height}");

    public ValueTask<Unit> SetPositionAsync(string label, double x, double y, CancellationToken cancellationToken) =>
        RecordUnit($"set-position|{label}|{x},{y}");

    public ValueTask<Unit> SetMinSizeAsync(string label, double? width, double? height, CancellationToken cancellationToken) =>
        RecordUnit($"set-min-size|{label}|{width}x{height}");

    public ValueTask<Unit> SetMaxSizeAsync(string label, double? width, double? height, CancellationToken cancellationToken) =>
        RecordUnit($"set-max-size|{label}|{width}x{height}");

    public ValueTask<Unit> SetAlwaysOnTopAsync(string label, bool value, CancellationToken cancellationToken) =>
        RecordUnit($"set-always-on-top|{label}|{value}");

    public ValueTask<Unit> SetResizableAsync(string label, bool value, CancellationToken cancellationToken) =>
        RecordUnit($"set-resizable|{label}|{value}");

    public ValueTask<Unit> SetDecorationsAsync(string label, bool value, CancellationToken cancellationToken) =>
        RecordUnit($"set-decorations|{label}|{value}");

    public ValueTask<Unit> SetFullscreenAsync(string label, bool value, CancellationToken cancellationToken) =>
        RecordUnit($"set-fullscreen|{label}|{value}");

    public ValueTask<WindowStateInfo> GetStateAsync(string label, CancellationToken cancellationToken)
    {
        Calls.Add($"get-state|{label}");
        return ValueTask.FromResult(
            new WindowStateInfo(
                label,
                "main-title",
                IsFocused: true,
                IsFullscreen: false,
                IsMaximized: false,
                IsMinimized: false,
                IsVisible: true,
                IsDecorated: true,
                IsResizable: true,
                IsAlwaysOnTop: false,
                "light",
                1.0,
                new LogicalPosition(10, 20),
                new LogicalSize(800, 600)));
    }

    public ValueTask<MonitorInfo?> GetCurrentMonitorAsync(string label, CancellationToken cancellationToken)
    {
        Calls.Add($"current-monitor|{label}");
        return ValueTask.FromResult(Monitors.FirstOrDefault(static monitor => monitor.IsCurrent));
    }

    public ValueTask<MonitorInfo?> GetPrimaryMonitorAsync(CancellationToken cancellationToken)
    {
        Calls.Add("primary-monitor");
        return ValueTask.FromResult(Monitors.FirstOrDefault(static monitor => monitor.IsPrimary));
    }

    public ValueTask<IReadOnlyList<MonitorInfo>> GetMonitorsAsync(string label, CancellationToken cancellationToken)
    {
        Calls.Add($"monitors|{label}");
        return ValueTask.FromResult<IReadOnlyList<MonitorInfo>>(Monitors);
    }

    public ValueTask<string[]> ListAsync(CancellationToken cancellationToken)
    {
        Calls.Add("list");
        return ValueTask.FromResult(Labels);
    }

    private ValueTask<Unit> RecordUnit(string call)
    {
        Calls.Add(call);
        return ValueTask.FromResult(new Unit());
    }
}

internal sealed class FakeWebviewService : IWebviewService
{
    public List<string> Calls { get; } = [];

    public string[] Labels { get; set; } = ["main", "editor"];

    public ValueTask<Unit> NavigateAsync(string webviewLabel, string url, CancellationToken cancellationToken)
    {
        Calls.Add($"navigate|{webviewLabel}|{url}");
        return ValueTask.FromResult(new Unit());
    }

    public ValueTask<WebviewStateInfo> GetStateAsync(string webviewLabel, CancellationToken cancellationToken)
    {
        Calls.Add($"get-state|{webviewLabel}");
        return ValueTask.FromResult(
            new WebviewStateInfo(webviewLabel, $"{webviewLabel}-window", $"{webviewLabel}://start", $"{webviewLabel}-title"));
    }

    public ValueTask<string[]> ListAsync(CancellationToken cancellationToken)
    {
        Calls.Add("list");
        return ValueTask.FromResult(Labels);
    }
}

internal sealed class FakeEventSender : IEventSender
{
    public List<(string Event, JsonElement Payload, string? TargetWindow)> Emitted { get; } = [];

    public ValueTask<Unit> EmitAsync(
        string eventName,
        JsonElement payload,
        string? targetWindow,
        CancellationToken cancellationToken)
    {
        Emitted.Add((eventName, payload, targetWindow));
        return ValueTask.FromResult(new Unit());
    }
}

internal sealed class FakeSystemServices
{
    public List<string> ProcessCalls { get; } = [];

    public FakePathService Paths { get; } = new();

    public FakeOsService Os { get; } = new();

    public FakeProcessService Process { get; } = new();

    public FakeShellService Shell { get; } = new();

    public FakeClipboardService Clipboard { get; } = new();
}

internal sealed class FakePathService : IPathService
{
    public string Resolve(string kind, string? relativePath) =>
        $"/resolved/{kind}/{relativePath}";
}

internal sealed class FakeOsService : IOsService
{
    public OsInfo GetInfo() =>
        new("windows", "x86_64", "10.0.26100", "windows", "en-US");
}

internal sealed class FakeProcessService : IProcessService
{
    public List<string> Calls { get; } = [];

    public void Shutdown(int code) => Calls.Add($"shutdown:{code}");

    public void Relaunch() => Calls.Add("relaunch");
}

internal sealed class FakeShellService : IShellService
{
    public ShellOpenResult Open(string target) => new(true);
}

internal sealed class FakeClipboardService : IClipboardService
{
    public string Text { get; private set; } = string.Empty;

    public ValueTask<string> ReadTextAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(Text);

    public ValueTask<Unit> WriteTextAsync(string text, CancellationToken cancellationToken)
    {
        Text = text;
        return ValueTask.FromResult(new Unit());
    }
}

internal sealed class FakeDialogService : IDialogService
{
    public List<string> WindowLabels { get; } = [];

    public List<(string Icon, string Button)> Messages { get; } = [];

    public List<string> Confirms { get; } = [];

    public ValueTask<OpenDialogResult> OpenAsync(
        OpenDialogOptions options,
        string windowLabel,
        CancellationToken cancellationToken)
    {
        WindowLabels.Add(windowLabel);
        return ValueTask.FromResult(new OpenDialogResult(["C:/tmp/a.txt"]));
    }

    public ValueTask<SaveDialogResult> SaveAsync(
        SaveDialogOptions options,
        string windowLabel,
        CancellationToken cancellationToken)
    {
        WindowLabels.Add(windowLabel);
        return ValueTask.FromResult(
            new SaveDialogResult(options.DefaultName is null ? null : $"C:/tmp/{options.DefaultName}"));
    }

    public ValueTask<MessageBoxResult> MessageAsync(
        MessageBoxOptions options,
        string windowLabel,
        CancellationToken cancellationToken)
    {
        WindowLabels.Add(windowLabel);
        Messages.Add((options.Icon, options.Button));
        return ValueTask.FromResult(new MessageBoxResult(MessageBoxResultNames.Ok));
    }

    public ValueTask<ConfirmResult> ConfirmAsync(
        ConfirmOptions options,
        string windowLabel,
        CancellationToken cancellationToken)
    {
        WindowLabels.Add(windowLabel);
        Confirms.Add($"{options.Title}|{options.Icon}|{options.OkLabel}|{options.CancelLabel}");
        return ValueTask.FromResult(new ConfirmResult(true));
    }
}
