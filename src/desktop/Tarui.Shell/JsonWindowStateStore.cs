using System.Text.Json;
using Tarui.Contracts;
using Tarui.Plugins.WindowState;

namespace Tarui.Shell;

/// <summary>
/// File-backed store for persisted window state. Snapshots live under the per-user application data
/// directory, one JSON file per window label.
/// </summary>
public sealed class JsonWindowStateStore(string directory) : IWindowStateStore
{
    public ValueTask SaveAsync(string windowLabel, WindowStateSnapshot snapshot, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, TaruiJsonContext.Default.WindowStateSnapshot);
        File.WriteAllBytes(PathFor(windowLabel), bytes);
        return ValueTask.CompletedTask;
    }

    public ValueTask<WindowStateSnapshot?> ReadAsync(string windowLabel, CancellationToken cancellationToken)
    {
        var file = PathFor(windowLabel);
        if (!File.Exists(file))
        {
            return ValueTask.FromResult<WindowStateSnapshot?>(null);
        }

        var snapshot = JsonSerializer.Deserialize(
            File.ReadAllBytes(file),
            TaruiJsonContext.Default.WindowStateSnapshot);
        return ValueTask.FromResult(snapshot);
    }

    public ValueTask ClearAsync(string windowLabel, CancellationToken cancellationToken)
    {
        var file = PathFor(windowLabel);
        if (File.Exists(file))
        {
            File.Delete(file);
        }

        return ValueTask.CompletedTask;
    }

    private string PathFor(string windowLabel)
    {
        if (string.IsNullOrWhiteSpace(windowLabel) ||
            windowLabel.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidOperationException(
                $"Cannot persist window state for invalid label '{windowLabel}'.");
        }

        return Path.Combine(directory, windowLabel + ".json");
    }
}