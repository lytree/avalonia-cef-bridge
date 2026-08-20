using System.Text.Json;

namespace Tarui.Contracts;

public sealed record LogicalPosition(double X, double Y);

public sealed record LogicalSize(double Width, double Height);

public sealed record MonitorInfo(
    string Name,
    LogicalPosition Position,
    LogicalSize Size,
    LogicalPosition WorkAreaPosition,
    LogicalSize WorkAreaSize,
    double ScaleFactor,
    bool IsPrimary,
    bool IsCurrent);

public sealed record WindowOptions(
    string Label,
    string? Url = null,
    string Title = "tarui.net",
    double Width = 1024,
    double Height = 768,
    double? MinWidth = null,
    double? MinHeight = null,
    double? MaxWidth = null,
    double? MaxHeight = null,
    double? X = null,
    double? Y = null,
    bool Center = true,
    bool Resizable = true,
    bool Decorations = true,
    bool AlwaysOnTop = false,
    bool Visible = true);

public sealed record WindowStateInfo(
    string Label,
    string Title,
    bool IsFocused,
    bool IsFullscreen,
    bool IsMaximized,
    bool IsMinimized,
    bool IsVisible,
    bool IsDecorated,
    bool IsResizable,
    bool IsAlwaysOnTop,
    string Theme,
    double ScaleFactor,
    LogicalPosition Position,
    LogicalSize Size);

public sealed record WindowGeometry(double X, double Y, double Width, double Height);

public sealed record WindowFocusChanged(bool Focused);

public sealed record WindowLabelOptions(string? Label = null);

public sealed record CloseWindowOptions(string? Label = null, bool Force = true);

public sealed record SetTitleOptions(string Title, string? Label = null);

public sealed record SetSizeOptions(double Width, double Height, string? Label = null);

public sealed record SetPositionOptions(double X, double Y, string? Label = null);

public sealed record SetExtentOptions(double? Width, double? Height, string? Label = null);

public sealed record SetFlagOptions(bool Value, string? Label = null);

public sealed record WindowLabels(string[] Labels);

public sealed record EventEmitOptions(
    string Event,
    JsonElement Payload,
    string? TargetWindow = null);
