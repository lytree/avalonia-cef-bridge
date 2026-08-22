namespace Tarui.Cli;

/// <summary>Static CLI identity and help text (kept in sync with Directory.Build.props TaruiVersion).</summary>
internal static class CliInfo
{
    public const string Version = "0.1.0";

    public const string HelpText =
        """
        tarui - Tarui development CLI

        Usage:
          tarui <command> [options]

        Commands:
          init      Scaffold a new application from the tarui-app template
          dev       Run the frontend dev server and the desktop app with hot reload
          build     Build the frontend, publish the desktop app and bundle distributables
          info      Print environment, toolchain and manifest diagnostics
          --help, -h       Show this help
          --version, -V    Print the CLI version

        Global options:
          --config <path>    Path to tarui.app.json (default: ./tarui.app.json)

        'tarui init' options:
          <name>             Application name (e.g. tarui init my-app)
          --template <t>     Frontend template (default: react-ts)
          --manager <m>      Package manager to install frontend deps (default: pnpm)
          --output <dir>     Target directory (default: ./<name>)
          --local <repo>     Reference a local Tarui source tree instead of published packages

        'tarui dev' options:
          --project <path>   Desktop .csproj to run (overrides manifest build.desktopProject)
          --no-watch         Use 'dotnet run' instead of 'dotnet watch run'
          --verbose          Print child process command lines

        'tarui build' options:
          --rid <rid>        Runtime identifier, e.g. win-x64 (default: current platform)
          --bundle <csv>     Bundle targets to produce, e.g. zip (default: manifest bundle.targets)
          --out <dir>        Output directory (default: ./dist)
          --verbose          Print child process command lines
        """;
}
