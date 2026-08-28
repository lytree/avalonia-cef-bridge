using System.Runtime.InteropServices;
using System.Text.Json;
using Tarui.Contracts;
using Tarui.Ipc;
using Tarui.Plugins.GlobalShortcut;

namespace Tarui.Shell;

/// <summary>
/// Process-wide keyboard shortcuts backed by the Windows <c>RegisterHotKey</c> API. Registration is
/// globally unique inside this process: re-registering an accelerator returns a stable failure rather
/// than throwing. A hidden message window is created on a dedicated thread that pumps messages, so a
/// <c>WM_HOTKEY</c> delivers the <c>global-shortcut://triggered</c> event to every authorized window.
/// On any other platform every operation degrades honestly to a not-registered / no-op result.
/// </summary>
public sealed class WindowsGlobalShortcutService(EventRouter events) : IGlobalShortcutService, IDisposable
{
    private const string TriggeredEvent = "global-shortcut://triggered";
    private const string WindowClassName = "Tarui.GlobalShortcut.MessageWindow";

    private const uint WmHotkey = 0x0312;
    private const uint WmDestroy = 0x0002;
    private const uint WmQuit = 0x0012;

    private static readonly WndProcDelegate WndProc = MessageWindowProc;
    private static readonly nint WndProcPtr = Marshal.GetFunctionPointerForDelegate(WndProc);

    private readonly object _gate = new();
    private readonly Dictionary<string, int> _idsByAccelerator = new(StringComparer.Ordinal);
    private readonly Dictionary<int, string> _acceleratorsById = new();
    private readonly ManualResetEventSlim _windowReady = new();

    private Thread? _messageThread;
    private uint _threadId;
    private nint _windowHandle;
    private GCHandle? _instanceHandle;
    private int _nextId = 1;
    private bool _disposed;

    public ValueTask<GlobalShortcutState> RegisterAsync(
        GlobalShortcutOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            return ValueTask.FromResult(new GlobalShortcutState(Registered: false));
        }

        var normalized = AcceleratorSpec.Parse(options.Accelerator).Normalized;
        var handle = EnsureWindow();

        lock (_gate)
        {
            if (_idsByAccelerator.ContainsKey(normalized))
            {
                return ValueTask.FromResult(new GlobalShortcutState(Registered: false));
            }

            var (modifiers, virtualKey) = ToWin32(options.Accelerator);
            var id = _nextId++;

            if (RegisterHotKey(handle, id, modifiers | ModNoRepeat, virtualKey) == 0)
            {
                return ValueTask.FromResult(new GlobalShortcutState(Registered: false));
            }

            _idsByAccelerator[normalized] = id;
            _acceleratorsById[id] = normalized;
            return ValueTask.FromResult(new GlobalShortcutState(Registered: true));
        }
    }

    public ValueTask<Unit> UnregisterAsync(
        GlobalShortcutOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            return ValueTask.FromResult(new Unit());
        }

        var normalized = AcceleratorSpec.Parse(options.Accelerator).Normalized;

        lock (_gate)
        {
            if (_idsByAccelerator.TryGetValue(normalized, out var id))
            {
                UnregisterHotKey(_windowHandle, id);
                _idsByAccelerator.Remove(normalized);
                _acceleratorsById.Remove(id);
            }
        }

        return ValueTask.FromResult(new Unit());
    }

    public ValueTask<Unit> UnregisterAllAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            return ValueTask.FromResult(new Unit());
        }

        lock (_gate)
        {
            foreach (var pair in _idsByAccelerator.ToArray())
            {
                UnregisterHotKey(_windowHandle, pair.Value);
                _idsByAccelerator.Remove(pair.Key);
                _acceleratorsById.Remove(pair.Value);
            }
        }

        return ValueTask.FromResult(new Unit());
    }

    public ValueTask<GlobalShortcutState> IsRegisteredAsync(
        GlobalShortcutOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            return ValueTask.FromResult(new GlobalShortcutState(Registered: false));
        }

        var normalized = AcceleratorSpec.Parse(options.Accelerator).Normalized;

        bool registered;
        lock (_gate)
        {
            registered = _idsByAccelerator.ContainsKey(normalized);
        }

        return ValueTask.FromResult(new GlobalShortcutState(Registered: registered));
    }

    public void Dispose()
    {
        if (!OperatingSystem.IsWindows() || _disposed)
        {
            return;
        }

        _disposed = true;

        lock (_gate)
        {
            foreach (var pair in _idsByAccelerator.ToArray())
            {
                UnregisterHotKey(_windowHandle, pair.Value);
            }

            _idsByAccelerator.Clear();
            _acceleratorsById.Clear();
        }

        if (_windowHandle != nint.Zero)
        {
            SetWindowLongPtr(_windowHandle, GwlUserData, nint.Zero);
        }

        var thread = _messageThread;
        if (thread is not null && thread.IsAlive)
        {
            PostThreadMessage(_threadId, WmQuit, nint.Zero, nint.Zero);
            thread.Join();
        }

        _instanceHandle?.Free();
        _instanceHandle = null;
        _windowReady.Dispose();
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
                _instanceHandle = GCHandle.Alloc(this);
                _messageThread = new Thread(MessageLoop)
                {
                    IsBackground = true,
                    Name = "Tarui.GlobalShortcut.Pump",
                };
                _messageThread.Start();
                _windowReady.Wait();
            }
        }

        return _windowHandle;
    }

    private void MessageLoop()
    {
        var windowClass = new WndClass
        {
            lpfnWndProc = WndProcPtr,
            hInstance = GetModuleHandle(null),
            lpszClassName = WindowClassName,
        };

        if (RegisterClass(ref windowClass) == 0 && Marshal.GetLastWin32Error() != (int)Win32Error.ClassAlreadyExists)
        {
            _windowReady.Set();
            return;
        }

        _threadId = GetCurrentThreadId();
        _windowHandle = CreateWindowEx(
            0,
            WindowClassName,
            "Tarui.GlobalShortcut",
            0,
            0,
            0,
            0,
            0,
            new nint(-3), // HWND_MESSAGE: a message-only window.
            nint.Zero,
            nint.Zero,
            nint.Zero);

        if (_instanceHandle.HasValue)
        {
            SetWindowLongPtr(_windowHandle, GwlUserData, GCHandle.ToIntPtr(_instanceHandle.Value));
        }

        _windowReady.Set();

        while (GetMessage(out var message, nint.Zero, 0, 0))
        {
            TranslateMessage(ref message);
            DispatchMessage(ref message);
        }

        DestroyWindow(_windowHandle);
        _windowHandle = nint.Zero;
    }

    private static nint MessageWindowProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == WmHotkey)
        {
            var userData = GetWindowLongPtr(hWnd, GwlUserData);
            if (userData != nint.Zero)
            {
                var instance = (WindowsGlobalShortcutService)GCHandle.FromIntPtr(userData).Target!;
                FireAndForget.Run(instance.OnHotkeyAsync((int)wParam));
            }

            return nint.Zero;
        }

        if (msg == WmDestroy)
        {
            PostQuitMessage(0);
            return nint.Zero;
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private async ValueTask OnHotkeyAsync(int id)
    {
        string? normalized;
        lock (_gate)
        {
            _acceleratorsById.TryGetValue(id, out normalized);
        }

        if (normalized is null)
        {
            return;
        }

        await events.EmitToAllAsync(
            TriggeredEvent,
            JsonSerializer.SerializeToElement(new GlobalShortcutTriggered(normalized), TaruiJsonContext.Default.GlobalShortcutTriggered));
    }

    private static (uint Modifiers, uint VirtualKey) ToWin32(string accelerator)
    {
        var spec = AcceleratorSpec.Parse(accelerator);

        uint modifiers = 0;
        if (spec.Control)
        {
            modifiers |= ModControl;
        }

        if (spec.Shift)
        {
            modifiers |= ModShift;
        }

        if (spec.Alt)
        {
            modifiers |= ModAlt;
        }

        if (spec.Meta)
        {
            modifiers |= ModWin;
        }

        uint virtualKey;
        if (spec.Key.Length == 1 && char.IsLetterOrDigit(spec.Key[0]))
        {
            virtualKey = (uint)char.ToUpperInvariant(spec.Key[0]);
        }
        else if (spec.Key.Length is >= 2 and <= 3 &&
                 spec.Key[0] == 'F' &&
                 int.TryParse(spec.Key[1..], out var fn) && fn is >= 1 and <= 24)
        {
            virtualKey = 0x70u + (uint)(fn - 1); // VK_F1 .. VK_F24.
        }
        else
        {
            throw new ArgumentException(
                $"Accelerator key '{spec.Key}' cannot be mapped to a Win32 virtual-key code.",
                nameof(accelerator));
        }

        return (modifiers, virtualKey);
    }



    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClass(ref WndClass windowClass);

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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);

    [DllImport("user32.dll")]
    private static extern bool GetMessage(out Msg message, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Msg message);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessage(ref Msg message);

    [DllImport("user32.dll")]
    private static extern nint DefWindowProc(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint idThread, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool PostQuitMessage(int exitCode);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? lpModuleName);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private const int GwlUserData = -21;

    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;

    private enum Win32Error
    {
        ClassAlreadyExists = 1410,
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClass
    {
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public string? lpszMenuName;
        public string? lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public Point pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    private delegate nint WndProcDelegate(nint hWnd, uint msg, nint wParam, nint lParam);
}