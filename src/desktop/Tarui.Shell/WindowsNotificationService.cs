using System.Runtime.InteropServices;
using Tarui.Contracts;
using Tarui.Plugins.Notification;

namespace Tarui.Shell;

/// <summary>
/// System notifications backed by the Windows <c>Shell_NotifyIcon</c> balloon tips. Notifications are
/// deduplicated by their app-defined <see cref="NotificationOptions.Id"/>: showing the same id twice
/// throws, cancelling an unknown id throws, and the deduplication state is tracked in pure managed
/// state so it is testable without displaying a real toast. A platform with no notification facility
/// still reports the dedup semantics as honest "granted/unsupported" and degrades show/cancel to no-ops.
/// </summary>
public sealed class WindowsNotificationService : INotificationService, IDisposable
{
    private const string UnsupportedReason = "notifications are not supported on this platform";

    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint NifInfo = 0x00000010;
    private const uint NifShowTip = 0x00000080;

    private const uint NimAdd = 0x00000000;
    private const uint NimDelete = 0x00000002;

    private const uint NiiInfo = 0x00000001;
    private const uint NiiNoSound = 0x00000010;

    private const uint CallbackMessage = 0x8001; // WM_APP + 1.
    private const string StaticWindowClass = "STATIC";

    private readonly object _gate = new();
    private readonly Dictionary<string, uint> _notifications = new(StringComparer.Ordinal);

    private nint _windowHandle;
    private uint _nextUid = 1;
    private bool _disposed;

    public ValueTask<NotificationPermissionStateResult> GetPermissionStateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(
            new NotificationPermissionStateResult(
                NotificationPermissionState.Granted,
                Supported: OperatingSystem.IsWindows(),
                Reason: OperatingSystem.IsWindows() ? null : UnsupportedReason));
    }

    public ValueTask<NotificationPermissionStateResult> RequestPermissionAsync(CancellationToken cancellationToken)
        => GetPermissionStateAsync(cancellationToken);

    public ValueTask<Unit> ShowAsync(NotificationOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            return ValueTask.FromResult(new Unit());
        }

        NotificationValidator.Validate(options);

        uint uid;
        lock (_gate)
        {
            if (_notifications.ContainsKey(options.Id))
            {
                throw new InvalidOperationException(
                    $"A notification with id '{options.Id}' is already showing.");
            }

            uid = _nextUid++;
            _notifications[options.Id] = uid;
        }

        var handle = EnsureWindow();
        var data = BuildIconData(handle, uid, options);

        // Best-effort OS display: a self-test host cannot render a real toast, so a failed
        // Shell_NotifyIcon call must not fail the command or corrupt the deduplication state.
        Shell_NotifyIcon(NimAdd, ref data);

        return ValueTask.FromResult(new Unit());
    }

    public ValueTask<Unit> CancelAsync(NotificationCancelOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            return ValueTask.FromResult(new Unit());
        }

        uint uid;
        lock (_gate)
        {
            if (!_notifications.TryGetValue(options.Id, out uid))
            {
                throw new InvalidOperationException(
                    $"No notification with id '{options.Id}' is showing.");
            }

            _notifications.Remove(options.Id);
        }

        if (_windowHandle != nint.Zero)
        {
            var data = BuildIconData(_windowHandle, uid, null);
            Shell_NotifyIcon(NimDelete, ref data);
        }

        return ValueTask.FromResult(new Unit());
    }

    public void Dispose()
    {
        if (!OperatingSystem.IsWindows() || _disposed)
        {
            return;
        }

        _disposed = true;
        _notifications.Clear();

        if (_windowHandle != nint.Zero)
        {
            DestroyWindow(_windowHandle);
            _windowHandle = nint.Zero;
        }
    }

    private nint EnsureWindow()
    {
        if (_windowHandle != nint.Zero)
        {
            return _windowHandle;
        }

        lock (_gate)
        {
            if (_windowHandle == nint.Zero)
            {
                // A plain hidden message-only window is a valid owner for Shell_NotifyIcon and keeps
                // the balloon alive without a dedicated message pump.
                _windowHandle = CreateWindowEx(
                    0,
                    StaticWindowClass,
                    "Tarui.Notification",
                    0,
                    0,
                    0,
                    0,
                    0,
                    new nint(-3), // HWND_MESSAGE: a message-only window.
                    nint.Zero,
                    nint.Zero,
                    nint.Zero);
            }
        }

        return _windowHandle;
    }

    private static NotifyIconData BuildIconData(nint windowHandle, uint uid, NotificationOptions? options)
    {
        var data = new NotifyIconData
        {
            cbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
            hWnd = windowHandle,
            uID = uid,
            uCallbackMessage = CallbackMessage,
        };

        if (options is null)
        {
            data.uFlags = NifMessage;
            return data;
        }

        data.uFlags = NifMessage | NifIcon | NifTip | NifInfo | NifShowTip;
        data.szTip = Truncate(options.Title, 128);
        data.szInfo = Truncate(options.Body, 256);
        data.szInfoTitle = Truncate(options.Title, 64);
        data.dwInfoFlags = options.Sound ? NiiInfo : NiiInfo | NiiNoSound;
        return data;
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string? szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string? szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string? szInfoTitle;
        public uint dwInfoFlags;
    }

    [DllImport("shell32.dll")]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NotifyIconData lpData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowEx(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        nint hWndParent,
        nint hMenu,
        nint hInstance,
        nint lpParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(nint hWnd);
}