namespace Tarui.Contracts;

/// <summary>Message box icon values. Mirrors the AvaloniaTemplate/Ursa icon set exposed to the frontend.</summary>
public static class MessageBoxIconNames
{
    public const string None = "none";
    public const string Info = "info";
    public const string Warning = "warning";
    public const string Error = "error";
    public const string Question = "question";
    public const string Success = "success";
}

/// <summary>Message box button combination values.</summary>
public static class MessageBoxButtonNames
{
    public const string Ok = "ok";
    public const string OkCancel = "okCancel";
    public const string YesNo = "yesNo";
    public const string YesNoCancel = "yesNoCancel";
}

/// <summary>Message box result values.</summary>
public static class MessageBoxResultNames
{
    public const string Ok = "ok";
    public const string Cancel = "cancel";
    public const string Yes = "yes";
    public const string No = "no";
}

/// <summary>
/// Options for a native message box. <see cref="Icon"/> is one of the
/// <see cref="MessageBoxIconNames"/> values; <see cref="Button"/> is one of the
/// <see cref="MessageBoxButtonNames"/> values.
/// </summary>
public sealed record MessageBoxOptions(
    string Title = "",
    string Content = "",
    string Icon = MessageBoxIconNames.None,
    string Button = MessageBoxButtonNames.Ok);

/// <summary>
/// Result of a native message box. <see cref="Result"/> is one of the
/// <see cref="MessageBoxResultNames"/> values; "cancel" is also returned when the
/// dialog is dismissed through the window close button.
/// </summary>
public sealed record MessageBoxResult(string Result);

/// <summary>
/// Options for a native confirmation dialog. <see cref="Icon"/> follows the
/// <see cref="MessageBoxIconNames"/> values and defaults to "question".
/// </summary>
public sealed record ConfirmOptions(
    string Title = "",
    string Content = "",
    string Icon = MessageBoxIconNames.Question,
    string OkLabel = "OK",
    string CancelLabel = "Cancel");

public sealed record ConfirmResult(bool Confirmed);
