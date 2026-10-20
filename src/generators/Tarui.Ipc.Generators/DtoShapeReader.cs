using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Tarui.Ipc.Generators;

/// <summary>
/// Resolves the JSON shape of a command's argument/result type into TypeScript, mirroring the
/// serializer settings declared on <c>TaruiJsonContext</c> (<c>PropertyNamingPolicy = CamelCase</c>,
/// <c>DefaultIgnoreCondition = WhenWritingNull</c>). It only reads syntax/semantic information that is
/// already part of the compilation — no reflection, no assembly scanning — so the generator stays
/// incremental.
/// </summary>
internal sealed class DtoShapeReader
{
    private const string JsonIgnoreAttribute = "System.Text.Json.Serialization.JsonIgnoreAttribute";
    private const string JsonPropertyNameAttribute = "System.Text.Json.Serialization.JsonPropertyNameAttribute";
    private const string JsonExtensionDataAttribute = "System.Text.Json.Serialization.JsonExtensionDataAttribute";

    /// <summary>Guards against pathological graphs; cycles are already handled by <see cref="_visited"/>.</summary>
    private const int MaxTypesPerCommand = 256;

    private readonly Dictionary<string, TypeShape> _types = new(StringComparer.Ordinal);
    private readonly HashSet<string> _visited = new(StringComparer.Ordinal);

    /// <summary>Every type discovered so far, sorted by catalog member name.</summary>
    public IReadOnlyList<TypeShape> Types => _types.Values
        .OrderBy(static type => type.Name, StringComparer.Ordinal)
        .ToArray();

    /// <summary>
    /// Registers the type behind a <c>TaruiJsonContext.Default.{Name}</c> argument and returns its
    /// contract, or <c>null</c> when the argument does not resolve to a <c>JsonTypeInfo&lt;T&gt;</c>.
    /// </summary>
    public TypeShape? Describe(string memberName, ITypeSymbol? payloadType)
    {
        if (payloadType is null)
        {
            return null;
        }

        // Mapping first registers every object DTO it walks through, so the member name below only
        // needs to record the type expression the command actually references.
        var expression = MapType(StripNullable(payloadType));
        var contract = new TypeShape(memberName, expression);

        // Two assemblies can name the same DTO (a plugin may re-register a contract type); the first
        // resolution wins and must stay identical, otherwise the generated declaration would flip-flop.
        if (!_types.TryGetValue(memberName, out var existing))
        {
            _types.Add(memberName, contract);
            return contract;
        }

        return existing;
    }

    private string MapType(ITypeSymbol type)
    {
        type = StripNullable(type);

        switch (type.SpecialType)
        {
            case SpecialType.System_String:
                return "string";
            case SpecialType.System_Boolean:
                return "boolean";
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
            case SpecialType.System_Byte:
            case SpecialType.System_SByte:
            case SpecialType.System_Single:
            case SpecialType.System_Double:
            case SpecialType.System_Decimal:
                return "number";
            case SpecialType.System_Object:
                return "unknown";
            case SpecialType.System_Char:
                return "string";
        }

        if (type is IArrayTypeSymbol array)
        {
            // System.Text.Json writes byte[] as base64, not as an array of numbers.
            return array.ElementType.SpecialType == SpecialType.System_Byte
                ? "string"
                : MapType(array.ElementType) + "[]";
        }

        if (type is not INamedTypeSymbol named)
        {
            return "unknown";
        }

        var definition = named.OriginalDefinition.ToDisplayString();
        if (named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T && named.TypeArguments.Length == 1)
        {
            return MapType(named.TypeArguments[0]) + " | null";
        }

        switch (definition)
        {
            case "System.Collections.Generic.List<T>":
            case "System.Collections.Generic.IList<T>":
            case "System.Collections.Generic.IReadOnlyList<T>":
            case "System.Collections.Generic.ICollection<T>":
            case "System.Collections.Generic.IReadOnlyCollection<T>":
            case "System.Collections.Generic.IEnumerable<T>":
            case "System.Collections.Immutable.ImmutableArray<T>":
                return MapType(named.TypeArguments[0]) + "[]";

            case "System.Collections.Generic.Dictionary<TKey, TValue>":
            case "System.Collections.Generic.IDictionary<TKey, TValue>":
            case "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>":
                return $"Record<string, {MapType(named.TypeArguments[1])}>";

            // Arbitrary JSON passes through untouched; "unknown" keeps the declaration honest.
            case "System.Text.Json.JsonElement":
            case "System.Text.Json.JsonDocument":
            case "System.Text.Json.Nodes.JsonNode":
            case "System.Text.Json.Nodes.JsonObject":
            case "System.Text.Json.Nodes.JsonArray":
                return "unknown";

            case "System.DateTime":
            case "System.DateTimeOffset":
            case "System.TimeSpan":
            case "System.Guid":
            case "System.Uri":
                return "string";
        }

        if (named.TypeKind == TypeKind.Enum)
        {
            // No JsonStringEnumConverter is registered on TaruiJsonContext, so enums travel as numbers.
            return "number";
        }

        if (named.TypeArguments.Length > 0)
        {
            // An unmodelled generic (a custom wrapper) has no faithful TypeScript spelling yet.
            return "unknown";
        }

        return MapObject(named);
    }

    /// <summary>Registers an object DTO as a TypeScript interface and returns its declaration name.</summary>
    private string MapObject(INamedTypeSymbol type)
    {
        var name = TypeScriptName(type);
        var key = type.ToDisplayString();

        if (!_visited.Add(key))
        {
            return name;
        }

        if (_types.Count >= MaxTypesPerCommand)
        {
            return name;
        }

        var members = new List<string>();
        foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
        {
            if (property.IsStatic || property.IsIndexer || property.DeclaredAccessibility != Accessibility.Public)
            {
                continue;
            }

            if (HasAttribute(property, JsonIgnoreAttribute) || HasAttribute(property, JsonExtensionDataAttribute))
            {
                continue;
            }

            var propertyType = property.Type;
            var optional = propertyType.NullableAnnotation == NullableAnnotation.Annotated ||
                IsNullableValueType(propertyType);
            var mapped = MapType(propertyType);
            var jsonName = JsonName(property);

            members.Add($"readonly {jsonName}{(optional ? "?" : string.Empty)}: {mapped};");
        }

        // A name already taken by another type keeps its first description; the declaration name is
        // then ambiguous, which the bindings gate reports as a conflict instead of silently merging.
        var body = string.Join("\n", members);
        if (!_types.ContainsKey(name))
        {
            _types.Add(name, new TypeShape(name, name, body));
        }

        return name;
    }

    private static string TypeScriptName(INamedTypeSymbol type) => type.Name;

    private static string JsonName(IPropertySymbol property)
    {
        foreach (var attribute in property.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != JsonPropertyNameAttribute ||
                attribute.ConstructorArguments.Length != 1)
            {
                continue;
            }

            if (attribute.ConstructorArguments[0].Value is string explicitName && explicitName.Length > 0)
            {
                return explicitName;
            }
        }

        return CamelCase(property.Name);
    }

    /// <summary>Mirrors <see cref="System.Text.Json.JsonNamingPolicy.CamelCase"/>: lowercases a leading acronym.</summary>
    private static string CamelCase(string name)
    {
        if (name.Length == 0 || !char.IsUpper(name[0]))
        {
            return name;
        }

        var builder = new StringBuilder(name);
        var index = 0;
        while (index < builder.Length)
        {
            if (!char.IsUpper(builder[index]))
            {
                break;
            }

            var isLastCharacter = index == builder.Length - 1;
            if (!isLastCharacter && index + 1 < builder.Length && !char.IsUpper(builder[index + 1]))
            {
                if (index > 0)
                {
                    break;
                }
            }

            builder[index] = char.ToLowerInvariant(builder[index]);
            index++;
        }

        return builder.ToString();
    }

    private static bool IsNullableValueType(ITypeSymbol type) =>
        type is INamedTypeSymbol named &&
        named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;

    private static ITypeSymbol StripNullable(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol named &&
            named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
            named.TypeArguments.Length == 1)
        {
            return named.TypeArguments[0];
        }

        return type.NullableAnnotation == NullableAnnotation.Annotated
            ? type.WithNullableAnnotation(NullableAnnotation.NotAnnotated)
            : type;
    }

    private static bool HasAttribute(ISymbol symbol, string attributeMetadataName) =>
        symbol.GetAttributes().Any(attribute =>
            attribute.AttributeClass?.ToDisplayString() == attributeMetadataName);
}

/// <summary>
/// One resolved type shape. It mirrors <c>Tarui.Contracts.TaruiTypeContract</c> structurally so the
/// analyzer assembly carries no runtime dependency; the generated catalog instantiates the contract.
/// </summary>
internal sealed class TypeShape
{
    public TypeShape(string name, string typeExpression, string body = "")
    {
        Name = name;
        TypeExpression = typeExpression;
        Body = body;
    }

    public string Name { get; }

    public string TypeExpression { get; }

    public string Body { get; }
}
