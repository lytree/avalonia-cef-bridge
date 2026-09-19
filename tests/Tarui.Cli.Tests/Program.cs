using System.IO.Compression;
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
            PluginPack();
            SchemaSynthesis();
            Msix();
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
        // 用平台原生的 rooted 临时目录构造路径，避免断言依赖 Windows 盘符写法。
        var manifestDir = Path.Combine(Path.GetTempPath(), "tarui-cli-tests", "app");
        var paths = new CliPaths(Path.Combine(manifestDir, "tarui.app.json"), manifestDir);
        Assert(
            paths.ResolveRelative("web/dist") == Path.GetFullPath(Path.Combine(manifestDir, "web", "dist")),
            "Relative paths must resolve against the manifest directory.");
        var absolute = Path.Combine(Path.GetTempPath(), "tarui-cli-absolute", "x");
        Assert(paths.ResolveRelative(absolute) == Path.GetFullPath(absolute), "Absolute paths must pass through unchanged.");
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
        var manifestDir = Path.Combine(Path.GetTempPath(), "tarui-cli-tests", "app");
        var paths = new CliPaths(Path.Combine(manifestDir, "tarui.app.json"), manifestDir);
        var build = new AppManifestBuild("web", null, null, null, "web/dist", null);
        Assert(
            paths.FrontendWorkingDirectory(build) == Path.GetFullPath(Path.Combine(manifestDir, "web")),
            "The frontend working directory must join the manifest directory with build.frontend.");
        var noFrontend = new AppManifestBuild(null, null, null, null, "web/dist", null);
        Assert(
            paths.FrontendWorkingDirectory(noFrontend) == Path.GetFullPath(manifestDir),
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
        SerializesRuntimeSchema();
        SignatureRoundTrips();
        CanonicalizationMatchesRuntimeFormat();
        CliProducedManifestIsVerifiedByRuntimeAlgorithm();
        ParserAcceptsSignKey();
    }

    private static void SerializesRuntimeSchema()
    {
        var latest = new LatestManifestDto
        {
            SchemaVersion = 1,
            Version = "0.1.0",
            Files = ["app-0.1.0-win-x64.zip"],
            Sha256 = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["app-0.1.0-win-x64.zip"] = "abc",
            },
            Signature = ""
        };
        var json = System.Text.Json.JsonSerializer.Serialize(latest, TaruiCliJsonContext.Default.LatestManifestDto);
        Assert(json.Contains("\"schemaVersion\""), "latest.json must emit schemaVersion.");
        Assert(json.Contains("\"version\""), "latest.json must emit version.");
        Assert(json.Contains("\"files\""), "latest.json must emit files array.");
        Assert(json.Contains("\"sha256\""), "latest.json must emit sha256 table.");
        Assert(json.Contains("\"signature\""), "latest.json must emit signature field.");
        Assert(!json.Contains("\"url\""), "latest.json must no longer emit the legacy url field.");
    }

    private static void SignatureRoundTrips()
    {
        var json = """{"schemaVersion":1,"version":"0.1.0","files":["app.zip"],"sha256":{"app.zip":"abc"},"signature":"sig"}""";
        var latest = System.Text.Json.JsonSerializer.Deserialize(json, TaruiCliJsonContext.Default.LatestManifestDto);
        Assert(latest is not null, "latest.json must deserialize back into a DTO.");
        Assert(latest!.Signature == "sig", "The signature field must survive a round trip.");
        Assert(latest.Version == "0.1.0", "The version field must survive a round trip.");
        Assert(latest.SchemaVersion == 1, "The schemaVersion field must survive a round trip.");
    }

    private static void CanonicalizationMatchesRuntimeFormat()
    {
        var manifest = new LatestManifestDto
        {
            SchemaVersion = 1,
            Version = "1.2.3",
            Files = ["b.zip", "a.zip"],
            Sha256 = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["a.zip"] = "h2",
                ["b.zip"] = "h1",
            },
        };
        var canonical = BuildCommand.CanonicalizeForSigning(manifest);
        var expected = string.Concat(
            "1\n",
            "1.2.3\n",
            "b.zip\n",
            "a.zip\n",
            "a.zip=h2\n",
            "b.zip=h1\n");
        var actual = System.Text.Encoding.UTF8.GetString(canonical);
        Assert(actual == expected,
            $"CLI canonicalization must match the runtime verifier byte-for-byte. Expected:\n'{expected}'\nActual:\n'{actual}'");
    }

    private static void CliProducedManifestIsVerifiedByRuntimeAlgorithm()
    {
        using var key = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP384);
        var files = new[] { "my-app-0.1.0-win-x64.zip" };
        var sha = new Dictionary<string, string>(StringComparer.Ordinal) { [files[0]] = "deadbeef" };
        var unsigned = new LatestManifestDto
        {
            SchemaVersion = 1,
            Version = "0.1.0",
            Files = files,
            Sha256 = sha,
            Signature = string.Empty,
        };
        var canonical = BuildCommand.CanonicalizeForSigning(unsigned);
        var signature = key.SignData(canonical, System.Security.Cryptography.HashAlgorithmName.SHA384);
        var signed = unsigned with { Signature = Convert.ToBase64String(signature) };

        var json = System.Text.Json.JsonSerializer.Serialize(signed, TaruiCliJsonContext.Default.LatestManifestDto);
        var roundTripped = System.Text.Json.JsonSerializer.Deserialize(json, TaruiCliJsonContext.Default.LatestManifestDto)!;
        var roundTrippedCanonical = BuildCommand.CanonicalizeForSigning(roundTripped);
        var verified = key.VerifyData(
            roundTrippedCanonical,
            Convert.FromBase64String(roundTripped.Signature),
            System.Security.Cryptography.HashAlgorithmName.SHA384);
        Assert(verified,
            "A CLI-signed manifest must be verifiable with the same ECDSA-P384/SHA-384 algorithm the runtime uses.");
    }

    private static void ParserAcceptsSignKey()
    {
        var inline = CommandLineParser.Parse(["build", "--sign-key=Zm9v"]);
        Assert(inline.SignKey == "Zm9v", "--sign-key=value must be captured.");
        var spaced = CommandLineParser.Parse(["build", "--sign-key", "YmFy"]);
        Assert(spaced.SignKey == "YmFy", "--sign-key value must be captured.");
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
                <PackageReference Include="Tarui.WebView.CefGlueNext" Version="0.1.0" />
                <PackageReference Include="Tarui.WebView.Avalonia" Version="0.1.0" />
                <PackageReference Include="NotInRepo" Version="0.1.0" />
                <PackageReference Include="Tarui.Plugins.Window" Version="0.1.0" />
              </ItemGroup>
            </Project>
            """;
        // 仓库根 fixture 必须是平台原生的绝对路径：RewriteContent 会对 root 做 GetFullPath
        // 归一化（Windows 盘符写法在 Linux 上会被当成相对路径拼到 cwd）。
        var repoRoot = Path.Combine(Path.GetTempPath(), "tarui-cli-local-repo");
        var root = repoRoot.Replace('\\', '/').TrimEnd('/');
        var rewritten = LocalReferenceRewriter.RewriteContent(csproj, repoRoot);
        Assert(!rewritten.Contains("PackageReference Include=\"Tarui.Hosting\"", StringComparison.Ordinal),
            "In-repo Tarui package references must be replaced.");
        Assert(rewritten.Contains($"<ProjectReference Include=\"{root}/src/desktop/Tarui.Hosting/Tarui.Hosting.csproj\" />", StringComparison.Ordinal),
            "Tarui.Hosting must resolve to its local project path.");
        Assert(rewritten.Contains($"<ProjectReference Include=\"{root}/src/webview/Tarui.WebView.CefGlueNext/Tarui.WebView.CefGlueNext.csproj\" />", StringComparison.Ordinal),
            "Tarui.WebView.CefGlueNext must resolve to its local project path.");
        Assert(rewritten.Contains($"<ProjectReference Include=\"{root}/src/webview/Tarui.WebView.Avalonia/Tarui.WebView.Avalonia.csproj\" />", StringComparison.Ordinal),
            "Tarui.WebView.Avalonia must resolve to its local project path.");
        Assert(rewritten.Contains($"<ProjectReference Include=\"{root}/src/plugins/Tarui.Plugins.Window/Tarui.Plugins.Window.csproj\" />", StringComparison.Ordinal),
            "Tarui.Plugins.Window must resolve to its local project path.");
        Assert(rewritten.Contains("PackageReference Include=\"NotInRepo\"", StringComparison.Ordinal),
            "Third-party package references must be left untouched.");
        Assert(rewritten.Contains($"<TaruiCefRuntimeRoot>{root}/runtime/cef</TaruiCefRuntimeRoot>", StringComparison.Ordinal),
            "The CEF runtime root must be pointed at the local source tree.");
        Assert(rewritten.Contains($"<TaruiWebDistRoot>{root}/web/apps/Tarui.Web/dist</TaruiWebDistRoot>", StringComparison.Ordinal),
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
        Assert(File.Exists(Path.Combine(root, "permissions", "default.json")),
            "The scaffolder must emit the default permission set.");
        Assert(File.Exists(Path.Combine(root, "guest-js", "package.json")),
            "The scaffolder must emit the guest-js package.");
        Assert(File.Exists(Path.Combine(root, "guest-js", "tsconfig.json")),
            "The scaffolder must emit a buildable guest-js tsconfig.");
        Assert(File.Exists(Path.Combine(root, "guest-js", "src", "index.ts")),
            "The scaffolder must emit a guest-js entry point.");
        Assert(File.Exists(Path.Combine(root, "tests", "Tarui.Plugins.Store.Tests", "Program.cs")),
            "The scaffolder must emit the test project.");
        Assert(File.Exists(Path.Combine(root, "tests", "Tarui.Plugins.Store.Tests", "Tarui.Plugins.Store.Tests.csproj")),
            "The scaffolder must emit a buildable test project.");
        Assert(File.Exists(Path.Combine(root, "README.md")),
            "The scaffolder must emit a README.");

        var pluginCs = File.ReadAllText(Path.Combine(root, "src", "Tarui.Plugins.Store", "Plugin.cs"));
        Assert(pluginCs.Contains("class StorePlugin", StringComparison.Ordinal) &&
               pluginCs.Contains("AddStorePlugin", StringComparison.Ordinal),
            "The scaffolder must derive plugin class names from the plugin name.");
        Assert(!pluginCs.Contains("FooPlugin", StringComparison.Ordinal),
            "The scaffolder must not retain placeholder class names.");
    }

    private static void PluginNameNormalization()
    {
        Assert(PluginScaffolder.NormalizePluginName("My-Store") == "my-store", "Plugin names must be lower-cased.");
        Assert(PluginScaffolder.NormalizePluginName("Foo!Bar") == "foobar", "Invalid characters must be stripped.");
        Assert(PluginScaffolder.NormalizePluginName("") == string.Empty, "An empty name normalizes to empty.");
    }

    private static void PluginPack()
    {
        DetectScaffoldLayout();
        ValidDefaultsPass();
        UnknownDefaultReferenceIsReported();
        VersionMismatchIsReported();
        DuplicateSchemaIdentifierIsReported();
        MalformedSchemaThrows();
    }

    private static void DetectScaffoldLayout()
    {
        using var temp = TempDirectory.Create();
        var root = PluginScaffolder.Scaffold("store", temp.Path, localRepo: null);
        var layout = PluginPacker.Detect(root);
        Assert(layout.PackageId == "Tarui.Plugins.Store", "The scaffolded package id must be detected.");
        Assert(layout.Version == "0.1.0", $"The scaffolded version must be detected, got '{layout.Version}'.");
        Assert(PluginPacker.HasPermissionsContent(layout), "The scaffolded permissions directory must be non-empty.");
    }

    private static void ValidDefaultsPass()
    {
        using var temp = TempDirectory.Create();
        var root = PluginScaffolder.Scaffold("store", temp.Path, localRepo: null);
        var errors = PluginPacker.ValidatePermissions(
            Path.Combine(root, "permissions", "schema.json"),
            Path.Combine(root, "permissions", "default.json"));
        Assert(errors.Count == 0, $"The scaffolded permission set must be valid, got: {string.Join("; ", errors)}");
        Assert(PluginPacker.CheckVersionConsistency(PluginPacker.Detect(root)) is null,
            "Scaffolded backend and guest-js versions must agree.");
    }

    private static void UnknownDefaultReferenceIsReported()
    {
        using var temp = TempDirectory.Create();
        var root = PluginScaffolder.Scaffold("store", temp.Path, localRepo: null);
        File.WriteAllText(
            Path.Combine(root, "permissions", "default.json"),
            """{ "plugin": "store", "permissions": ["plugin:store|missing"] }""");
        var errors = PluginPacker.ValidatePermissions(
            Path.Combine(root, "permissions", "schema.json"),
            Path.Combine(root, "permissions", "default.json"));
        Assert(errors.Any(error => error.Contains("plugin:store|missing", StringComparison.Ordinal)),
            "A default reference to an undeclared identifier must be reported.");
    }

    private static void VersionMismatchIsReported()
    {
        using var temp = TempDirectory.Create();
        var root = PluginScaffolder.Scaffold("store", temp.Path, localRepo: null);
        File.WriteAllText(
            Path.Combine(root, "guest-js", "package.json"),
            """{ "name": "@lytree/plugin-store", "version": "0.2.0" }""");
        var problem = PluginPacker.CheckVersionConsistency(PluginPacker.Detect(root));
        Assert(problem is not null && problem.Contains("0.2.0", StringComparison.Ordinal),
            "A guest-js version that diverges from the backend must be reported.");
    }

    private static void DuplicateSchemaIdentifierIsReported()
    {
        using var temp = TempDirectory.Create();
        var root = PluginScaffolder.Scaffold("store", temp.Path, localRepo: null);
        File.WriteAllText(
            Path.Combine(root, "permissions", "schema.json"),
            """
            {
              "plugin": "store",
              "version": "0.1.0",
              "permissions": [
                { "identifier": "plugin:store|ping", "scope": null },
                { "identifier": "plugin:store|ping", "scope": null }
              ]
            }
            """);
        var errors = PluginPacker.ValidatePermissions(
            Path.Combine(root, "permissions", "schema.json"),
            Path.Combine(root, "permissions", "default.json"));
        Assert(errors.Any(error => error.Contains("Duplicate", StringComparison.Ordinal)),
            "Duplicate schema identifiers must be reported.");
    }

    private static void MalformedSchemaThrows()
    {
        using var temp = TempDirectory.Create();
        var root = PluginScaffolder.Scaffold("store", temp.Path, localRepo: null);
        File.WriteAllText(Path.Combine(root, "permissions", "schema.json"), "{ not json");
        Throws<CliException>(
            () => PluginPacker.ValidatePermissions(
                Path.Combine(root, "permissions", "schema.json"),
                Path.Combine(root, "permissions", "default.json")),
            "A malformed schema must be reported as a CliException.");
    }

    private static void SchemaSynthesis()
    {
        MergesPluginSchemas();
        WritesMergedSchemaToOutput();
        RejectsDuplicateIdentifiers();
        RejectsMalformedSchema();
        ScaffoldedCsprojNamespacesPermissions();
    }

    private static void MergesPluginSchemas()
    {
        using var temp = TempDirectory.Create();
        var binDir = Path.Combine(temp.Path, "bin");
        WritePluginSchema(binDir, "foo", "plugin:foo|ping");
        WritePluginSchema(binDir, "bar", "plugin:bar|ping");

        var schema = SchemaSynthesizer.Synthesize(binDir);
        var plugins = schema.Plugins;
        Assert(plugins is { Count: 2 }, "Both plugin schemas must be collected.");
        Assert(plugins![0].Plugin == "bar" && plugins[1].Plugin == "foo",
            "Plugins must be merged in deterministic name order.");
        Assert(plugins[0].Permissions?[0].Identifier == "plugin:bar|ping",
            "Each plugin's permission identifier must be preserved.");
    }

    private static void WritesMergedSchemaToOutput()
    {
        using var temp = TempDirectory.Create();
        var binDir = Path.Combine(temp.Path, "bin");
        WritePluginSchema(binDir, "foo", "plugin:foo|ping");

        var schema = SchemaSynthesizer.Synthesize(binDir);
        var output = SchemaSynthesizer.Write(binDir, schema);
        Assert(File.Exists(output), "The synthesized schema must be written to the publish output.");
        Assert(Path.GetFileName(output) == "permissions.schema.json",
            "The synthesized schema output must be named permissions.schema.json.");

        var roundTripped = System.Text.Json.JsonSerializer.Deserialize(
            File.ReadAllText(output),
            TaruiCliJsonContext.Default.SynthesizedPermissionSchemaDto);
        Assert(roundTripped?.Plugins is { Count: 1 } && roundTripped.Plugins[0].Plugin == "foo",
            "The written schema must survive a round trip.");
    }

    private static void RejectsDuplicateIdentifiers()
    {
        using var temp = TempDirectory.Create();
        var binDir = Path.Combine(temp.Path, "bin");
        WritePluginSchema(binDir, "foo", "plugin:shared|ping");
        WritePluginSchema(binDir, "bar", "plugin:shared|ping");

        Throws<CliException>(() => SchemaSynthesizer.Synthesize(binDir),
            "Duplicate permission identifiers across plugins must be rejected during synthesis.");
    }

    private static void RejectsMalformedSchema()
    {
        using var temp = TempDirectory.Create();
        var binDir = Path.Combine(temp.Path, "bin");
        Directory.CreateDirectory(Path.Combine(binDir, "permissions", "foo"));
        File.WriteAllText(Path.Combine(binDir, "permissions", "foo", "schema.json"), "{ not json");

        Throws<CliException>(() => SchemaSynthesizer.Synthesize(binDir),
            "A malformed plugin schema must be reported as a CliException.");
    }

    private static void ScaffoldedCsprojNamespacesPermissions()
    {
        using var temp = TempDirectory.Create();
        var root = PluginScaffolder.Scaffold("store", temp.Path, localRepo: null);
        var csproj = File.ReadAllText(Path.Combine(root, "src", "Tarui.Plugins.Store", "Tarui.Plugins.Store.csproj"));
        Assert(csproj.Contains(@"Link=""permissions\store\%(Filename)%(Extension)""", StringComparison.Ordinal),
            "Scaffolded plugin permissions must be namespaced under the normalized plugin name.");
        Assert(!csproj.Contains("{{normalized}}", StringComparison.Ordinal),
            "The csproj must not retain the raw {{normalized}} anchor.");
    }

    private static void WritePluginSchema(string binDir, string plugin, string identifier)
    {
        var directory = Path.Combine(binDir, "permissions", plugin);
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, "schema.json"),
            $$"""
            {
              "plugin": "{{plugin}}",
              "version": "0.1.0",
              "permissions": [
                { "identifier": "{{identifier}}", "description": "test", "scope": null }
              ],
              "events": [],
              "default": []
            }
            """);
    }

    private static void Msix()
    {
        ParsesMsixManifest();
        MsixManifestReportsTargetMismatch();
        AppxManifestHasExpectedFields();
        BlockMapMatchesPackageContents();
        EndToEndPacksValidPackage();
        InfoPlistRendersAllRequiredKeys();
        InfoPlistEmitsUrlTypesForEveryScheme();
        InfoPlistDeduplicatesRepeatedScheme();
        InfoPlistRejectsInvalidSchemeToken();
        InfoPlistRejectsBadBundleId();
        InfoPlistUsesRidDefaultForMinimumSystemVersion();
        MacOsBundleValidatesRid();
        MacOsBundleProducesExpectedLayout();
        MacOsBundleEmitsTarGzArchive();
        AppBundleTargetAcceptedByValidator();
        MacOsSchemesWithoutTargetIsReported();
    }

    private static void ParsesMsixManifest()
    {
        var manifest = AppManifestLoader.Parse(
            """
            {
              "product": { "name": "my-app", "version": "0.1.0", "identifier": "com.example.app" },
              "build": { "frontendDist": "web/dist" },
              "bundle": {
                "targets": ["msix"],
                "msix": {
                  "publisher": "CN=Contoso",
                  "certificate": { "path": "cert.pfx", "password": "p", "timeStamperUrl": "http://ts.example" }
                }
              }
            }
            """);
        Assert(manifest.Bundle.Msix?.Publisher == "CN=Contoso", "bundle.msix.publisher must be parsed.");
        Assert(manifest.Bundle.Msix?.CertificatePath == "cert.pfx", "bundle.msix.certificate.path must be parsed.");
        Assert(manifest.Bundle.Msix?.CertificatePassword == "p", "bundle.msix.certificate.password must be parsed.");
        Assert(manifest.Bundle.Msix?.TimeStamperUrl == "http://ts.example", "bundle.msix.certificate.timeStamperUrl must be parsed.");
    }

    private static void MsixManifestReportsTargetMismatch()
    {
        using var directory = TempDirectory.Create();
        var manifest = AppManifestLoader.Parse(
            """
            {
              "product": { "name": "a", "version": "0.1.0", "identifier": "a" },
              "build": { "frontendDist": "d" },
              "bundle": { "targets": ["zip"], "msix": { "publisher": "CN=X" } }
            }
            """);
        var errors = AppManifestValidator.Validate(manifest, directory.Path);
        Assert(Has(errors, "'msix'"), "bundle.msix without an 'msix' target must be reported.");
    }

    private static void AppxManifestHasExpectedFields()
    {
        var manifest = AppManifestLoader.Parse(
            """
            {
              "product": { "name": "my-app", "version": "0.1.0", "identifier": "com.example.app" },
              "build": { "frontendDist": "web/dist" },
              "bundle": { "targets": ["msix"], "shortDescription": "A test app", "msix": { "publisher": "CN=Contoso" } }
            }
            """);
        var xml = MsixPacker.BuildAppxManifest(manifest, "MyApp.exe", "win-x64");
        Assert(xml.Contains($"Name=\"com.example.app\"", StringComparison.Ordinal), "The appx identity must carry the identifier.");
        Assert(xml.Contains($"Publisher=\"CN=Contoso\"", StringComparison.Ordinal), "The appx identity must carry the publisher.");
        Assert(xml.Contains("Version=\"0.1.0.0\"", StringComparison.Ordinal), "The version must be normalized to four parts.");
        Assert(xml.Contains("ProcessorArchitecture=\"x64\"", StringComparison.Ordinal), "The win-x64 rid must map to x64.");
        Assert(xml.Contains("Executable=\"MyApp.exe\"", StringComparison.Ordinal), "The entry executable must be registered.");
        Assert(xml.Contains("DisplayName>my-app", StringComparison.Ordinal), "The display name must be emitted.");
        Assert(xml.Contains("Description>A test app", StringComparison.Ordinal), "The short description must be emitted.");
        Assert(xml.Contains("runFullTrust", StringComparison.Ordinal), "A full-trust desktop app must declare runFullTrust.");
    }

    private static void BlockMapMatchesPackageContents()
    {
        using var directory = TempDirectory.Create();
        var binDir = Path.Combine(directory.Path, "bin");
        Directory.CreateDirectory(binDir);
        WriteSamplePayload(binDir);
        var manifest = ManifestForMsix();
        var result = MsixPacker.PackAsync(manifest, "my-app.exe", binDir, directory.Path, "win-x64").GetAwaiter().GetResult();
        Assert(File.Exists(result.Path), "The MSIX must be written to the output directory.");
        Assert(result.Path.EndsWith(".msix", StringComparison.OrdinalIgnoreCase), "The output must carry an .msix extension.");
        Assert(!result.Signed, "Without a certificate the package must be emitted unsigned.");
        Assert(result.Sha256.Length == 64, "The package SHA-256 must be computed.");
        Assert(MsixPacker.VerifyBlockMap(result.Path), "The on-disk block map must match the stored payload hashes.");
    }

    private static void EndToEndPacksValidPackage()
    {
        using var directory = TempDirectory.Create();
        var binDir = Path.Combine(directory.Path, "bin");
        Directory.CreateDirectory(binDir);
        var payload = WriteSamplePayload(binDir);
        var manifest = ManifestForMsix();
        var result = MsixPacker.PackAsync(manifest, "my-app.exe", binDir, directory.Path, "win-x64").GetAwaiter().GetResult();

        using var archive = ZipFile.OpenRead(result.Path);
        Assert(archive.GetEntry("[Content_Types].xml") is not null, "The OPC content types part must be present.");
        Assert(archive.GetEntry("AppxManifest.xml") is not null, "The appx manifest part must be present.");
        Assert(archive.GetEntry("AppxBlockMap.xml") is not null, "The appx block map part must be present.");

        foreach (var relative in payload)
        {
            Assert(archive.GetEntry(relative) is not null, $"The payload part {relative} must be included.");
        }

        Assert(MsixPacker.VerifyBlockMap(result.Path), "The packaged payload must satisfy the block map.");
    }

    private static List<string> WriteSamplePayload(string binDir)
    {
        var files = new List<string>();
        File.WriteAllText(Path.Combine(binDir, "my-app.exe"), "app-binary");
        files.Add("my-app.exe");
        File.WriteAllText(Path.Combine(binDir, "app.dll"), "managed");
        files.Add("app.dll");
        File.WriteAllText(Path.Combine(binDir, "index.html"), "<html></html>");
        files.Add("index.html");
        return files;
    }

    private static AppManifest ManifestForMsix() =>
        AppManifestLoader.Parse(
            """
            {
              "product": { "name": "my-app", "version": "0.1.0", "identifier": "com.example.app" },
              "build": { "frontendDist": "web/dist" },
              "bundle": { "targets": ["msix"] }
            }
            """);

    private static AppManifest ManifestForAppBundle(params string[] schemes) =>
        AppManifestLoader.Parse(
            $$"""
            {
              "product": { "name": "my-app", "version": "0.1.0", "identifier": "com.example.app" },
              "build": { "frontendDist": "web/dist" },
              "bundle": {
                "targets": ["app-bundle"],
                "macOS": {
                  "bundleId": "com.example.app",
                  "schemes": [{{string.Join(", ", schemes.Select(static s => $"\"{s}\""))}}]
                }
              }
            }
            """);

    private static void InfoPlistRendersAllRequiredKeys()
    {
        var manifest = ManifestForAppBundle("tarui");
        var infoPlist = InfoPlistBuilder.Build(manifest, "osx-arm64");

        Assert(infoPlist.StartsWith("<?xml", StringComparison.Ordinal),
            "Info.plist must declare the XML 1.0 processing instruction.");
        Assert(infoPlist.Contains("<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\"", StringComparison.Ordinal),
            "Info.plist must declare the PLIST 1.0 DOCTYPE for plutil compatibility.");
        Assert(infoPlist.Contains("<plist version=\"1.0\">", StringComparison.Ordinal),
            "Info.plist must wrap the body in <plist version=\"1.0\">.");
        foreach (var required in new[]
                 {
                     "CFBundleIdentifier", "CFBundleName", "CFBundleDisplayName",
                     "CFBundleExecutable", "CFBundleVersion", "CFBundleShortVersionString",
                     "CFBundlePackageType", "CFBundleSignature", "LSMinimumSystemVersion",
                     "NSHighResolutionCapable", "NSPrincipalClass",
                 })
        {
            Assert(infoPlist.Contains($"<key>{required}</key>", StringComparison.Ordinal),
                $"Info.plist must declare the {required} key.");
        }

        Assert(infoPlist.Contains("<string>com.example.app</string>", StringComparison.Ordinal),
            "The bundle identifier from the manifest must be carried into CFBundleIdentifier.");
        Assert(infoPlist.Contains("<string>my-app</string>", StringComparison.Ordinal),
            "The product name must populate CFBundleName / CFBundleDisplayName.");
        Assert(infoPlist.Contains("<string>APPL</string>", StringComparison.Ordinal),
            "CFBundlePackageType must be APPL for an application bundle.");
    }

    private static void InfoPlistEmitsUrlTypesForEveryScheme()
    {
        var manifest = ManifestForAppBundle("tarui", "market");
        var infoPlist = InfoPlistBuilder.Build(manifest, "osx-arm64");

        Assert(infoPlist.Contains("<key>CFBundleURLTypes</key>", StringComparison.Ordinal),
            "Info.plist must declare CFBundleURLTypes when schemes are configured.");
        Assert(infoPlist.Contains("<string>tarui</string>", StringComparison.Ordinal),
            "Every configured scheme must appear under CFBundleURLSchemes.");
        Assert(infoPlist.Contains("<string>market</string>", StringComparison.Ordinal),
            "Every configured scheme must appear under CFBundleURLSchemes.");
        Assert(infoPlist.Contains("net.tarui.tarui", StringComparison.Ordinal),
            "CFBundleURLName must use the net.tarui.<scheme> convention for primary scheme.");
        Assert(infoPlist.Contains("net.tarui.market", StringComparison.Ordinal),
            "CFBundleURLName must use the net.tarui.<scheme> convention for the second scheme.");
    }

    private static void InfoPlistDeduplicatesRepeatedScheme()
    {
        var manifest = ManifestForAppBundle("tarui", "tarui");
        var urlTypes = InfoPlistBuilder.BuildUrlTypes(manifest.Bundle.MacOs!.Schemes);

        var occurrences = CountOccurrences(urlTypes, "<string>tarui</string>");
        Assert(occurrences == 1,
            $"Repeated schemes must collapse to a single CFBundleURLTypes entry, got {occurrences}.");
    }

    private static void InfoPlistRejectsInvalidSchemeToken()
    {
        var manifest = AppManifestLoader.Parse(
            """
            {
              "product": { "name": "my-app", "version": "0.1.0", "identifier": "com.example.app" },
              "build": { "frontendDist": "web/dist" },
              "bundle": {
                "targets": ["app-bundle"],
                "macOS": { "schemes": ["1bad"] }
              }
            }
            """);
        Throws<CliException>(
            () => InfoPlistBuilder.Build(manifest, "osx-arm64"),
            "Schemes starting with a digit must be rejected at injection time.");
    }

    private static void InfoPlistRejectsBadBundleId()
    {
        var manifest = AppManifestLoader.Parse(
            """
            {
              "product": { "name": "my-app", "version": "0.1.0", "identifier": "com.example.app" },
              "build": { "frontendDist": "web/dist" },
              "bundle": {
                "targets": ["app-bundle"],
                "macOS": { "bundleId": "Not A Bundle Id" }
              }
            }
            """);
        Throws<CliException>(
            () => InfoPlistBuilder.Build(manifest, "osx-arm64"),
            "CFBundleIdentifier must follow the reverse-DNS grammar.");
    }

    private static void InfoPlistUsesRidDefaultForMinimumSystemVersion()
    {
        var arm = InfoPlistBuilder.Build(ManifestForAppBundle(), "osx-arm64");
        Assert(arm.Contains("<string>11.0</string>", StringComparison.Ordinal),
            "osx-arm64 must default LSMinimumSystemVersion to 11.0.");

        var x64 = InfoPlistBuilder.Build(ManifestForAppBundle(), "osx-x64");
        Assert(x64.Contains("<string>10.15</string>", StringComparison.Ordinal),
            "osx-x64 must default LSMinimumSystemVersion to 10.15.");
    }

    private static void MacOsBundleValidatesRid()
    {
        using var directory = TempDirectory.Create();
        var binDir = Path.Combine(directory.Path, "bin");
        Directory.CreateDirectory(binDir);
        File.WriteAllText(Path.Combine(binDir, "my-app"), "binary");
        Throws<CliException>(
            () => MacOsBundleBuilder.BuildAsync(ManifestForAppBundle("tarui"), binDir, directory.Path, "win-x64").GetAwaiter().GetResult(),
            "Non-macOS RIDs must be rejected by the bundle builder.");
    }

    private static void MacOsBundleProducesExpectedLayout()
    {
        using var directory = TempDirectory.Create();
        var binDir = Path.Combine(directory.Path, "bin");
        Directory.CreateDirectory(binDir);
        File.WriteAllText(Path.Combine(binDir, "my-app"), "binary");
        File.WriteAllText(Path.Combine(binDir, "my-app.dll"), "managed");
        File.WriteAllText(Path.Combine(binDir, "index.html"), "<html></html>");

        var result = MacOsBundleBuilder.BuildAsync(ManifestForAppBundle("tarui"), binDir, directory.Path, "osx-arm64").GetAwaiter().GetResult();
        Assert(Directory.Exists(Path.Combine(result.BundlePath, "Contents", "MacOS")),
            "Contents/MacOS must exist inside the assembled bundle.");
        Assert(File.Exists(Path.Combine(result.BundlePath, "Contents", "Info.plist")),
            "Contents/Info.plist must be written.");
        Assert(File.Exists(Path.Combine(result.BundlePath, "Contents", "PkgInfo")),
            "Contents/PkgInfo must be written.");
        Assert(File.Exists(Path.Combine(result.BundlePath, "Contents", "MacOS", "my-app")),
            "The product executable must be promoted into Contents/MacOS.");
        Assert(File.Exists(Path.Combine(result.BundlePath, "Contents", "Resources", "my-app.dll")),
            "Managed assemblies must be carried into Contents/Resources.");
        Assert(File.Exists(Path.Combine(result.BundlePath, "Contents", "Resources", "index.html")),
            "Frontend assets must be carried into Contents/Resources.");
    }

    private static void MacOsBundleEmitsTarGzArchive()
    {
        using var directory = TempDirectory.Create();
        var binDir = Path.Combine(directory.Path, "bin");
        Directory.CreateDirectory(binDir);
        File.WriteAllText(Path.Combine(binDir, "my-app"), "binary");

        var result = MacOsBundleBuilder.BuildAsync(ManifestForAppBundle("tarui"), binDir, directory.Path, "osx-arm64").GetAwaiter().GetResult();
        Assert(File.Exists(result.TarGzPath), "The tar.gz archive must be written next to the bundle directory.");
        Assert(result.TarGzPath.EndsWith(".app.tar.gz", StringComparison.OrdinalIgnoreCase),
            "The archive must carry the .app.tar.gz suffix.");
        Assert(result.Sha256.Length == 64, "The archive SHA-256 must be computed.");
    }

    private static void AppBundleTargetAcceptedByValidator()
    {
        var manifest = AppManifestLoader.Parse(
            """
            {
              "product": { "name": "my-app", "version": "0.1.0", "identifier": "com.example.app" },
              "build": { "frontendDist": "web/dist" },
              "bundle": { "targets": ["app-bundle"] }
            }
            """);
        var errors = AppManifestValidator.Validate(manifest, ".");
        Assert(errors.Count == 0,
            $"An app-bundle target alone must validate, got: {string.Join("; ", errors)}");
    }

    private static void MacOsSchemesWithoutTargetIsReported()
    {
        var manifest = AppManifestLoader.Parse(
            """
            {
              "product": { "name": "a", "version": "0.1.0", "identifier": "a" },
              "build": { "frontendDist": "d" },
              "bundle": { "targets": ["zip"], "macOS": { "schemes": ["tarui"] } }
            }
            """);
        var errors = AppManifestValidator.Validate(manifest, ".");
        Assert(Has(errors, "'app-bundle'"),
            "bundle.macOS without an app-bundle target must be reported.");
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
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

