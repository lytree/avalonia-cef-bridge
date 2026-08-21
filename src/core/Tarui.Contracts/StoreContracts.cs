namespace Tarui.Contracts;

/// <summary>
/// Selects a JSON store file: <see cref="Base"/> names a known base directory (defaults to
/// <c>appData</c>) and <see cref="Path"/> is the store file relative to that base (defaults to
/// <c>settings.json</c>). The path is validated by <c>IFileAccessPolicy</c> before any disk access.
/// </summary>
public sealed record StoreFileOptions(string Base = "appData", string? Path = "settings.json");

/// <summary>A single key lookup or mutation on a JSON store file.</summary>
public sealed record StoreKeyOptions(string Key, string Base = "appData", string? Path = "settings.json");

/// <summary>Writes <see cref="Value"/> at <see cref="Key"/> on a JSON store file. A <see langword="null"/>
/// value removes the key, matching Tauri's <c>store</c> plugin erase semantics.</summary>
public sealed record StoreSetOptions(string Key, string? Value, string Base = "appData", string? Path = "settings.json");

/// <summary>Result of <c>plugin:store|get</c>; a missing key yields <see langword="null"/>.</summary>
public sealed record StoreGetResult(string? Value);

public sealed record StoreHasResult(bool Has);

public sealed record StoreKeysResult(string[] Keys);