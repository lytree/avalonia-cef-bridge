namespace Tarui.Cli;

using System.Text;

/// <summary>
/// Scaffolds a new Tarui plugin skeleton (<c>tarui plugin init</c>). Emits the
/// published-mode layout described in design §8.4:
/// src/Tarui.Plugins.{Name}, permissions/, guest-js/, tests/, examples/, README.md.
/// The CLI writes these files directly (no dotnet template package) so the output is
/// fully controlled and unit-testable; <c>--local</c> rewrites NuGet references to a
/// local Tarui source tree, mirroring <c>tarui init</c>.
/// </summary>
internal static class PluginScaffolder
{
    /// <summary>
    /// Normalizes a user-supplied plugin name ("my-foo", "foo") into the canonical
    /// suffix used by the package and directories, e.g. "foo".
    /// </summary>
    public static string NormalizePluginName(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (char.IsLetterOrDigit(ch) || ch == '-')
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Writes a complete plugin skeleton under <paramref name="outputDir"/> rooted
    /// at <c>tarui-plugin-{normalized}</c> (or directly in <paramref name="outputDir"/>
    /// when <paramref name="flat"/> is true). Returns the created plugin root directory.
    /// </summary>
    public static string Scaffold(string name, string outputDir, string? localRepo, bool flat = false)
    {
        var normalized = NormalizePluginName(name);
        if (normalized.Length == 0)
        {
            throw new CliUsageException($"Invalid plugin name '{name}'. Use letters, digits or '-'.'");
        }

        var csharpSuffix = ProjectName.ToIdentifier(normalized, "Plugin");
        var packageName = "Tarui.Plugins." + csharpSuffix;
        var sourceDirectory = "src/" + packageName;
        var namespaceName = packageName;

        var rootName = flat ? outputDir : Path.Combine(outputDir, "tarui-plugin-" + normalized);
        if (Directory.Exists(rootName))
        {
            throw new CliException($"Target directory already exists: {rootName}");
        }

        Directory.CreateDirectory(rootName);

        Write(Path.Combine(rootName, sourceDirectory), packageName + ".csproj", BuildCsproj(packageName, namespaceName));
        Write(Path.Combine(rootName, sourceDirectory), "Plugin.cs", BuildPluginCs(namespaceName));
        Write(Path.Combine(rootName, sourceDirectory), "Contracts.cs", BuildContractsCs(namespaceName));

        Write(Path.Combine(rootName, "permissions"), "schema.json", BuildSchemaJson(normalized));

        Write(Path.Combine(rootName, "guest-js"), "package.json", BuildGuestPackageJson(normalized));

        Write(
            Path.Combine(rootName, "tests", packageName + ".Tests"),
            "Program.cs",
            BuildTestProgramCs(packageName + ".Tests"));

        Write(Path.Combine(rootName, "examples", "demo"), "README.md", BuildExampleReadme(namespaceName, normalized));
        Write(Path.Combine(rootName), "README.md", BuildPluginReadme(normalized));

        if (!string.IsNullOrEmpty(localRepo))
        {
            RewriteLocal(
                Path.Combine(rootName, sourceDirectory, packageName + ".csproj"),
                Path.GetFullPath(localRepo));
        }

        return rootName;
    }

    private static string BuildCsproj(string packageName, string namespaceName)
    {
        return
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <!--
                {0}: published-mode plugin. Runtime Tarui libraries come from NuGet
                (Tarui.Ipc / Tarui.Contracts / Tarui.Ipc.Generators as an analyzer). The
                descriptors in permissions/ are packed alongside the assembly and merged
                into the application validation schema by 'tarui build'.
              -->
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>{1}</RootNamespace>
                <PackageId>{0}</PackageId>
                <Description>Tarui plugin: {2}.</Description>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.0.0" />
                <PackageReference Include="Tarui.Ipc" Version="0.1.0" />
                <PackageReference Include="Tarui.Contracts" Version="0.1.0" />
                <PackageReference Include="Tarui.Ipc.Generators" Version="0.1.0"
                                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
              </ItemGroup>
              <ItemGroup>
                <Content Include="..\..\permissions\*.json"
                         Pack="true" PackagePath="permissions/"
                         CopyToOutputDirectory="PreserveNewest" />
              </ItemGroup>
            </Project>
            """.Replace("{0}", packageName).Replace("{1}", namespaceName).Replace("{2}", packageName);
    }

    private static string BuildPluginCs(string namespaceName)
    {
        return
            $$"""
            using Microsoft.Extensions.DependencyInjection;
            using Tarui.Contracts;
            using Tarui.Ipc;

            namespace {{namespaceName}};

            /// <summary>
            /// Registers this plugin's commands. Every command must be explicitly
            /// permission-checked; add the corresponding permission identifiers to the
            /// capabilities file of the calling window to grant them.
            /// </summary>
            public sealed class FooPlugin : ITaruiPlugin
            {
                public void ConfigureCommands(CommandRouterBuilder commands)
                {
                    // commands.Add(
                    //     "plugin:foo|ping",
                    //     FooJsonContext.Default.EmptyArgs,
                    //     FooJsonContext.Default.FooPingResult,
                    //     (_, _, ct) => ValueTask.FromResult(new FooPingResult { Pong = true }),
                    //     "plugin:foo|ping");
                }
            }

            public static class FooPluginServiceCollectionExtensions
            {
                public static IServiceCollection AddFooPlugin(this IServiceCollection services)
                    => services.AddPlugin<FooPlugin>();
            }
            """;
    }

    private static string BuildContractsCs(string namespaceName)
    {
        return
            $$"""
            namespace {{namespaceName}};

            // Plugin-owned DTOs plus their source-generated JSON metadata. Because the
            // context lives here, this plugin needs no changes to core Tarui.Contracts.
            public sealed record FooPingResult(bool Pong);
            """;
    }

    private static string BuildSchemaJson(string normalized)
    {
        return
            $$"""
            {
              "$schema": "https://tarui.dev/schemas/plugin-permission.schema.json",
              "plugin": "{{normalized}}",
              "version": "0.1.0",
              "permissions": [
                {
                  "identifier": "plugin:{{normalized}}|ping",
                  "description": "Returns a ping result.",
                  "scope": null
                }
              ],
              "events": [],
              "default": [
                "plugin:{{normalized}}|ping"
              ]
            }
            """;
    }

    private static string BuildGuestPackageJson(string normalized)
    {
        return
            $$"""
            {
              "name": "@tarui/plugin-{{normalized}}",
              "version": "0.1.0",
              "type": "module",
              "files": ["dist"],
              "main": "./dist/index.js",
              "types": "./dist/index.d.ts",
              "exports": {
                ".": { "types": "./dist/index.d.ts", "default": "./dist/index.js" }
              },
              "scripts": {
                "build": "tsc -b",
                "prepack": "tsc -b"
              },
              "publishConfig": { "access": "public" }
            }
            """;
    }

    private static string BuildTestProgramCs(string namespaceName)
    {
        return
            $$"""
            using {{namespaceName}};

            // Console self-test skeleton (repository test discipline). Extend with the
            // four skeleton families: happy path, permission denied, invalid arguments,
            // platform unsupported.
            internal static class Program
            {
                public static int Main()
                {
                    return 0;
                }
            }
            """;
    }

    private static string BuildExampleReadme(string namespaceName, string normalized)
    {
        return
            $$"""
            # demo

            End-to-end wiring example for `{{namespaceName}}`.

            ```csharp
            builder.Services.AddFooPlugin();
            ```

            1. `dotnet add package {{namespaceName}}`
            2. `builder.Services.AddFooPlugin();`
            3. `pnpm add @tarui/plugin-{{normalized}}`
            4. Grant in `capabilities/main.json`:
               ```json
               { "identifier": "plugin:{{normalized}}|ping" }
               ```
            5. `tarui build` merges `permissions/schema.json` into the app schema.
            """;
    }

    private static string BuildPluginReadme(string normalized)
    {
        return
            $$"""
            # tarui-plugin-{{normalized}}

            Skeleton Tarui plugin (commands: `plugin:{{normalized}}|ping`).

            ## Permissions

            This plugin ships a `permissions/` directory (schema.json + default.json).
            No permission is granted automatically; authorize commands in the app's
            `capabilities/*.json`.

            ## Layout

            - `src/Tarui.Plugins.*` — plugin assembly, DI extension, contracts
            - `permissions/` — permission descriptors packed into the NuGet package
            - `guest-js/` — typed frontend bridge (`@tarui/plugin-{{normalized}}`)
            - `tests/` — console self-tests
            - `examples/demo/` — wiring example
            """;
    }

    private static void RewriteLocal(string csprojPath, string repoRoot)
    {
        var content = LocalReferenceRewriter.RewriteContent(File.ReadAllText(csprojPath), repoRoot);
        using var writer = new StreamWriter(csprojPath, append: false, Encoding.UTF8);
        writer.Write(content);
    }

    private static void Write(string directory, string fileName, string content)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, fileName), content);
    }
}