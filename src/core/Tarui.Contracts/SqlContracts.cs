namespace Tarui.Contracts;

/// <summary>
/// Opens a database handle. <see cref="Connection"/> is a connection string whose scheme selects
/// the driver: <c>sqlite://&lt;path&gt;</c>, <c>sqlite::memory:</c>, or <c>postgres://</c> and
/// <c>mysql://</c> when those drivers are registered. Only SQLite ships in the box; the scheme is
/// resolved by <c>ISqlDriverResolver</c> so an application can register additional drivers
/// explicitly.
///
/// <para>
/// The <c>sqlite://</c> path is resolved through <c>IFileAccessPolicy</c> and is additionally
/// constrained by the <c>sql:allow-load</c> scope authorizer, so a capability must name the
/// database file in its scope list before any handle is opened.
/// </para>
/// </summary>
public sealed record SqlLoadOptions(string Connection);

/// <summary>
/// A single statement execution. <see cref="Db"/> is the handle returned by
/// <c>plugin:sql|load</c>. <see cref="Query"/> carries the SQL text.
/// <see cref="Values"/> supplies positional bind parameters in <c>?</c> order; values are bound
/// through the driver parameter API, never string-concatenated.
/// </summary>
public sealed record SqlExecuteOptions(string Db, string Query, SqlValue[]? Values = null);

/// <summary>Result of <c>plugin:sql|select</c>: the returned rows in column order.</summary>
public sealed record SqlSelectResult(SqlRow[] Rows);

/// <summary>One result row. Cells are positional and map to the query's select list order.</summary>
public sealed record SqlRow(SqlValue[] Cells);

/// <summary>
/// A single SQL cell. SQLite is dynamically typed, so a cell carries both a JSON-encodable value
/// and the storage class that produced it (<c>null</c>, <c>integer</c>, <c>real</c>, <c>text</c>
/// or <c>blob</c>). <see cref="Value"/> holds integers, doubles and strings directly; blobs are
/// base64-encoded into <see cref="Value"/> because the wire format is JSON, and
/// <see cref="IsNull"/> distinguishes SQL NULL from an empty string or a zero.
/// </summary>
public sealed record SqlValue(string? Value, string Type, bool IsNull = false)
{
    /// <summary>A SQL NULL cell.</summary>
    public static SqlValue Null { get; } = new(null, "null", IsNull: true);

    public static SqlValue FromInt(long value) => new(value.ToString(System.Globalization.CultureInfo.InvariantCulture), "integer");

    public static SqlValue FromReal(double value) => new(value.ToString("R", System.Globalization.CultureInfo.InvariantCulture), "real");

    public static SqlValue FromText(string value) => new(value, "text");

    public static SqlValue FromBlob(byte[] value) => new(Convert.ToBase64String(value), "blob");
}

/// <summary>Result of <c>plugin:sql|execute</c>: rows affected and the last insert row id.</summary>
public sealed record SqlExecuteResult(long RowsAffected, long LastInsertId);

/// <summary>
/// Result of <c>plugin:sql|close</c>. <see cref="Closed"/> is <see langword="false"/> when the
/// handle was already closed or never existed, which makes the call idempotent and lets the
/// renderer clean up in a finally block without tracking state.
/// </summary>
public sealed record SqlCloseResult(bool Closed);

/// <summary>
/// Source-generated JSON metadata for SQL cells. Cells are stored as string payloads plus a type
/// tag rather than as a polymorphic object graph, which keeps the contract representable in
/// source-generated metadata and avoids runtime type resolution in the renderer.
/// </summary>
[System.Text.Json.Serialization.JsonSourceGenerationOptions(
    PropertyNamingPolicy = System.Text.Json.Serialization.JsonKnownNamingPolicy.Unspecified,
    GenerationMode = System.Text.Json.Serialization.JsonSourceGenerationMode.Metadata)]
[System.Text.Json.Serialization.JsonSerializable(typeof(SqlValue))]
[System.Text.Json.Serialization.JsonSerializable(typeof(SqlValue[]))]
[System.Text.Json.Serialization.JsonSerializable(typeof(SqlRow))]
[System.Text.Json.Serialization.JsonSerializable(typeof(SqlRow[]))]
public partial class SqlValueJsonContext : System.Text.Json.Serialization.JsonSerializerContext;