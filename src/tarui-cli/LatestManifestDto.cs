using System.Text.Json.Serialization;

namespace Tarui.Cli;

/// <summary>
/// Updater blueprint manifest (<c>latest.json</c>). Field names and the signature
/// algorithm are placeholders frozen when the Updater plugin is scoped
/// (docs/dev-workflow-design.md §5.5).
/// </summary>
internal sealed class LatestManifestDto
{
    [JsonPropertyName("version")] public string? Version { get; set; }

    [JsonPropertyName("url")] public string? Url { get; set; }

    [JsonPropertyName("sha256")] public string? Sha256 { get; set; }

    [JsonPropertyName("signature")] public string? Signature { get; set; }
}
