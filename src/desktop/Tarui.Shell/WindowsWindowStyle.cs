using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace Tarui.Shell;

/// <summary>
/// Windows-only extended window styles applied after the native window exists. Avalonia does not expose
/// pre-creation <c>dwExStyle</c> flags, so <c>WS_EX_NOREDIRECTIONBITMAP</c> (Tauri's
/// <c>noRedirectionBitmap</c>) is a best-effort post-creation patch: the flag is added to
/// <c>GWL_EXSTYLE</c> and the frame is refreshed so the compositor drops the redirection surface.
/// Other platforms ignore the request entirely, matching the upstream Windows-only option.
/// </summary>
internal static class WindowsWindowStyle
{
    internal const int GwlExStyle = -20;
    internal const int WsExNoRedirectionBitmap = 0x00200000;

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;

    /// <summary>Returns the style bits with <c>WS_EX_NOREDIRECTIONBITMAP</c> set (idempotent).</summary>
    internal static int WithNoRedirectionBitmap(int exStyle) => exStyle | WsExNoRedirectionBitmap;

    /// <summary>
    /// Applies <c>WS_EX_NOREDIRECTIONBITMAP</c> to an already created native window. Returns false when
    /// the platform is not Windows, the handle is not available yet, or the style cannot be written.
    /// </summary>
    internal static bool TryApply(Window window)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var handle = window.TryGetPlatformHandle()?.Handle ?? 0;
        if (handle == 0)
        {
            return false;
        }

        var current = GetWindowLongW(handle, GwlExStyle);
        if (current == 0 && Marshal.GetLastWin32Error() != 0)
        {
            return false;
        }

        var desired = WithNoRedirectionBitmap(current);
        if (desired == current)
        {
            return true;
        }

        if (SetWindowLongW(handle, GwlExStyle, desired) == 0 && Marshal.GetLastWin32Error() != 0)
        {
            return false;
        }

        // Flush the cached frame so the changed extended style takes effect without resizing/moving.
        SetWindowPos(
            handle,
            0,
            0,
            0,
            0,
            0,
            SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
        return true;
    }

    [DllImport("user32", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLongW(nint hwnd, int index);

    [DllImport("user32", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLongW(nint hwnd, int index, int newStyle);

    [DllImport("user32", SetLastError = true)]
    private static extern bool SetWindowPos(
        nint hwnd,
        nint insertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);
}
