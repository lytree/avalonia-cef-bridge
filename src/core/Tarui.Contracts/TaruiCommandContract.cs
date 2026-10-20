namespace Tarui.Contracts;

/// <summary>
/// The compile-time contract of one routed command: its id, the DTO type names of its argument and
/// result, and the permission that gates it. Emitted by <c>Tarui.Ipc.Generators</c> into a
/// per-assembly {Assembly}CommandCatalog so the hand-written front-end bindings can be diffed
/// against it (see tests/Tarui.Bindings.Tests) and so the generated TypeScript declaration stays in
/// step with the native command surface.
/// </summary>
public sealed record TaruiCommandContract(string Id, string Input, string Output, string Permission = "");

/// <summary>
/// The compile-time JSON shape of one command argument/result type, as the front-end sees it. The
/// command contract only names a DTO (<c>FsDirEntryArray</c>); this record carries the resolved
/// TypeScript expression (<c>FsDirEntry[]</c>) plus, for object DTOs, the member list so the
/// generated declaration can spell the fields out instead of stopping at the type name.
/// <para>
/// <c>Name</c> is the catalog member name the command references (the generated
/// <c>TaruiJsonContext.Default.{Name}</c> property), <c>TypeExpression</c> is the TypeScript type it
/// resolves to, and <c>Body</c> holds one interface member per line (empty for scalars, arrays,
/// dictionaries and the empty payload types).
/// </para>
/// </summary>
public sealed record TaruiTypeContract(string Name, string TypeExpression, string Body = "");
