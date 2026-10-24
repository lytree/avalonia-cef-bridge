using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace Tarui.Shell;

/// <summary>
/// Windows-only window effects behind the cross-platform window service: taskbar progress and badge
/// (<c>ITaskbarList3</c>), DWM non-client rendering (frame shadow) and bottom z-order. Every operation
/// degrades to an honest no-op on other platforms, which is reported by <c>core:platform|capabilities</c>.
/// </summary>
internal static class WindowsWindowEffects
{
    internal const int ProgressNoProgress = 0x0000;
    internal const int ProgressIndeterminate = 0x0001;
    internal const int ProgressNormal = 0x0002;
    internal const int ProgressError = 0x0004;
    internal const int ProgressPaused = 0x0008;

    private const int SwpNoSize = 0x0001;
    private const int SwpNoMove = 0x0002;
    private const int SwpNoActivate = 0x0010;

    private const int DwmwaNcRenderingPolicy = 2;
    private const int DwmncrpDisabled = 1;
    private const int DwmncrpEnabled = 2;

    private static readonly nint HwndBottom = 1;

    private static readonly object Gate = new();
    private static readonly Dictionary<nint, nint> BadgeIcons = [];
    private static readonly HashSet<nint> TrackedWindows = [];

    /// <summary>
    /// Pushes the window to the bottom of the z-order when <paramref name="value"/> is true. Windows has
    /// no persistent "always on bottom" state, so the effect is best-effort and lasts until another window
    /// is activated; disabling it is therefore a no-op.
    /// </summary>
    public static void SetAlwaysOnBottom(Window window, bool value)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        if (!value)
        {
            // There is no persistent bottom state to clear; activating the window restores normal order.
            return;
        }

        var handle = GetHandle(window);
        if (handle == 0)
        {
            return;
        }

        SetWindowPos(handle, HwndBottom, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    /// <summary>Toggles the DWM-drawn frame shadow by enabling or disabling non-client rendering.</summary>
    public static void SetShadow(Window window, bool value)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var handle = GetHandle(window);
        if (handle == 0)
        {
            return;
        }

        var policy = value ? DwmncrpEnabled : DwmncrpDisabled;
        if (DwmSetWindowAttribute(handle, DwmwaNcRenderingPolicy, ref policy, sizeof(int)) != 0)
        {
            // The DWM attribute is best-effort; unsupported composition degrades to the existing frame.
        }
    }

    /// <summary>
    /// Applies the badge count as a taskbar overlay icon. Windows has no dock-badge concept, so the count
    /// is rendered locally by <see cref="WindowsBadgeIcon"/>. A null or non-positive count clears it.
    /// </summary>
    public static void SetBadgeCount(Window window, int? count)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var handle = GetHandle(window);
        if (handle == 0)
        {
            return;
        }

        Track(handle, window);

        nint icon = 0;
        string? description = null;
        if (count is > 0)
        {
            description = WindowsBadgeIcon.LabelFor(count.Value);
            icon = WindowsBadgeIcon.Create(description);
            if (icon == 0)
            {
                return;
            }
        }

        nint previous;
        lock (Gate)
        {
            BadgeIcons.Remove(handle, out previous);
        }

        TrySetOverlayIcon(handle, icon, description);
        if (previous != 0)
        {
            DestroyIcon(previous);
        }

        if (icon != 0)
        {
            lock (Gate)
            {
                BadgeIcons[handle] = icon;
            }
        }
    }

    /// <summary>Applies the taskbar progress state; <c>none</c> removes the indicator.</summary>
    public static void SetProgressBar(Window window, string? status, double? progress)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var handle = GetHandle(window);
        if (handle == 0)
        {
            return;
        }

        var flags = ProgressFlagsFor(status);
        var taskbar = TryCreateTaskbarList();
        if (taskbar is null)
        {
            return;
        }

        try
        {
            taskbar.HrInit();
            taskbar.SetProgressState(handle, flags);
            if (flags is ProgressNormal or ProgressPaused or ProgressError)
            {
                var (completed, total) = ProgressValuesFor(progress);
                taskbar.SetProgressValue(handle, completed, total);
            }
        }
        catch (COMException)
        {
            // The taskbar can be unavailable (for example in a headless session); degrade to a no-op.
        }
        finally
        {
            ReleaseComObject(taskbar);
        }
    }

    /// <summary>Maps the wire status onto a <c>TBPFLAG</c>; unknown values fail honestly.</summary>
    internal static int ProgressFlagsFor(string? status) => status switch
    {
        null or "" or "none" => ProgressNoProgress,
        "normal" => ProgressNormal,
        "indeterminate" => ProgressIndeterminate,
        "paused" => ProgressPaused,
        "error" => ProgressError,
        _ => throw new NotSupportedException($"Unknown progress bar status '{status}'."),
    };

    /// <summary>Clamps the percentage to 0-100 and converts it to the 0-1000 taskbar scale.</summary>
    internal static (ulong Completed, ulong Total) ProgressValuesFor(double? progress)
    {
        var clamped = Math.Clamp(progress ?? 0, 0, 100);
        return ((ulong)Math.Round(clamped * 10, MidpointRounding.AwayFromZero), 1000);
    }

    private static void Track(nint handle, Window window)
    {
        lock (Gate)
        {
            if (!TrackedWindows.Add(handle))
            {
                return;
            }
        }

        window.Closed += (_, _) => ReleaseBadge(handle);
    }

    private static void ReleaseBadge(nint handle)
    {
        nint icon;
        lock (Gate)
        {
            TrackedWindows.Remove(handle);
            if (!BadgeIcons.Remove(handle, out icon))
            {
                return;
            }
        }

        if (icon != 0)
        {
            DestroyIcon(icon);
        }
    }

    private static void TrySetOverlayIcon(nint handle, nint icon, string? description)
    {
        var taskbar = TryCreateTaskbarList();
        if (taskbar is null)
        {
            return;
        }

        try
        {
            taskbar.HrInit();
            taskbar.SetOverlayIcon(handle, icon, description);
        }
        catch (COMException)
        {
            // Same honest degradation as the progress indicator.
        }
        finally
        {
            ReleaseComObject(taskbar);
        }
    }

    private static void ReleaseComObject(ITaskbarList3 taskbar)
    {
        if (OperatingSystem.IsWindows())
        {
            Marshal.FinalReleaseComObject(taskbar);
        }
    }

    private static ITaskbarList3? TryCreateTaskbarList()
    {
        try
        {
            object instance = new TaskbarListClass();
            return instance as ITaskbarList3;
        }
        catch (COMException)
        {
            return null;
        }
    }

    private static nint GetHandle(Window window) => window.TryGetPlatformHandle()?.Handle ?? 0;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(nint icon);

    [ComImport]
    [Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
    [ClassInterface(ClassInterfaceType.None)]
    private sealed class TaskbarListClass
    {
    }

    [ComImport]
    [Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        void HrInit();

        void AddTab(nint hwnd);

        void DeleteTab(nint hwnd);

        void ActivateTab(nint hwnd);

        void SetActiveAlt(nint hwnd);

        void MarkFullscreenWindow(nint hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);

        void SetProgressValue(nint hwnd, ulong completed, ulong total);

        void SetProgressState(nint hwnd, int flags);

        void RegisterTab(nint tab, nint mdi);

        void UnregisterTab(nint tab);

        void SetTabOrder(nint tab, nint insertBefore);

        void SetTabActive(nint tab, nint mdi, uint reserved);

        void ThumbBarAddButtons(nint hwnd, uint count, nint buttons);

        void ThumbBarUpdateButtons(nint hwnd, uint count, nint buttons);

        void ThumbBarSetImageList(nint hwnd, nint imageList);

        void SetOverlayIcon(nint hwnd, nint icon, [MarshalAs(UnmanagedType.LPWStr)] string? description);

        void SetThumbnailTooltip(nint hwnd, [MarshalAs(UnmanagedType.LPWStr)] string? tooltip);

        void SetThumbnailClip(nint hwnd, nint clip);
    }
}
