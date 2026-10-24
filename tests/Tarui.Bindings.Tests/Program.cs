using System.Text.RegularExpressions;
using Tarui.Contracts;

namespace Tarui.Bindings.Tests;

/// <summary>
/// Drift gate for the typed command contract. It unions the per-assembly
/// <c>{Assembly}CommandCatalog.Contracts</c> emitted by Tarui.Ipc.Generators and cross-checks it against
/// the hand-written <c>@lytree/api</c> bindings, so a command cannot be added on one side only.
/// </summary>
internal static class Program
{
    private static readonly Regex InvokeLiteralPattern = new(
        @"\binvoke\s*(?:<[^>]*>)?\s*\(\s*'([^']+)'",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Commands the shell posts on its own (never written as an <c>invoke('…')</c> literal in
    /// <c>@lytree/api</c>), so the front-end scan legitimately never finds them.
    /// </summary>
    private static readonly string[] InternalOnlyCommands =
    [
        "plugin:webview|eval-callback-complete",
    ];

    public static int Main()
    {
        var generated = CollectGeneratedContracts();

        Assert(generated.Count > 0, "The generated command contract must not be empty.");
        FrontendInvokeLiteralsMatchGeneratedCommandContract(generated);
        InternalOnlyCommandsAreAllowlisted(generated);
        GeneratedCommandContractCarriesUniqueTypedEntries(generated);

        Console.WriteLine("Tarui.Bindings self-tests passed.");
        return 0;
    }

    private static void FrontendInvokeLiteralsMatchGeneratedCommandContract(
        IReadOnlyList<TaruiCommandContract> generated)
    {
        var known = generated.Select(static contract => contract.Id).ToHashSet(StringComparer.Ordinal);
        var invoked = ReadFrontendInvokeLiterals();

        Assert(invoked.Count > 0, "The front-end scan must find at least one invoke() literal.");

        var unknown = invoked
            .Where(id => !known.Contains(id))
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToArray();
        Assert(
            unknown.Length == 0,
            "Every command the front end invokes must exist in the generated contract; unknown: "
                + string.Join(", ", unknown));

        var allowlist = InternalOnlyCommands.ToHashSet(StringComparer.Ordinal);
        var unbound = known
            .Where(id => !invoked.Contains(id) && !allowlist.Contains(id))
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToArray();
        Assert(
            unbound.Length == 0,
            "Every generated command must be invoked by the front end or allowlisted as internal-only; unbound: "
                + string.Join(", ", unbound));
    }

    private static void InternalOnlyCommandsAreAllowlisted(IReadOnlyList<TaruiCommandContract> generated)
    {
        var known = generated.Select(static contract => contract.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var command in InternalOnlyCommands)
        {
            Assert(
                known.Contains(command),
                $"Allowlisted internal command '{command}' must exist in the generated contract.");
        }
    }

    private static void GeneratedCommandContractCarriesUniqueTypedEntries(
        IReadOnlyList<TaruiCommandContract> generated)
    {
        foreach (var contract in generated)
        {
            Assert(!string.IsNullOrWhiteSpace(contract.Id), "A generated command contract must carry an id.");
            Assert(!string.IsNullOrWhiteSpace(contract.Input), $"Command '{contract.Id}' must declare an input DTO.");
            Assert(!string.IsNullOrWhiteSpace(contract.Output), $"Command '{contract.Id}' must declare an output DTO.");
        }

        var duplicates = generated
            .GroupBy(static contract => contract.Id, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToArray();
        Assert(
            duplicates.Length == 0,
            "Generated command ids must be unique; duplicates: " + string.Join(", ", duplicates));
    }

    private static List<TaruiCommandContract> CollectGeneratedContracts()
    {
        var contracts = new List<TaruiCommandContract>();
        Add(contracts, global::Tarui.Generated.Tarui_Plugins_AutostartCommandCatalog.Contracts);
        Add(contracts, global::Tarui.Generated.Tarui_Plugins_CookieCommandCatalog.Contracts);
        Add(contracts, global::Tarui.Generated.Tarui_Plugins_CoreCommandCatalog.Contracts);
        Add(contracts, global::Tarui.Generated.Tarui_Plugins_DeepLinkCommandCatalog.Contracts);
        Add(contracts, global::Tarui.Generated.Tarui_Plugins_DialogCommandCatalog.Contracts);
        Add(contracts, global::Tarui.Generated.Tarui_Plugins_EventCommandCatalog.Contracts);
        Add(contracts, global::Tarui.Generated.Tarui_Plugins_FileSystemCommandCatalog.Contracts);
        Add(contracts, global::Tarui.Generated.Tarui_Plugins_GlobalShortcutCommandCatalog.Contracts);
        Add(contracts, global::Tarui.Generated.Tarui_Plugins_HttpCommandCatalog.Contracts);
        Add(contracts, global::Tarui.Generated.Tarui_Plugins_LogCommandCatalog.Contracts);
        Add(contracts, global::Tarui.Generated.Tarui_Plugins_MenuCommandCatalog.Contracts);
        Add(contracts, global::Tarui.Generated.Tarui_Plugins_NotificationCommandCatalog.Contracts);
        Add(contracts, global::Tarui.Generated.Tarui_Plugins_PersistedScopeCommandCatalog.Contracts);
        Add(contracts, global::Tarui.Generated.Tarui_Plugins_ShellCommandCatalog.Contracts);
        Add(contracts, global::Tarui.Generated.Tarui_Plugins_SqlCommandCatalog.Contracts);
        Add(contracts, global::Tarui.Generated.Tarui_Plugins_StoreCommandCatalog.Contracts);
        Add(contracts, global::Tarui.Generated.Tarui_Plugins_SystemCommandCatalog.Contracts);
        Add(contracts, global::Tarui.Generated.Tarui_Plugins_TrayCommandCatalog.Contracts);
        Add(contracts, global::Tarui.Generated.Tarui_Plugins_UpdaterCommandCatalog.Contracts);
        Add(contracts, global::Tarui.Generated.Tarui_Plugins_WebviewCommandCatalog.Contracts);
        Add(contracts, global::Tarui.Generated.Tarui_Plugins_WebsocketCommandCatalog.Contracts);
        Add(contracts, global::Tarui.Generated.Tarui_Plugins_WindowCommandCatalog.Contracts);
        Add(contracts, global::Tarui.Generated.Tarui_Plugins_WindowStateCommandCatalog.Contracts);
        return contracts;
    }

    private static void Add(List<TaruiCommandContract> target, IReadOnlyList<TaruiCommandContract> source) =>
        target.AddRange(source);

    private static HashSet<string> ReadFrontendInvokeLiterals()
    {
        var apiDirectory = Path.Combine(RepositoryRoot.Find(), "web", "packages", "api");
        Assert(Directory.Exists(apiDirectory), $"The @lytree/api sources must exist at '{apiDirectory}'.");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(apiDirectory, "*.ts", SearchOption.AllDirectories))
        {
            if (file.EndsWith(".d.ts", StringComparison.Ordinal) ||
                file.EndsWith(".test.ts", StringComparison.Ordinal) ||
                file.Contains($"{Path.DirectorySeparatorChar}__tests__{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            var masked = MaskComments(File.ReadAllText(file));
            foreach (Match match in InvokeLiteralPattern.Matches(masked))
            {
                ids.Add(match.Groups[1].Value);
            }
        }

        return ids;
    }

    /// <summary>Blanks out comments (leaving string literals intact) so a commented invoke never counts.</summary>
    private static string MaskComments(string source)
    {
        var buffer = source.ToCharArray();
        var index = 0;

        while (index < buffer.Length)
        {
            var current = buffer[index];
            if (current is '\'' or '"' or '`')
            {
                index = SkipString(buffer, index, current);
                continue;
            }

            if (current == '/' && index + 1 < buffer.Length)
            {
                if (buffer[index + 1] == '/')
                {
                    while (index < buffer.Length && buffer[index] != '\n')
                    {
                        buffer[index] = ' ';
                        index++;
                    }

                    continue;
                }

                if (buffer[index + 1] == '*')
                {
                    buffer[index] = ' ';
                    buffer[index + 1] = ' ';
                    index += 2;
                    while (index + 1 < buffer.Length && !(buffer[index] == '*' && buffer[index + 1] == '/'))
                    {
                        buffer[index] = ' ';
                        index++;
                    }

                    if (index + 1 < buffer.Length)
                    {
                        buffer[index] = ' ';
                        buffer[index + 1] = ' ';
                        index += 2;
                    }

                    continue;
                }
            }

            index++;
        }

        return new string(buffer);
    }

    private static int SkipString(char[] buffer, int start, char quote)
    {
        var index = start + 1;
        while (index < buffer.Length)
        {
            if (buffer[index] == '\\')
            {
                index += 2;
                continue;
            }

            if (buffer[index] == quote)
            {
                return index + 1;
            }

            index++;
        }

        return index;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static class RepositoryRoot
    {
        public static string Find()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                var source = Path.Combine(directory.FullName, "src");
                var tests = Path.Combine(directory.FullName, "tests");
                if (Directory.Exists(source) && Directory.Exists(tests) &&
                    (File.Exists(Path.Combine(directory.FullName, "tarui.net.sln")) ||
                     File.Exists(Path.Combine(directory.FullName, "tarui.net.slnx"))))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException(
                $"Could not locate the repository root from '{AppContext.BaseDirectory}'.");
        }
    }
}
