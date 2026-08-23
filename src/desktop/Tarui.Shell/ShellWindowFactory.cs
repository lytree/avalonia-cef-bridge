using Tarui.Contracts;

namespace Tarui.Shell;

/// <summary>
/// Creates an empty <see cref="ShellWindow"/> from <see cref="WindowOptions"/>. It only materializes the
/// native window frame and its content slot — web view surfaces are mounted later by
/// <see cref="WebviewAttacher"/>, so creating a window never implies or requires a web view.
/// </summary>
public sealed class ShellWindowFactory
{
    public static ShellWindow Create(WindowOptions options) => new(options);
}