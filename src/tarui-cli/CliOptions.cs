namespace Tarui.Cli;

internal enum TaruiCommand
{
    Help,
    Version,
    Info,
    Dev,
    Build,
    Init
}

/// <summary>Normalized result of command-line parsing.</summary>
internal sealed record CliOptions
{
    public TaruiCommand Command { get; init; }

    /// <summary>--config /app manifest path (default ./tarui.app.json).</summary>
    public string? ManifestPath { get; init; }

    /// <summary>dev --project: desktop .csproj override.</summary>
    public string? Project { get; init; }

    /// <summary>dev --no-watch: use dotnet run instead of dotnet watch run.</summary>
    public bool NoWatch { get; init; }

    /// <summary>--verbose: print child process command lines.</summary>
    public bool Verbose { get; init; }

    /// <summary>build --rid: runtime identifier.</summary>
    public string? Rid { get; init; }

    /// <summary>build --bundle: explicit bundle targets (comma separated).</summary>
    public IReadOnlyList<string>? Bundles { get; init; }

    /// <summary>build --out: output directory.</summary>
    public string? OutDir { get; init; }

    /// <summary>init &lt;name&gt;: application name (positional).</summary>
    public string? Name { get; init; }

    /// <summary>init --template: frontend template (default react-ts).</summary>
    public string? Template { get; init; }

    /// <summary>init --manager: package manager (default pnpm).</summary>
    public string? Manager { get; init; }

    /// <summary>init --output: target directory (default ./&lt;name&gt;).</summary>
    public string? Output { get; init; }

    /// <summary>init --local: reference a local Tarui source tree instead of published packages.</summary>
    public string? Local { get; init; }
}