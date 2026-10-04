using System.Diagnostics.CodeAnalysis;
using Tarui.Contracts;
using Tarui.Plugins.Notification;

namespace Tarui.Shell.Toast;

/// <summary>
/// Windows notification backend built on real toast notifications (Action Center, interactive
/// action buttons, activated/dismissed lifecycle events) instead of balloon tips. The toast XML is
/// produced by <see cref="ToastContentBuilder"/>; lifecycle callbacks are delivered to the injected
/// <see cref="INotificationEventSink"/> so the shell can forward them to the Web side as
/// <c>notification://activated</c>/<c>notification://dismissed</c> events.
/// </summary>
/// <remarks>
/// The WinRT engine is only compiled into the windows flavor of this assembly; without it the
/// service transparently degrades every show/cancel to the injected fallback backend while keeping
/// the dedup semantics (a duplicate id throws, cancelling an unknown id throws). This keeps the
/// net10.0 asset — and every non-Windows host — honest without a second implementation.
/// </remarks>
public sealed class WindowsToastNotificationService : INotificationService, IDisposable
{
    private const string UnsupportedReason = "notifications are not supported on this platform";

    private readonly ToastConfiguration _configuration;
    private readonly INotificationEventSink _sink;
    private readonly INotificationService _fallback;
    private readonly object _gate = new();
    private readonly Dictionary<string, NotificationOptions> _toastActive = new(StringComparer.Ordinal);
    private readonly HashSet<string> _fallbackActive = new(StringComparer.Ordinal);
    private readonly IToastEngine? _engine;
    private bool _disposed;

    public WindowsToastNotificationService(
        ToastConfiguration configuration,
        INotificationEventSink sink,
        INotificationService fallback)
    {
        _configuration = configuration;
        _sink = sink;
        _fallback = fallback;
        _engine = CreateEngine();
    }

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

    public async ValueTask<Unit> ShowAsync(NotificationOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        NotificationValidator.Validate(options);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_toastActive.ContainsKey(options.Id) || _fallbackActive.Contains(options.Id))
            {
                throw new InvalidOperationException(
                    $"A notification with id '{options.Id}' is already showing.");
            }

            if (_engine is not null)
            {
                // Pre-book the id so an activation/dismissal racing the OS Show call is observed.
                _toastActive.Add(options.Id, options);
            }
        }

        if (_engine is not null)
        {
            try
            {
                _engine.Show(options.Id, ToastContentBuilder.Build(options));
                return new Unit();
            }
            catch (Exception)
            {
                // Toast infrastructure failed (unregisterable AUMID, WinRT fault, ...): release the
                // dedup slot and degrade this show to the fallback backend so the notification
                // still reaches the user.
                lock (_gate)
                {
                    _toastActive.Remove(options.Id);
                }
            }
        }

        lock (_gate)
        {
            _fallbackActive.Add(options.Id);
        }

        try
        {
            return await _fallback.ShowAsync(options, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            lock (_gate)
            {
                _fallbackActive.Remove(options.Id);
            }

            throw;
        }
    }

    public async ValueTask<Unit> CancelAsync(NotificationCancelOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        bool toastTracked;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            toastTracked = _toastActive.Remove(options.Id);
            if (!toastTracked && !_fallbackActive.Remove(options.Id))
            {
                throw new InvalidOperationException(
                    $"No notification with id '{options.Id}' is showing.");
            }
        }

        if (toastTracked)
        {
            try
            {
                _engine?.Hide(options.Id);
            }
            catch (Exception)
            {
                // Hiding is best-effort: a failing OS call must not fail the command or corrupt
                // dedup state; the toast simply stays visible until clicked or expired.
            }

            return new Unit();
        }

        return await _fallback.CancelAsync(options, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _toastActive.Clear();
            _fallbackActive.Clear();
        }

        _engine?.Dispose();
    }

#if TARUI_TOAST_WINRT
    // Deliberate interface indirection: the engine is an exchangeable backend shared with the
    // engine-less net10.0 flavor of this service.
#pragma warning disable CA1859
    private IToastEngine? CreateEngine()
        => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)
            ? new Windows.WinRtToastEngine(_configuration, OnEngineActivated, OnEngineDismissed, OnEngineFailed)
            : null;
#pragma warning restore CA1859
#else
    private static IToastEngine? CreateEngine()
        // The net10.0 asset carries no WinRT projections: without an engine every operation
        // degrades to the injected fallback backend.
        => null;
#endif

    private void OnEngineActivated(string id, string? actionArguments)
    {
        if (!TryTakeToast(id, out var options))
        {
            return;
        }

        // A plain click on the toast body reports empty arguments; an <action> tap reports the
        // app-defined action id stored in the button's arguments attribute.
        _sink.DispatchActivated(new NotificationEvent(
            id,
            options.Title,
            options.Body,
            string.IsNullOrEmpty(actionArguments) ? null : actionArguments));
    }

    private void OnEngineDismissed(string id)
    {
        if (!TryTakeToast(id, out var options))
        {
            return;
        }

        _sink.DispatchDismissed(new NotificationEvent(id, options.Title, options.Body));
    }

    private void OnEngineFailed(string id)
    {
        // A failed toast was never displayed: release the dedup slot without lifecycle events.
        TryTakeToast(id, out _);
    }

    private bool TryTakeToast(string id, [NotNullWhen(true)] out NotificationOptions? options)
    {
        lock (_gate)
        {
            if (_toastActive.TryGetValue(id, out var candidate))
            {
                _toastActive.Remove(id);
                options = candidate;
                return true;
            }
        }

        options = null;
        return false;
    }
}
