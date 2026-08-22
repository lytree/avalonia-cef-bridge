using Tarui.Cli;

namespace Tarui.Cli.Tests;

internal static class Program
{
    public static int Main()
    {
        try
        {
            Parser();
            ManifestLoading();
            ManifestValidation();
            PathResolution();
            Tooling();
            LatestManifest();
            Init();
            Plugin();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }

        Console.WriteLine("Tarui.Cli self-tests passed.");
        return 0;
    }

    private static void Parser()
    {
        ParsesDevCommand();
        ParsesBuildCommand();
        ParsesHelpAndVersion();
        ParsesOptions();
        ParsesInlineValues();
        UnknownOptionThrowsUsage();
        UnknownCommandThrowsUsage();
        MissingValueThrowsUsage();
        UnexpectedPositionalThrowsUsage();
    }

    private static void ParsesDevCommand()
    {
        var options = CommandLineParser.Parse(["dev"]);
        Assert(options.Command == TaruiCommand.Dev, "Parse(['dev']) must select the Dev command.");
    }

    private static void ParsesBuildCommand()
    {
        var options = CommandLineParser.Parse(["build", "--rid", "win-x64", "--bundle", "zip"]);
        Assert(options.Command == TaruiCommand.Build, "Parse(['build', ...]) must select the Build command.");
        Assert(options.Rid == "win-x64", "--rid must be captured.");
        Assert(options.Bundles is ["zip"], "--bundle must split into a single zip target.");
    }

    private static void ParsesHelpAndVersion()
    {
        Assert(CommandLineParser.Parse(["-h"]).Command == TaruiCommand.Help, "-h must map to Help.");
        Assert(CommandLineParser.Parse(["--version"]).Command == TaruiCommand.Version, "--version must map to Version.");
        Assert(CommandLineParser.Parse([]).Command == TaruiCommand.Help, "No arguments must default to Help.");
    }

    private static void ParsesOptions()
    {
        var options = CommandLineParser.Parse(["dev", "--config", "custom.json", "--project", "App.csproj", "--no-watch", "--verbose"]);
        Assert(options.ManifestPath == "custom.json", "--config must be captured.");
        Assert(options.Project == "App.csproj", "--project must be captured.");
        Assert(options.NoWatch, "--no-watch must be captured.");
        Assert(options.Verbose, "--verbose must be captured.");
    }

    private static void ParsesInlineValues()
    {
        var options = CommandLineParser.Parse(["build", "--bundle=zip,msix", "--out=./artifacts"]);
        Assert(options.Bundles is ["zip", "msix"], "--bundle=zip,msix must split into two targets.");
        Assert(options.OutDir == "./artifacts", "--out=... must be captured.");
    }

    private static void UnknownOptionThrowsUsage()
    {
        Throws<CliUsageException>(() => CommandLineParser.Parse(["dev", "--nope"]), "Unknown options must throw CliUsageException.");
    }

    private static void UnknownCommandThrowsUsage()
    {
        Throws<CliUsageException>(() => CommandLineParser.Parse(["frobnicate"]), "Unknown commands must throw CliUsageException.");
    }

    private static void MissingValueThrowsUsage()
    {
        Throws<CliUsageException>(() => CommandLineParser.Parse(["dev", "--config"]), "Missing option values must throw CliUsageException.");
    }

    private static void UnexpectedPositionalThrowsUsage()
    {
        Throws<CliUsageException>(() => CommandLineParser.Parse(["dev", "extra"]), "Unexpected positional arguments must throw CliUsageException.");
    }

    private static void ManifestLoading()
    {
        ParsesValidManifest();
        IgnoresSchemaProperty();
        ThrowsOnInvalidJson();
        ThrowsOnEmptyManifest();
        ThrowsOnMissingFile();
    }

    private static void ParsesValidManifest()
    {
        var manifest = AppManifestLoader.Parse(
            """
            {
              "product": { "name": "my-app", "version": "0.1.0", "identifier": "com.example.app" },
              "build": { "frontend": "web", "devUrl": "http://127.0.0.1:5173", "frontendDist": "web/dist" },
              "bundle": { "targets": ["zip"] },
              "app": { "capabilities": ["main"] }
            }
            """);
        Assert(manifest.Product.Name == "my-app", "product.name must be parsed.");
        Assert(manifest.Product.Version == "0.1.0", "product.version must be parsed.");
        Assert(manifest.Build.Frontend == "web", "build.frontend must be parsed.");
        Assert(manifest.Build.DevUrl == "http://127.0.0.1:5173", "build.devUrl must be parsed.");
        Assert(manifest.Build.FrontendDist == "web/dist", "build.frontendDist must be parsed.");
        Assert(manifest.Bundle.Targets is ["zip"], "bundle.targets must be parsed.");
        Assert(manifest.App?.Capabilities is ["main"], "app.capabilities must be parsed.");
    }

    private static void IgnoresSchemaProperty()
    {
        var manifest = AppManifestLoader.Parse(
            """
            {
              "$schema": "https://tarui.dev/schemas/app.v1.json",
              "product": { "name": "a", "version": "0.1.0", "identifier": "a" },
              "build": { "frontendDist": "web/dist" },
              "bundle": { "targets": ["zip"] }
            }
            """);
        Assert(manifest.Product.Name == "a", "The $schema property must be ignored, not rejected.");
    }

    private static void ThrowsOnInvalidJson()
    {
        Throws<CliException>(() => AppManifestLoader.Parse("{ not json"), "Invalid JSON must throw CliException.");
    }

    private static void ThrowsOnEmptyManifest()
    {
        Throws<CliException>(() => AppManifestLoader.Parse(""), "An empty manifest must throw CliException.");
    }

    private static void ThrowsOnMissingFile()
    {
        using var directory = TempDirectory.Create();
        Throws<CliException>(
            () => AppManifestLoader.Load(Path.Combine(directory.Path, "missing.json")),
            "A missing manifest file must throw CliException.");
    }

    private static void ManifestValidation()
    {
        ReportsMissingRequiredFields();
        ReportsInvalidDevUrl();
        ReportsUnknownBundleTarget();
        ReportsMissingCapabilityFile();
        ValidManifestHasNoErrors();
    }

    private static void ReportsMissingRequiredFields()
    {
        var manifest = AppManifestLoader.Parse(
            """
            { "product": { "name": "", "version": "x", "identifier": "" }, "build": { "frontendDist": "" }, "bundle": { "targets": [] } }
            """);
        var errors = AppManifestValidator.Validate(manifest, Environment.CurrentDirectory);
        Assert(Has(errors, "product.name"), "Empty product.name must be reported.");
        Assert(Has(errors, "product.version"), "Invalid product.version must be reported.");
        Assert(Has(errors, "product.identifier"), "Empty product.identifier must be reported.");
        Assert(Has(errors, "frontendDist"), "Empty build.frontendDist must be reported.");
        Assert(Has(errors, "bundle.targets"), "Empty bundle.targets must be reported.");
    }

    private static void ReportsInvalidDevUrl()
    {
        var manifest = AppManifestLoader.Parse(
            """
            { "product": { "name": "a", "version": "0.1.0", "identifier": "a" }, "build": { "devUrl": "ftp://x", "frontendDist": "d" }, "bundle": { "targets": ["zip"] } }
            """);
        var errors = AppManifestValidator.Validate(manifest, Environment.CurrentDirectory);
        Assert(Has(errors, "devUrl"), "A non-http(s) devUrl must be reported.");
    }

    private static void ReportsUnknownBundleTarget()
    {
        var manifest = AppManifestLoader.Parse(
            """
            { "product": { "name": "a", "version": "0.1.0", "identifier": "a" }, "build": { "frontendDist": "d" }, "bundle": { "targets": ["dmg"] } }
            """);
        var errors = AppManifestValidator.Validate(manifest, Environment.CurrentDirectory);
        Assert(Has(errors, "dmg"), "An unsupported bundle target must be reported.");
    }

    private static void ReportsMissingCapabilityFile()
    {
        using var directory = TempDirectory.Create();
        var manifest = AppManifestLoader.Parse(
            """
            { "product": { "name": "a", "version": "0.1.0", "identifier": "a" }, "build": { "frontendDist": "d" }, "bundle": { "targets": ["zip"] }, "app": { "capabilities": ["main"] } }
            """);
        var errors = AppManifestValidator.Validate(manifest, directory.Path);
        Assert(Has(errors, "capabilities/main.json"), "A referenced capability file that is missing must be reported.");
    }

    private static void ValidManifestHasNoErrors()
    {
        using var directory = TempDirectory.Create();
        Directory.CreateDirectory(Path.Combine(directory.Path, "capabilities"));
        File.WriteAllText(Path.Combine(directory.Path, "capabilities", "main.json"), "{}");
        var manifest = AppManifestLoader.Parse(
            """
            { "product": { "name": "a", "version": "0.1.0", "identifier": "a" }, "build": { "devUrl": "http://127.0.0.1:5173", "frontendDist": "d" }, "bundle": { "targets": ["zip"] }, "app": { "capabilities": ["main"] } }
            """);
        var errors = AppManifestValidator.Validate(manifest, directory.Path);
        Assert(errors.Count == 0, $"A valid manifest must have no errors, but got: {string.Join("; ", errors)}");
    }

    private static void PathResolution()
    {
        ResolvesRelativeAgainstManifestDirectory();
        DefaultsToTaruiAppJsonInCurrentDirectory();
        FrontendWorkingDirectoryUsesFrontendRoot();
        DevUrlResolution();
        DesktopProjectResolution();
    }

    private static void ResolvesRelativeAgainstManifestDirectory()
    {
        var paths = new CliPaths("C:\\app\\tarui.app.json", "C:\\app");
        Assert(paths.ResolveRelative("web/dist") == Path.GetFullPath("C:\\app\\web\\dist"), "Relative paths must resolve against the manifest directory.");
        Assert(paths.ResolveRelative("C:\\absolute\\x") == Path.GetFullPath("C:\\absolute\\x"), "Absolute paths must pass through unchanged.");
    }

    private static void DefaultsToTaruiAppJsonInCurrentDirectory()
    {
        var paths = CliPaths.Resolve(null);
        Assert(
            string.Equals(paths.ManifestPath, Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "tarui.app.json")), StringComparison.OrdinalIgnoreCase),
            "The default manifest path must be ./tarui.app.json in the current directory.");
    }

    private static void FrontendWorkingDirectoryUsesFrontendRoot()
    {
        var paths = new CliPaths("C:\\app\\tarui.app.json", "C:\\app");
        var build = new AppManifestBuild("web", null, null, null, "web/dist", null);
        Assert(
            paths.FrontendWorkingDirectory(build) == Path.GetFullPath("C:\\app\\web"),
            "The frontend working directory must join the manifest directory with build.frontend.");
        var noFrontend = new AppManifestBuild(null, null, null, null, "web/dist", null);
        Assert(
            paths.FrontendWorkingDirectory(noFrontend) == Path.GetFullPath("C:\\app"),
            "Without build.frontend the manifest directory must be used.");
    }

    private static void DevUrlResolution()
    {
        var manifest = AppManifestLoader.Parse(
            """
            { "product": { "name": "a", "version": "0.1.0", "identifier": "a" }, "build": { "devUrl": "http://127.0.0.1:5173", "frontendDist": "d" }, "bundle": { "targets": ["zip"] } }
            """);
        Assert(ManifestLoader.ResolveDevUrl(manifest).ToString() == "http://127.0.0.1:5173/", "A valid devUrl must be resolved to a Uri.");

        var withoutDevUrl = AppManifestLoader.Parse(
            """
            { "product": { "name": "a", "version": "0.1.0", "identifier": "a" }, "build": { "frontendDist": "d" }, "bundle": { "targets": ["zip"] } }
            """);
        Throws<CliException>(() => ManifestLoader.ResolveDevUrl(withoutDevUrl), "A missing devUrl must throw CliException for dev.");
    }

    private static void DesktopProjectResolution()
    {
        using var directory = TempDirectory.Create();
        File.WriteAllText(Path.Combine(directory.Path, "App.csproj"), "<Project />");
        var paths = new CliPaths(Path.Combine(directory.Path, "tarui.app.json"), directory.Path);
        var manifest = AppManifestLoader.Parse(
            """
            { "product": { "name": "a", "version": "0.1.0", "identifier": "a" }, "build": { "frontendDist": "d" }, "bundle": { "targets": ["zip"] } }
            """);

        var resolved = ManifestLoader.ResolveDesktopProject(manifest, "App.csproj", paths);
        Assert(
            string.Equals(resolved, Path.Combine(directory.Path, "App.csproj"), StringComparison.OrdinalIgnoreCase),
            "A --project override that exists must resolve to its absolute path.");

        Throws<CliException>(
            () => ManifestLoader.ResolveDesktopProject(manifest, null, paths),
            "A missing manifest desktopProject must throw CliException.");
        Throws<CliException>(
            () => ManifestLoader.ResolveDesktopProject(manifest, "Missing.csproj", paths),
            "A project path that does not exist must throw CliException.");
    }

    private static void Tooling()
    {
        RuntimeIdentifierIsNonEmpty();
        ShellCommandSelectsPlatformShell();
    }

    private static void RuntimeIdentifierIsNonEmpty()
    {
        var rid = RuntimeIdentifier.ForCurrentPlatform();
        Assert(!string.IsNullOrWhiteSpace(rid), "The default runtime identifier must never be empty.");
        Assert(rid.Contains('-'), $"The default runtime identifier must be RID-shaped, got '{rid}'.");
    }

    private static void ShellCommandSelectsPlatformShell()
    {
        var (fileName, arguments) = ShellCommand.For("pnpm dev");
        if (OperatingSystem.IsWindows())
        {
            Assert(fileName == "cmd.exe", "On Windows the shell must be cmd.exe.");
            Assert(arguments is ["/d", "/s", "/c", "pnpm dev"], "On Windows the command must be passed through cmd /d /s /c.");
        }
        else
        {
            Assert(fileName == "/bin/sh", "On non-Windows the shell must be /bin/sh.");
            Assert(arguments is ["-c", "pnpm dev"], "On non-Windows the command must be passed through sh -c.");
        }
    }

    private static void LatestManifest()
    {
        SerializesCamelCase();
        SignatureRoundTrips();
    }

    private static void SerializesCamelCase()
    {
        var latest = new LatestManifestDto
        {
            Version = "0.1.0",
            Url = "app-0.1.0-win-x64.zip",
            Sha256 = "abc",
            Signature = ""
        };
        var json = System.Text.Json.JsonSerializer.Serialize(latest, TaruiCliJsonContext.Default.LatestManifestDto);
        Assert(json.Contains("\"version\"") && json.Contains("\"url\"") && json.Contains("\"sha256\"") && json.Contains("\"signature\""),
            "latest.json must serialize with camelCase property names.");
    }

    private static void SignatureRoundTrips()
    {
        var json = """{"version":"0.1.0","url":"app.zip","sha256":"abc","signature":"sig"}""";
        var latest = System.Text.Json.JsonSerializer.Deserialize(json, TaruiCliJsonContext.Default.LatestManifestDto);
        Assert(latest?.Signature == "sig", "The signature placeholder must survive a round trip.");
    }

    private static void Init()
    {
        ParsesInitCommand();
        InitRejectsMultipleNames();
        NameNormalization();
        LocalReferenceRewrite();
    }

    private static void ParsesInitCommand()
    {
        var options = CommandLineParser.Parse(["init", "my-app", "--template", "react-ts", "--manager", "pnpm", "--output", "./out", "--local", "/repo"]);
        Assert(options.Command == TaruiCommand.Init, "Parse(['init', ...]) must select the Init command.");
        Assert(options.Name == "my-app", "init <name> must be captured as the application name.");
        Assert(options.Template == "react-ts", "--template must be captured.");
        Assert(options.Manager == "pnpm", "--manager must be captured.");
        Assert(options.Output == "./out", "--output must be captured.");
        Assert(options.Local == "/repo", "--local must be captured.");

        var noName = CommandLineParser.Parse(["init"]);
        Assert(noName.Command == TaruiCommand.Init, "A bare 'init' must still select the Init command.");
        Assert(noName.Name is null, "A bare 'init' must carry no application name.");
    }

    private static void InitRejectsMultipleNames()
    {
        Throws<CliUsageException>(
            () => CommandLineParser.Parse(["init", "a", "b"]),
            "tarui init must reject more than one application name.");
    }

    private static void NameNormalization()
    {
        Assert(ProjectName.ToIdentifier("my-app", "App") == "MyApp", "kebab-case must become PascalCase for C#.");
        Assert(ProjectName.ToIdentifier("tmp-app", "App") == "TmpApp", "tmp-app must become TmpApp.");
        Assert(ProjectName.ToIdentifier("!bad!name", "App") == "BadName", "Invalid characters must be stripped.");
        Assert(ProjectName.ToIdentifier("", "Fallback") == "Fallback", "An empty name must fall back.");
        Assert(ProjectName.ToIdentifierName("my-app") == "dev.myapp", "An app name must derive dev.<lowercased-alnum>.");
    }

    private static void LocalReferenceRewrite()
    {
        const string csproj =
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Tarui.Hosting" Version="0.1.0" />
                <PackageReference Include="NotInRepo" Version="0.1.0" />
                <PackageReference Include="Tarui.Plugins.Window" Version="0.1.0" />
              </ItemGroup>
            </Project>
            """;
        var rewritten = LocalReferenceRewriter.RewriteContent(csproj, "C:/local/repo");
        Assert(!rewritten.Contains("PackageReference Include=\"Tarui.Hosting\"", StringComparison.Ordinal),
            "In-repo Tarui package references must be replaced.");
        Assert(rewritten.Contains("<ProjectReference Include=\"C:/local/repo/src/desktop/Tarui.Hosting/Tarui.Hosting.csproj\" />", StringComparison.Ordinal),
            "Tarui.Hosting must resolve to its local project path.");
        Assert(rewritten.Contains("<ProjectReference Include=\"C:/local/repo/src/plugins/Tarui.Plugins.Window/Tarui.Plugins.Window.csproj\" />", StringComparison.Ordinal),
            "Tarui.Plugins.Window must resolve to its local project path.");
        Assert(rewritten.Contains("PackageReference Include=\"NotInRepo\"", StringComparison.Ordinal),
            "Third-party package references must be left untouched.");
        Assert(rewritten.Contains("<TaruiCefRuntimeRoot>C:/local/repo/runtime/cef</TaruiCefRuntimeRoot>", StringComparison.Ordinal),
            "The CEF runtime root must be pointed at the local source tree.");
        Assert(rewritten.Contains("<TaruiWebDistRoot>C:/local/repo/web/apps/Tarui.Web/dist</TaruiWebDistRoot>", StringComparison.Ordinal),
            "The web dist root must be pointed at the local source tree.");
    }

    private static void Plugin()
    {
        ParsesPluginInitCommand();
        ParsesPluginPackCommand();
        PluginRejectsUnknownSubCommand();
        PluginScaffoldsSkeleton();
        PluginNameNormalization();
    }

    private static void ParsesPluginInitCommand()
    {
        var options = CommandLineParser.Parse(["plugin", "init", "store", "--local", "/repo"]);
        Assert(options.Command == TaruiCommand.Plugin, "Parse(['plugin', ...]) must select the Plugin command.");
        Assert(options.PluginAction == PluginAction.Init, "plugin init must select the Init action.");
        Assert(options.PluginName == "store", "plugin init <name> must capture the plugin name.");
        Assert(options.Local == "/repo", "--local must be captured for plugin init.");

        var noName = CommandLineParser.Parse(["plugin", "init"]);
        Assert(noName.PluginName is null, "A bare 'plugin init' must carry no name.");
    }

    private static void ParsesPluginPackCommand()
    {
        var options = CommandLineParser.Parse(["plugin", "pack"]);
        Assert(options.Command == TaruiCommand.Plugin, "Parse(['plugin', 'pack']) must select the Plugin command.");
        Assert(options.PluginAction == PluginAction.Pack, "plugin pack must select the Pack action.");
    }

    private static void PluginRejectsUnknownSubCommand()
    {
        Throws<CliUsageException>(
            () => CommandLineParser.Parse(["plugin", "frobnicate"]),
            "Unknown plugin sub-commands must throw CliUsageException.");
        Throws<CliUsageException>(
            () => CommandLineParser.Parse(["plugin"]),
            "A bare 'plugin' must throw CliUsageException.");
        Throws<CliUsageException>(
            () => CommandLineParser.Parse(["plugin", "init", "a", "b"]),
            "'plugin init' must reject more than one plugin name.");
        Throws<CliUsageException>(
            () => CommandLineParser.Parse(["plugin", "pack", "extra"]),
            "'plugin pack' must reject positional arguments.");
    }

    private static void PluginScaffoldsSkeleton()
    {
        using var temp = TempDirectory.Create();
        var root = PluginScaffolder.Scaffold("store", temp.Path, localRepo: null);
        Assert(File.Exists(Path.Combine(root, "src", "Tarui.Plugins.Store", "Tarui.Plugins.Store.csproj")),
            "The scaffolder must emit the plugin .csproj.");
        Assert(File.Exists(Path.Combine(root, "src", "Tarui.Plugins.Store", "Plugin.cs")),
            "The scaffolder must emit Plugin.cs.");
        Assert(File.Exists(Path.Combine(root, "src", "Tarui.Plugins.Store", "Contracts.cs")),
            "The scaffolder must emit Contracts.cs.");
        Assert(File.Exists(Path.Combine(root, "permissions", "schema.json")),
            "The scaffolder must emit the permissions schema.");
        Assert(File.Exists(Path.Combine(root, "guest-js", "package.json")),
            "The scaffolder must emit the guest-js package.");
        Assert(File.Exists(Path.Combine(root, "tests", "Tarui.Plugins.Store.Tests", "Program.cs")),
            "The scaffolder must emit the test project.");
        Assert(File.Exists(Path.Combine(root, "README.md")),
            "The scaffolder must emit a README.");
    }

    private static void PluginNameNormalization()
    {
        Assert(PluginScaffolder.NormalizePluginName("My-Store") == "my-store", "Plugin names must be lower-cased.");
        Assert(PluginScaffolder.NormalizePluginName("Foo!Bar") == "foobar", "Invalid characters must be stripped.");
        Assert(PluginScaffolder.NormalizePluginName("") == string.Empty, "An empty name normalizes to empty.");
    }

    private static bool Has(IEnumerable<string> errors, string fragment) =>
        errors.Any(error => error.Contains(fragment, StringComparison.Ordinal));

    private static void Throws<TException>(Action action, string message)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path) => Path = path;

        public string Path { get; }

        public static TempDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tarui-cli-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup.
            }
        }
    }
}
