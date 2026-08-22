using System.Text.Json.Serialization;

namespace Tarui.Cli;

/// <summary>
/// Source-generated JSON metadata for the CLI (no runtime reflection, per repository discipline).
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AppManifestDto))]
[JsonSerializable(typeof(LatestManifestDto))]
internal sealed partial class TaruiCliJsonContext : JsonSerializerContext
{
}
