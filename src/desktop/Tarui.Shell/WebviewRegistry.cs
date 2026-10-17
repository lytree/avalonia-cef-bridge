using Tarui.Ipc;

namespace Tarui.Shell;

/// <summary>
/// Resolves web views as first-class entities, independently of the windows that frame them. Window-
/// targeted events resolve every web view hosted by a window; surface-targeted events resolve one
/// web view by its own label.
/// </summary>
public interface IWebviewSinkRegistry
{
    IReadOnlyCollection<string> WebviewLabels { get; }

    bool TryGetSink(string webviewLabel, out IEventSink sink);

    /// <summary>
    /// Resolves the capability set of a registered web view, used to gate delivery of reserved native
    /// events. A web view inherits its host window's capability profile.
    /// </summary>
    bool TryGetCapabilities(string webviewLabel, out CapabilitySet capabilities);

    /// <summary>The labels of every web view hosted by <paramref name="windowLabel"/>.</summary>
    IReadOnlyList<string> WebviewLabelsFor(string windowLabel);
}

/// <summary>
/// A registered web view: its own label, its host window label, the UI-neutral session that drives it
/// and the visual surface that presents it. A web view belongs to exactly one window.
/// </summary>
public sealed class WebviewEntry
{
    public WebviewEntry(
        string webviewLabel,
        string windowLabel,
        WebviewSession session,
        WebviewPresenter presenter)
    {
        WebviewLabel = webviewLabel;
        WindowLabel = windowLabel;
        Session = session;
        Presenter = presenter;
    }

    public string WebviewLabel { get; }

    public string WindowLabel { get; }

    public WebviewSession Session { get; }

    public WebviewPresenter Presenter { get; }

    public CommandContext Context => Session.Context;
}

/// <summary>
/// The web view registry: web views indexed by their own label, with a secondary index from a window
/// label to the labels of the web views it hosts. This is the entity separation that lets a window
/// host several surfaces while the single-surface case stays a one-element window index.
/// </summary>
public sealed class WebviewRegistry : IWebviewSinkRegistry
{
    private readonly Dictionary<string, WebviewEntry> _byWebview = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _byWindow = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public IReadOnlyCollection<string> WebviewLabels
    {
        get
        {
            lock (_gate)
            {
                return _byWebview.Keys.ToArray();
            }
        }
    }

    public void Add(WebviewEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_gate)
        {
            if (!_byWebview.TryAdd(entry.WebviewLabel, entry))
            {
                throw new InvalidOperationException($"A web view with label '{entry.WebviewLabel}' already exists.");
            }

            if (!_byWindow.TryGetValue(entry.WindowLabel, out var labels))
            {
                labels = [];
                _byWindow[entry.WindowLabel] = labels;
            }

            labels.Add(entry.WebviewLabel);
        }
    }

    public WebviewEntry Get(string webviewLabel)
    {
        lock (_gate)
        {
            if (_byWebview.TryGetValue(webviewLabel, out var entry))
            {
                return entry;
            }
        }

        throw new KeyNotFoundException($"No web view is registered with label '{webviewLabel}'.");
    }

    public bool TryGet(string webviewLabel, out WebviewEntry entry)
    {
        lock (_gate)
        {
            return _byWebview.TryGetValue(webviewLabel, out entry!);
        }
    }

    public bool Remove(string webviewLabel)
    {
        lock (_gate)
        {
            if (!_byWebview.Remove(webviewLabel, out var entry))
            {
                return false;
            }

            UnlinkFromWindow(entry.WindowLabel, webviewLabel);
            return true;
        }
    }

    /// <summary>Detaches every web view hosted by a window and returns the removed entries.</summary>
    public IReadOnlyList<WebviewEntry> RemoveWindow(string windowLabel)
    {
        lock (_gate)
        {
            if (!_byWindow.Remove(windowLabel, out var labels))
            {
                return [];
            }

            var removed = new List<WebviewEntry>(labels.Count);
            foreach (var label in labels)
            {
                if (_byWebview.Remove(label, out var entry))
                {
                    removed.Add(entry);
                }
            }

            return removed;
        }
    }

    public IReadOnlyList<string> WebviewLabelsFor(string windowLabel)
    {
        lock (_gate)
        {
            return _byWindow.TryGetValue(windowLabel, out var labels) ? labels.ToArray() : [];
        }
    }

    public bool TryGetSink(string webviewLabel, out IEventSink sink)
    {
        lock (_gate)
        {
            if (_byWebview.TryGetValue(webviewLabel, out var entry))
            {
                sink = entry.Session;
                return true;
            }
        }

        sink = null!;
        return false;
    }

    public bool TryGetCapabilities(string webviewLabel, out CapabilitySet capabilities)
    {
        lock (_gate)
        {
            if (_byWebview.TryGetValue(webviewLabel, out var entry))
            {
                capabilities = entry.Session.Context.Capabilities;
                return true;
            }
        }

        capabilities = null!;
        return false;
    }

    private void UnlinkFromWindow(string windowLabel, string webviewLabel)
    {
        if (!_byWindow.TryGetValue(windowLabel, out var labels))
        {
            return;
        }

        labels.Remove(webviewLabel);
        if (labels.Count == 0)
        {
            _byWindow.Remove(windowLabel);
        }
    }
}
