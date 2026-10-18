namespace Tarui.Contracts;

/// <summary>
/// The compile-time contract of one routed command: its id plus the DTO type names of its argument and
/// result. Emitted by <c>Tarui.Ipc.Generators</c> into a per-assembly {Assembly}CommandCatalog so the
/// hand-written front-end bindings can be diffed against it (see tests/Tarui.Bindings.Tests). The
/// permission id is intentionally omitted — it stays a runtime fact of the command router.
/// </summary>
public sealed record TaruiCommandContract(string Id, string Input, string Output);
