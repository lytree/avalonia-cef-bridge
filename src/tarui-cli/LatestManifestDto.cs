using System.Text.Json.Serialization;
using Tarui.Contracts;

namespace Tarui.Cli;

/// <summary>
/// The updater manifest written by <c>tarui build</c> as <c>latest.json</c>. The schema and
/// signature algorithm are intentionally identical to <see cref="UpdateManifest"/> so the runtime
/// <c>UpdaterService</c> can verify the CLI's output without a translation layer.
/// </summary>
internal sealed record LatestManifestDto
{
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; init; }

    [JsonPropertyName("version")] public string Version { get; init; } = string.Empty;

    [JsonPropertyName("files")] public IReadOnlyList<string> Files { get; init; } = [];

    [JsonPropertyName("sha256")] public IReadOnlyDictionary<string, string> Sha256 { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    [JsonPropertyName("signature")] public string Signature { get; init; } = string.Empty;

    /// <summary>Maps this DTO onto the runtime <see cref="UpdateManifest"/> record.</summary>
    public UpdateManifest ToContract() =>
        new(SchemaVersion, Version, Files.ToArray(), new Dictionary<string, string>(Sha256, StringComparer.Ordinal), Signature);
}
