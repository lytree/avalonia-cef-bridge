using System.Xml;
using System.Xml.Linq;
using System.Text.RegularExpressions;

namespace Tarui.Architecture.Tests;

internal static class Program
{
    private static readonly SourceRule[] ForbiddenSourceRules =
    [
        new("System.Reflection", false),
        new("Activator", true),
        new("MethodInfo", true),
        new("dynamic", true),
        new("AssemblyLoadContext", true),
        new("XmlSerializer", true),
        new("ReactiveUI", true),
        new("System.Reactive", false),
        new("Avalonia.Controls.WebView", false)
    ];

    private static readonly string[] ForbiddenPackageFragments =
    [
        "CefGlue",
        "CefSharp",
        "CefNet",
        "Chromium",
        "LibCef",
        "CefRuntime",
        "CefBrowser",
        "CefHost",
        "WebView2",
        "ReactiveUI",
        "System.Reactive",
        "Avalonia.Controls.WebView"
    ];

    public static int Main()
    {
        try
        {
            var repositoryRoot = RepositoryRoot.Find();
            var result = new ArchitectureGate(repositoryRoot).Run();

            Console.WriteLine($"Tarui architecture gate scanned {result.FileCount} active files.");

            if (result.Violations.Count == 0)
            {
                Console.WriteLine("Tarui architecture gate passed.");
                return 0;
            }

            foreach (var violation in result.Violations)
            {
                Console.Error.WriteLine(
                    $"[{violation.Rule}] {violation.Path}:{violation.Line}:{violation.Column} {violation.Message}");
            }

            Console.Error.WriteLine($"Tarui architecture gate failed with {result.Violations.Count} violation(s).");
            return 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Tarui architecture gate could not run: {exception.Message}");
            return 2;
        }
    }

    private sealed class ArchitectureGate
    {
        private readonly string _repositoryRoot;
        private readonly string _sourceRoot;
        private readonly string _vendoredCefGlueRoot;

        public ArchitectureGate(string repositoryRoot)
        {
            _repositoryRoot = repositoryRoot;
            _sourceRoot = Path.Combine(repositoryRoot, "src");
            _vendoredCefGlueRoot = Path.Combine(_sourceRoot, "webview", "cefglue");
        }

        public GateResult Run()
        {
            var violations = new List<Violation>();
            var projectFiles = EnumerateProjectFiles().ToArray();
            var sourceFiles = EnumerateActiveSourceFiles(projectFiles).ToArray();
            var files = projectFiles.Concat(sourceFiles).ToArray();

            foreach (var file in projectFiles)
            {
                ScanProjectFile(file, violations);
            }

            foreach (var file in sourceFiles)
            {
                ScanSource(file, violations);
            }

            return new GateResult(files.Length, violations);
        }

        private IEnumerable<string> EnumerateProjectFiles()
        {
            if (!Directory.Exists(_sourceRoot))
            {
                throw new DirectoryNotFoundException($"Source root was not found: {_sourceRoot}");
            }

            foreach (var file in Directory.EnumerateFiles(_sourceRoot, "*", SearchOption.AllDirectories)
                         .Where(IsActiveFile)
                         .Where(IsProjectFile)
                         .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
            {
                yield return file;
            }

            foreach (var fileName in new[] { "Directory.Build.props", "Directory.Build.targets" })
            {
                var file = Path.Combine(_repositoryRoot, fileName);
                if (File.Exists(file))
                {
                    yield return file;
                }
            }
        }

        private static IEnumerable<string> EnumerateActiveSourceFiles(IReadOnlyList<string> projectFiles)
        {
            var activeSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var projectFile in projectFiles.Where(static path =>
                         string.Equals(Path.GetExtension(path), ".csproj", StringComparison.OrdinalIgnoreCase)))
            {
                var document = LoadProjectDocument(projectFile);
                if (document is null)
                {
                    continue;
                }

                var projectDirectory = Path.GetDirectoryName(projectFile)!;
                var projectSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var defaultCompileItems = IsDefaultCompileEnabled(document);

                if (defaultCompileItems)
                {
                    foreach (var source in Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
                                 .Where(IsActiveFile))
                    {
                        projectSources.Add(Path.GetFullPath(source));
                    }
                }

                foreach (var include in ReadCompileItems(document, "Include"))
                {
                    AddCompileMatches(projectSources, projectDirectory, include);
                }

                foreach (var remove in ReadCompileItems(document, "Remove"))
                {
                    projectSources.RemoveWhere(path => MatchesCompileItem(projectDirectory, path, remove));
                }

                activeSources.UnionWith(projectSources);
            }

            return activeSources
                .Where(IsActiveFile)
                .Where(static path => string.Equals(Path.GetExtension(path), ".cs", StringComparison.OrdinalIgnoreCase))
                .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase);
        }

        private static XDocument? LoadProjectDocument(string file)
        {
            try
            {
                return XDocument.Load(file, LoadOptions.PreserveWhitespace);
            }
            catch
            {
                return null;
            }
        }

        private static bool IsDefaultCompileEnabled(XDocument document)
        {
            var enableDefaultItems = ReadProperty(document, "EnableDefaultItems");
            var enableDefaultCompileItems = ReadProperty(document, "EnableDefaultCompileItems");

            return !string.Equals(enableDefaultItems, "false", StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(enableDefaultCompileItems, "false", StringComparison.OrdinalIgnoreCase);
        }

        private static string? ReadProperty(XDocument document, string name) =>
            document.Descendants()
                .FirstOrDefault(element => element.Name.LocalName == name)?
                .Value
                .Trim();

        private static IEnumerable<string> ReadCompileItems(XDocument document, string attributeName) =>
            document.Descendants()
                .Where(static element => element.Name.LocalName == "Compile")
                .Select(element => element.Attribute(attributeName)?.Value)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .SelectMany(static value => value!.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        private static void AddCompileMatches(HashSet<string> sources, string projectDirectory, string pattern)
        {
            if (!ContainsWildcard(pattern))
            {
                var path = Path.GetFullPath(Path.Combine(projectDirectory, pattern));
                if (File.Exists(path) && IsActiveFile(path))
                {
                    sources.Add(path);
                }

                return;
            }

            foreach (var source in Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
                         .Where(IsActiveFile))
            {
                if (MatchesCompileItem(projectDirectory, source, pattern))
                {
                    sources.Add(Path.GetFullPath(source));
                }
            }
        }

        private static bool MatchesCompileItem(string projectDirectory, string source, string pattern)
        {
            var relativePath = NormalizePath(Path.GetRelativePath(projectDirectory, source));
            var normalizedPattern = NormalizePath(pattern).TrimStart('.', '/');
            var regexPattern = Regex.Escape(normalizedPattern)
                .Replace("\\*\\*/", "(?:.*/)?", StringComparison.Ordinal)
                .Replace("\\*\\*", ".*", StringComparison.Ordinal)
                .Replace("\\*", "[^/]*", StringComparison.Ordinal)
                .Replace("\\?", "[^/]", StringComparison.Ordinal);

            return Regex.IsMatch(
                relativePath,
                $"^{regexPattern}$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static bool ContainsWildcard(string value) => value.Contains('*') || value.Contains('?');

        private static string NormalizePath(string path) => path.Replace('\\', '/');

        private void ScanSource(string file, List<Violation> violations)
        {
            var source = File.ReadAllText(file);
            var maskedSource = CSharpMasker.Mask(source);

            foreach (var rule in ForbiddenSourceRules)
            {
                var searchStart = 0;
                while (searchStart < maskedSource.Length)
                {
                    var index = maskedSource.IndexOf(rule.Text, searchStart, StringComparison.Ordinal);
                    if (index < 0)
                    {
                        break;
                    }

                    if (!rule.WholeWord || IsWholeWord(maskedSource, index, rule.Text.Length))
                    {
                        violations.Add(CreateViolation(
                            file,
                            maskedSource,
                            index,
                            "TN-SOURCE",
                            $"Forbidden runtime API or dependency marker '{rule.Text}'."));
                    }

                    searchStart = index + rule.Text.Length;
                }
            }
        }

        private void ScanProjectFile(string file, List<Violation> violations)
        {
            XDocument document;
            try
            {
                document = XDocument.Load(file, LoadOptions.PreserveWhitespace);
            }
            catch (Exception exception)
            {
                violations.Add(new Violation(
                    "TN-PROJECT",
                    RelativePath(file),
                    1,
                    1,
                    $"Project file is not valid XML: {exception.Message}"));
                return;
            }

            var isVendoredCefGlueProject = IsUnder(file, _vendoredCefGlueRoot);

            foreach (var packageReference in document.Descendants()
                         .Where(static element => element.Name.LocalName == "PackageReference"))
            {
                var packageId = ReadPackageId(packageReference);
                if (string.IsNullOrWhiteSpace(packageId))
                {
                    violations.Add(CreateProjectViolation(
                        file,
                        packageReference,
                        "TN-PACKAGE",
                        "PackageReference must declare a static Include or Update package ID."));
                    continue;
                }

                if (isVendoredCefGlueProject && !IsAllowedAvaloniaPackage(packageId))
                {
                    violations.Add(CreateProjectViolation(
                        file,
                        packageReference,
                        "TN-PACKAGE",
                        $"Vendored CefGlue projects may reference only Avalonia framework packages; found '{packageId}'."));
                    continue;
                }

                if (IsForbiddenRuntimePackage(packageId))
                {
                    violations.Add(CreateProjectViolation(
                        file,
                        packageReference,
                        "TN-PACKAGE",
                        $"CEF/WebView/Reactive runtime package '{packageId}' is forbidden; use vendored source and explicit project references."));
                }
            }
        }

        private Violation CreateProjectViolation(
            string file,
            XElement element,
            string rule,
            string message)
        {
            var lineInfo = (IXmlLineInfo)element;
            var line = lineInfo.HasLineInfo() ? lineInfo.LineNumber : 1;
            var column = lineInfo.HasLineInfo() ? lineInfo.LinePosition : 1;
            return new Violation(rule, RelativePath(file), line, column, message);
        }

        private Violation CreateViolation(
            string file,
            string maskedSource,
            int index,
            string rule,
            string message)
        {
            var line = 1;
            var lineStart = 0;
            for (var current = 0; current < index; current++)
            {
                if (maskedSource[current] == '\n')
                {
                    line++;
                    lineStart = current + 1;
                }
            }

            return new Violation(
                rule,
                RelativePath(file),
                line,
                index - lineStart + 1,
                message);
        }

        private string RelativePath(string path) =>
            Path.GetRelativePath(_repositoryRoot, path).Replace('\\', '/');

        private static string? ReadPackageId(XElement element)
        {
            var attribute = element.Attribute("Include") ?? element.Attribute("Update");
            if (attribute is not null)
            {
                return attribute.Value.Trim();
            }

            return element.Elements()
                .FirstOrDefault(static child => child.Name.LocalName is "Include" or "Update")?
                .Value
                .Trim();
        }

        private static bool IsAllowedAvaloniaPackage(string packageId) =>
            packageId.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase) &&
            !packageId.Contains("WebView", StringComparison.OrdinalIgnoreCase);

        private static bool IsForbiddenRuntimePackage(string packageId)
        {
            if (string.Equals(packageId, "Cef", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return ForbiddenPackageFragments.Any(fragment =>
                packageId.Contains(fragment, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsSourceFile(string path) =>
            string.Equals(Path.GetExtension(path), ".cs", StringComparison.OrdinalIgnoreCase);

        private static bool IsProjectFile(string path) =>
            string.Equals(Path.GetExtension(path), ".csproj", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetExtension(path), ".props", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetExtension(path), ".targets", StringComparison.OrdinalIgnoreCase);

        private static bool IsActiveFile(string path)
        {
            var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return !segments.Any(static segment =>
                string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(segment, ".git", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsUnder(string path, string root)
        {
            var fullPath = Path.GetFullPath(path);
            var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                           Path.DirectorySeparatorChar;
            return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsWholeWord(string text, int index, int length)
        {
            var hasPrefix = index > 0 && IsIdentifierCharacter(text[index - 1]);
            var end = index + length;
            var hasSuffix = end < text.Length && IsIdentifierCharacter(text[end]);
            return !hasPrefix && !hasSuffix;
        }

        private static bool IsIdentifierCharacter(char value) =>
            char.IsLetterOrDigit(value) || value == '_';
    }

    private static class RepositoryRoot
    {
        public static string Find()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                var solution = Path.Combine(directory.FullName, "tarui.net.sln");
                var source = Path.Combine(directory.FullName, "src");
                var tests = Path.Combine(directory.FullName, "tests");

                var cefGlue = Path.Combine(source, "webview", "cefglue");
                if (File.Exists(solution) && Directory.Exists(source) && Directory.Exists(tests) && Directory.Exists(cefGlue))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException(
                $"Could not locate tarui.net repository root from '{AppContext.BaseDirectory}'.");
        }
    }

    private static class CSharpMasker
    {
        public static string Mask(string source)
        {
            var buffer = source.ToCharArray();
            var index = 0;

            while (index < buffer.Length)
            {
                if (source[index] == '/' && index + 1 < source.Length && source[index + 1] == '/')
                {
                    MaskLineComment(buffer, index);
                    index += 2;
                    while (index < source.Length && source[index] is not '\r' and not '\n')
                    {
                        buffer[index++] = ' ';
                    }

                    continue;
                }

                if (source[index] == '/' && index + 1 < source.Length && source[index + 1] == '*')
                {
                    MaskBlockComment(buffer, index);
                    index += 2;
                    while (index + 1 < source.Length)
                    {
                        if (source[index] == '*' && source[index + 1] == '/')
                        {
                            buffer[index] = ' ';
                            buffer[index + 1] = ' ';
                            index += 2;
                            break;
                        }

                        MaskCharacter(buffer, index++);
                    }

                    continue;
                }

                if (TryMaskRawString(source, buffer, ref index))
                {
                    continue;
                }

                if (TryMaskVerbatimString(source, buffer, ref index))
                {
                    continue;
                }

                if (source[index] == '"')
                {
                    MaskRegularString(source, buffer, ref index);
                    continue;
                }

                if (source[index] == '\'')
                {
                    MaskCharacterLiteral(source, buffer, ref index);
                    continue;
                }

                index++;
            }

            return new string(buffer);
        }

        private static bool TryMaskRawString(string source, char[] buffer, ref int index)
        {
            var quoteIndex = index;
            while (quoteIndex < source.Length && source[quoteIndex] == '$')
            {
                quoteIndex++;
            }

            if (quoteIndex >= source.Length || source[quoteIndex] != '"')
            {
                return false;
            }

            var quoteCount = CountQuotes(source, quoteIndex);
            if (quoteCount < 3)
            {
                return false;
            }

            var delimiterEnd = quoteIndex + quoteCount;
            for (var current = index; current < delimiterEnd; current++)
            {
                buffer[current] = ' ';
            }

            index = delimiterEnd;
            while (index < source.Length)
            {
                if (source[index] == '"' && CountQuotes(source, index) >= quoteCount)
                {
                    for (var current = index; current < index + quoteCount; current++)
                    {
                        buffer[current] = ' ';
                    }

                    index += quoteCount;
                    return true;
                }

                MaskCharacter(buffer, index++);
            }

            return true;
        }

        private static bool TryMaskVerbatimString(string source, char[] buffer, ref int index)
        {
            var quoteIndex = index;
            if (source[quoteIndex] == '@')
            {
                quoteIndex++;
            }
            else if (source[quoteIndex] == '$' && quoteIndex + 1 < source.Length && source[quoteIndex + 1] == '@')
            {
                quoteIndex += 2;
            }
            else if (source[quoteIndex] == '@' && quoteIndex + 1 < source.Length && source[quoteIndex + 1] == '$')
            {
                quoteIndex += 2;
            }
            else
            {
                return false;
            }

            if (quoteIndex >= source.Length || source[quoteIndex] != '"')
            {
                return false;
            }

            for (var current = index; current <= quoteIndex; current++)
            {
                buffer[current] = ' ';
            }

            index = quoteIndex + 1;
            while (index < source.Length)
            {
                if (source[index] == '"')
                {
                    buffer[index] = ' ';
                    if (index + 1 < source.Length && source[index + 1] == '"')
                    {
                        buffer[index + 1] = ' ';
                        index += 2;
                        continue;
                    }

                    index++;
                    return true;
                }

                MaskCharacter(buffer, index++);
            }

            return true;
        }

        private static void MaskRegularString(string source, char[] buffer, ref int index)
        {
            buffer[index++] = ' ';
            while (index < source.Length)
            {
                if (source[index] == '\\')
                {
                    buffer[index++] = ' ';
                    if (index < source.Length)
                    {
                        buffer[index++] = ' ';
                    }

                    continue;
                }

                var closes = source[index] == '"';
                buffer[index++] = ' ';
                if (closes)
                {
                    return;
                }
            }
        }

        private static void MaskCharacterLiteral(string source, char[] buffer, ref int index)
        {
            buffer[index++] = ' ';
            while (index < source.Length)
            {
                if (source[index] == '\\')
                {
                    buffer[index++] = ' ';
                    if (index < source.Length)
                    {
                        buffer[index++] = ' ';
                    }

                    continue;
                }

                var closes = source[index] == '\'';
                buffer[index++] = ' ';
                if (closes)
                {
                    return;
                }
            }
        }

        private static void MaskLineComment(char[] buffer, int index)
        {
            buffer[index] = ' ';
            if (index + 1 < buffer.Length)
            {
                buffer[index + 1] = ' ';
            }
        }

        private static void MaskBlockComment(char[] buffer, int index)
        {
            buffer[index] = ' ';
            if (index + 1 < buffer.Length)
            {
                buffer[index + 1] = ' ';
            }
        }

        private static void MaskCharacter(char[] buffer, int index)
        {
            if (buffer[index] is not '\r' and not '\n')
            {
                buffer[index] = ' ';
            }
        }

        private static int CountQuotes(string source, int index)
        {
            var count = 0;
            while (index < source.Length && source[index] == '"')
            {
                count++;
                index++;
            }

            return count;
        }
    }

    private sealed record SourceRule(string Text, bool WholeWord);

    private sealed record Violation(
        string Rule,
        string Path,
        int Line,
        int Column,
        string Message);

    private sealed record GateResult(int FileCount, List<Violation> Violations);
}
