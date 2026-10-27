using System.Data.Common;
using Tarui.Contracts;
using Tarui.Ipc;

namespace Tarui.Plugins.Sql;

/// <summary>
/// Executes SQL against a database and returns rows. Handles are opened by
/// <c>plugin:sql|load</c> and addressed by the returned identifier; the service owns the handle
/// lifetime so the renderer never touches a raw <see cref="DbConnection"/>.
/// </summary>
public interface ISqlService
{
    ValueTask<string> LoadAsync(SqlLoadOptions options, CancellationToken cancellationToken);

    ValueTask<SqlSelectResult> SelectAsync(SqlExecuteOptions options, CancellationToken cancellationToken);

    ValueTask<SqlExecuteResult> ExecuteAsync(SqlExecuteOptions options, CancellationToken cancellationToken);

    ValueTask<SqlCloseResult> CloseAsync(SqlExecuteOptions options, CancellationToken cancellationToken);
}

/// <summary>
/// An open database. <see cref="Reader"/> runs parameterized queries, <see cref="Writer"/> runs
/// statements that do not return rows.
/// </summary>
public interface ISqlDatabase : IAsyncDisposable
{
    ValueTask<SqlRow[]> QueryAsync(string query, SqlValue[]? values, CancellationToken cancellationToken);

    ValueTask<(long RowsAffected, long LastInsertId)> ExecuteAsync(string query, SqlValue[]? values, CancellationToken cancellationToken);
}

/// <summary>
/// Resolves a connection-string scheme to a driver. SQLite ships in the box; an application
/// registers PostgreSQL or MySQL explicitly through
/// <see cref="SqlPluginServiceCollectionExtensions.AddSqlDriver"/>, which keeps the default
/// surface small and avoids an implicit dependency on a database the application never uses.
/// </summary>
public interface ISqlDriverResolver
{
    /// <summary>Whether this resolver handles the scheme of <paramref name="connectionString"/>.</summary>
    bool CanHandle(string connectionString);

    /// <summary>
    /// Opens a connection. The returned <see cref="SqlOpenKind"/> tells the service whether the
    /// handle is a shared singleton (an in-memory database) or a private per-handle connection.
    /// </summary>
    (DbConnection Connection, SqlOpenKind Kind) Open(string connectionString);
}

/// <summary>
/// Whether a handle is shared across every <c>load</c> call. SQLite <c>:memory:</c> databases are
/// destroyed when their last connection closes, so the service keeps one live connection and
/// multiplexes every statement over it. File-backed and network databases open a fresh connection
/// per handle, which keeps SQLite's write locking and connection pooling behaving normally.
/// </summary>
public enum SqlOpenKind
{
    /// <summary>Each handle owns a private connection that closes with the handle.</summary>
    PerHandle,

    /// <summary>Every handle shares one process-wide connection.</summary>
    Shared
}

/// <summary>
/// Optional driver capability for drivers that can create their database on first open. Tauri's
/// sql 2.6.0 added <c>LoadOptions.autoCreate</c> (default <c>true</c>); SQLite is the only driver
/// where "create the database" is a file operation, so only it implements this. Other drivers keep
/// their existing <see cref="ISqlDriverResolver.Open"/> behavior and ignore the flag.
/// </summary>
public interface ISqlCreateModeDriver
{
    /// <summary>Opens a connection, creating the database only when <paramref name="createIfMissing"/> is set.</summary>
    (DbConnection Connection, SqlOpenKind Kind) Open(string connectionString, bool createIfMissing);
}

/// <summary>SQLite-flavoured driver built on <c>Microsoft.Data.Sqlite</c>.</summary>
public sealed class SqliteDriver : ISqlDriverResolver, ISqlCreateModeDriver
{
    public const string Scheme = "sqlite:";

    public bool CanHandle(string connectionString) =>
        connectionString.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase);

    public (DbConnection Connection, SqlOpenKind Kind) Open(string connectionString) =>
        Open(connectionString, createIfMissing: true);

    public (DbConnection Connection, SqlOpenKind Kind) Open(string connectionString, bool createIfMissing)
    {
        var connection = new Microsoft.Data.Sqlite.SqliteConnection(
            SqliteConnectionString.Normalize(connectionString, createIfMissing));
        connection.Open();

        // An in-memory database lives and dies with its connection, so it must be shared or the
        // first handle to close would silently drop the schema for every later statement.
        var isMemory = SqliteConnectionString.IsInMemory(connectionString);
        return (connection, isMemory ? SqlOpenKind.Shared : SqlOpenKind.PerHandle);
    }
}

/// <summary>Normalizes the <c>sqlite://</c> connection-string forms accepted by <c>plugin:sql|load</c>.</summary>
public static class SqliteConnectionString
{
    public const string MemoryPrefix = "sqlite::memory:";

    /// <summary>
    /// Whether the connection string names an in-memory database. Accepts <c>sqlite::memory:</c>
    /// and <c>sqlite://:memory:</c>; the mode is case-insensitive.
    /// </summary>
    public static bool IsInMemory(string connectionString)
    {
        var tail = connectionString[SqliteDriver.Scheme.Length..];
        var trimmed = tail.TrimStart(':', '/', ' ');
        return trimmed.Length == 0 ||
            trimmed.StartsWith("memory:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals(":memory:", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Extracts the database path from a <c>sqlite://</c> connection string. The path is treated
    /// as relative to the database base directory; it is not yet trusted and must pass
    /// <c>IFileAccessPolicy.Authorize</c>.
    /// </summary>
    public static string ExtractRelativePath(string connectionString)
    {
        var tail = connectionString[SqliteDriver.Scheme.Length..].TrimStart(':', '/', ' ');
        return tail;
    }

    /// <summary>
    /// Rewrites a user connection string into a <c>Data Source</c> the ADO.NET provider accepts.
    /// <c>sqlite://app.db</c> yields <c>Data Source=app.db;Mode=ReadWriteCreate</c>; an in-memory form
    /// yields <c>Data Source=:memory:</c>. When <paramref name="createIfMissing"/> is
    /// <see langword="false"/> the file mode becomes <c>ReadWrite</c>, so opening a missing database
    /// fails instead of silently creating an empty one (Tauri's <c>autoCreate: false</c>).
    /// </summary>
    public static string Normalize(string connectionString, bool createIfMissing = true)
    {
        if (IsInMemory(connectionString))
        {
            return "Data Source=:memory:";
        }

        var mode = createIfMissing ? "ReadWriteCreate" : "ReadWrite";
        return $"Data Source={ExtractRelativePath(connectionString)};Mode={mode}";
    }
}