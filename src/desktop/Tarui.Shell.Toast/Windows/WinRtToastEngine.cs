using System.Runtime.Versioning;
using Tarui.Plugins.Notification;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace Tarui.Shell.Toast.Windows;

/// <summary>
/// WinRT-backed <see cref="IToastEngine"/>: loads the toast XML produced by
/// <see cref="ToastContentBuilder"/> and shows it through a <see cref="ToastNotifier"/> created
/// under the configured AppUserModelId. Per-toast Activated/Dismissed/Failed events are routed
/// back to the owning service, which owns the dedup state and the event sink.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
internal sealed class WinRtToastEngine(
    ToastConfiguration configuration,
    Action<string, string?> onActivated,
    Action<string> onDismissed,
    Action<string> onFailed) : IToastEngine
{
    private readonly ToastNotifier _notifier = CreateNotifier(configuration);
    private readonly object _gate = new();
    private readonly Dictionary<string, ToastNotification> _active = new(StringComparer.Ordinal);
    private bool _disposed;

    public void Show(string id, string xml)
    {
        var document = new XmlDocument();
        document.LoadXml(xml);
        var toast = new ToastNotification(document);

        // WinRT raises Activated with an untyped object argument that must be cast to
        // ToastActivatedEventArgs; Arguments carries the tapped <action>'s arguments value (the
        // app-defined action id) or an empty string for a plain click on the toast body.
        toast.Activated += (_, eventArgs) =>
        {
            var activatedArgs = (ToastActivatedEventArgs)eventArgs;
            onActivated(id, activatedArgs.Arguments);
        };
        toast.Dismissed += (_, _) => onDismissed(id);
        toast.Failed += (_, _) => onFailed(id);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _active.Add(id, toast);
        }

        try
        {
            _notifier.Show(toast);
        }
        catch
        {
            lock (_gate)
            {
                _active.Remove(id);
            }

            throw;
        }
    }

    public void Hide(string id)
    {
        lock (_gate)
        {
            if (_active.Remove(id, out var toast))
            {
                // Hiding requires the exact ToastNotification instance that was shown.
                _notifier.Hide(toast);
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _active.Clear();
        }
    }

    private static ToastNotifier CreateNotifier(ToastConfiguration configuration)
    {
        ToastAumIdRegistrar.EnsureRegistered(configuration);
        return ToastNotificationManager.CreateToastNotifier(configuration.AumId);
    }
}
