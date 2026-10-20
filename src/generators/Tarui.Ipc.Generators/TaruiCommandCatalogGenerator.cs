using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Tarui.Ipc.Generators;

/// <summary>
/// Emits a per-assembly <c>{Assembly}CommandCatalog</c> describing every routed command. Commands are
/// discovered from the literal <c>commands.Add("id", TaruiJsonContext.Default.Input,
/// TaruiJsonContext.Default.Output, handler, "permission", …)</c> call sites (the single registration
/// path, covering both attribute-decorated handlers and inline lambdas) and carry the argument/result
/// DTO type names plus the gating permission. The front-end TypeScript declaration
/// (<c>web/packages/api/generated/commands.ts</c>) is derived from these catalogs by
/// <c>tests/Tarui.Bindings.Tests</c>, which also drift-checks it.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class TaruiCommandCatalogGenerator : IIncrementalGenerator
{
    private const string CommandRouterBuilderMetadataName = "Tarui.Ipc.CommandRouterBuilder";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var commands = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is InvocationExpressionSyntax invocation && IsCandidateAdd(invocation),
                static (syntaxContext, _) => ReadCommand(syntaxContext))
            .Where(static command => command is not null)
            .Select(static (command, _) => command!);

        var input = commands.Collect().Combine(context.CompilationProvider);

        context.RegisterSourceOutput(input, static (productionContext, pair) =>
            EmitCatalog(productionContext, pair.Left, pair.Right.AssemblyName));
    }

    private static bool IsCandidateAdd(InvocationExpressionSyntax invocation) =>
        invocation.Expression is MemberAccessExpressionSyntax { Name.Identifier.Text: "Add" } &&
        invocation.ArgumentList.Arguments.Count is 5 or 6;

    private static CommandDescriptor? ReadCommand(GeneratorSyntaxContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var model = context.SemanticModel;

        if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method ||
            method.Name != "Add" ||
            method.ContainingType?.ToDisplayString() != CommandRouterBuilderMetadataName)
        {
            return null;
        }

        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count is not (5 or 6))
        {
            return null;
        }

        var id = GetStringLiteral(arguments[0], model);
        var input = GetJsonContextMemberName(arguments[1]);
        var output = GetJsonContextMemberName(arguments[2]);
        // The permission argument is always a compile-time literal in this repository; a non-constant
        // expression still yields a contract entry with an empty permission so the drift gate (which
        // requires a permission per command) surfaces it instead of silently dropping the command.
        var permission = GetStringLiteral(arguments[4], model) ?? string.Empty;
        if (id is null || input is null || output is null)
        {
            return null;
        }

        // The payload types come from the same JsonTypeInfo<T> the dispatcher deserializes with, so the
        // emitted shape cannot drift from the wire format.
        var shapes = new DtoShapeReader();
        shapes.Describe(input, GetJsonPayloadType(arguments[1], model));
        shapes.Describe(output, GetJsonPayloadType(arguments[2], model));

        return new CommandDescriptor(id, input, output, permission, shapes.Types);
    }

    private static string? GetStringLiteral(ArgumentSyntax argument, SemanticModel model) =>
        model.GetConstantValue(argument.Expression).Value as string;

    /// <summary>Reads the payload type out of a <c>TaruiJsonContext.Default.{Name}</c> argument.</summary>
    private static ITypeSymbol? GetJsonPayloadType(ArgumentSyntax argument, SemanticModel model)
    {
        if (model.GetSymbolInfo(argument.Expression).Symbol is not IPropertySymbol property ||
            property.Type is not INamedTypeSymbol { TypeArguments.Length: 1 } named ||
            named.OriginalDefinition.ToDisplayString() != "System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>")
        {
            return null;
        }

        return named.TypeArguments[0];
    }

    /// <summary>Reads the DTO name out of a <c>TaruiJsonContext.Default.{Name}</c> argument.</summary>
    private static string? GetJsonContextMemberName(ArgumentSyntax argument)
    {
        if (argument.Expression is MemberAccessExpressionSyntax
            {
                Name.Identifier.Text: { Length: > 0 } name,
                Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: "Default" }
            })
        {
            return name;
        }

        return null;
    }

    private static void EmitCatalog(
        SourceProductionContext context,
        ImmutableArray<CommandDescriptor> commands,
        string? assemblyName)
    {
        var distinct = commands
            .GroupBy(static command => command.Id, StringComparer.Ordinal)
            .Select(static group => group.First())
            .OrderBy(static command => command.Id, StringComparer.Ordinal)
            .ToArray();

        // Several commands share DTOs; the first description of a name wins so the output is stable.
        var types = distinct
            .SelectMany(static command => command.Types)
            .GroupBy(static type => type.Name, StringComparer.Ordinal)
            .Select(static group => group.First())
            .OrderBy(static type => type.Name, StringComparer.Ordinal)
            .ToArray();

        // The class is named per assembly so several plugin catalogs can be referenced at once without
        // a CS0433 clash (the bindings drift test unions them).
        var className = Sanitize(assemblyName) + "CommandCatalog";
        var source = new StringBuilder();
        source.AppendLine("// <auto-generated />");
        source.AppendLine("using System.Collections.Generic;");
        source.AppendLine("using Tarui.Contracts;");
        source.AppendLine();
        source.AppendLine("namespace Tarui.Generated;");
        source.AppendLine();
        source.AppendLine($"public static class {className}");
        source.AppendLine("{");
        source.AppendLine("    public static IReadOnlyList<string> Commands { get; } =");
        source.AppendLine("    [");

        foreach (var command in distinct)
        {
            source.Append("        \"");
            source.Append(Escape(command.Id));
            source.AppendLine("\",");
        }

        source.AppendLine("    ];");
        source.AppendLine();
        source.AppendLine("    public static IReadOnlyList<TaruiCommandContract> Contracts { get; } =");
        source.AppendLine("    [");

        foreach (var command in distinct)
        {
            source.Append("        new(\"");
            source.Append(Escape(command.Id));
            source.Append("\", \"");
            source.Append(Escape(command.Input));
            source.Append("\", \"");
            source.Append(Escape(command.Output));
            source.Append("\", \"");
            source.Append(Escape(command.Permission));
            source.AppendLine("\"),");
        }

        source.AppendLine("    ];");
        source.AppendLine();
        source.AppendLine("    /// <summary>");
        source.AppendLine("    /// The JSON shape of every argument/result type the commands above reference, resolved from");
        source.AppendLine("    /// the DTOs behind <c>TaruiJsonContext</c> (camelCase members, null members omitted).");
        source.AppendLine("    /// </summary>");
        source.AppendLine("    public static IReadOnlyList<TaruiTypeContract> Types { get; } =");
        source.AppendLine("    [");

        foreach (var type in types)
        {
            source.Append("        new(\"");
            source.Append(Escape(type.Name));
            source.Append("\", \"");
            source.Append(Escape(type.TypeExpression));
            source.Append('"');
            if (type.Body.Length > 0)
            {
                source.Append(", \"");
                source.Append(Escape(type.Body));
                source.Append('"');
            }

            source.AppendLine("),");
        }

        source.AppendLine("    ];");
        source.AppendLine("}");
        context.AddSource($"{className}.g.cs", source.ToString());
    }

    private static string Sanitize(string? assemblyName)
    {
        var name = string.IsNullOrEmpty(assemblyName) ? "Tarui" : assemblyName!;
        var builder = new StringBuilder(name.Length);
        foreach (var character in name)
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '_');
        }

        return builder.ToString();
    }

    private static string Escape(string value) => value
        .Replace("\\", "\\\\")
        .Replace("\"", "\\\"")
        .Replace("\r", "\\r")
        .Replace("\n", "\\n");

    private sealed class CommandDescriptor
    {
        public CommandDescriptor(
            string id,
            string input,
            string output,
            string permission,
            IReadOnlyList<TypeShape> types)
        {
            Id = id;
            Input = input;
            Output = output;
            Permission = permission;
            Types = types;
        }

        public string Id { get; }

        public string Input { get; }

        public string Output { get; }

        public string Permission { get; }

        public IReadOnlyList<TypeShape> Types { get; }
    }
}
