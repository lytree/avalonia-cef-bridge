using System.Data;
using System.Data.Common;
using Tarui.Contracts;
using Tarui.Ipc;

namespace Tarui.Plugins.Sql;

/// <summary>
/// Default SQL service. <c>load</c> resolves the connection string through the registered
/// <see cref="ISqlDriverResolver"/> chain and returns an opaque handle id; the renderer addresses
/// that handle for every later <c>select</c> / <c>execute</c> / <c>close</c>.
///
/// <para>
/// Concurrency: a handle owns a <see cref="SemaphoreSlim"/> so statements on the same database are
/// serialized. SQLite allows one writer at a time and the provider throws
/// <c>SQLITE_BUSY</c> when writers overlap; serializing per handle turns that into ordinary
/// queuing instead of a surfacing error. Statements on different handles still run in parallel.
/// </para>
/// </summary>
public sealed class SqlService : ISqlService, IAsyncDisposable
{
    /// <summary>
    /// The default base directory that holds database files. Databases are writable application
    /// artifacts, so they live beside other app-owned state under <c>appData</c>.
    /// </summary>
    public const string DefaultSqlDatabaseBase = "appData";

    private readonly ISqlDriverResolver[] _drivers;
    private readonly IFileAccessPolicy _filePolicy;
    private readonly string _databaseBase;
    private readonly object _gate = new();
    private readonly Dictionary<string, SqlHandle> _handles = new(StringComparer.Ordinal);
    private int _handleCounter;
    private bool _disposed;

    public SqlService(
        IEnumerable<ISqlDriverResolver> drivers,
        IFileAccessPolicy filePolicy,
        string databaseBase = DefaultSqlDatabaseBase)
    {
        _drivers = drivers.ToArray();
        if (_drivers.Length == 0)
        {
            throw new InvalidOperationException("No SQL driver is registered. Call AddSqlPlugin() which registers the built-in SQLite driver.");
        }

        _filePolicy = filePolicy;
        _databaseBase = databaseBase;
    }

    /// <summary>The base directory that database paths resolve against.</summary>
    public string DatabaseBase => _databaseBase;

    public ValueTask<string> LoadAsync(SqlLoadOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.Connection))
        {
            throw new InvalidOperationException("A non-empty connection string is required.");
        }

        var driver = ResolveDriver(options.Connection);
        var createIfMissing = options.AutoCreate ?? true;
        var connectionString = ResolveConnectionString(options.Connection, out var absolutePath);
        if (!createIfMissing &&
            absolutePath is not null &&
            !File.Exists(absolutePath))
        {
            // Tauri's autoCreate: false turns "database missing" into an error. Rejecting here keeps
            // the message actionable instead of surfacing the provider's "unable to open database".
            throw new InvalidOperationException(
                $"Database file '{absolutePath}' does not exist and autoCreate is false.");
        }

        var (connection, kind) = driver is ISqlCreateModeDriver createModeDriver
            ? createModeDriver.Open(connectionString, createIfMissing)
            : driver.Open(connectionString);

        var handle = new SqlHandle(connection, kind);
        string id;
        lock (_gate)
        {
            id = $"db{++_handleCounter}";
            _handles[id] = handle;
        }

        return ValueTask.FromResult(id);
    }

    public async ValueTask<SqlSelectResult> SelectAsync(SqlExecuteOptions options, CancellationToken cancellationToken)
    {
        var handle = Resolve(options.Db);
        var rows = await handle.Database.QueryAsync(options.Query, options.Values, cancellationToken).ConfigureAwait(false);
        return new SqlSelectResult(rows);
    }

    public async ValueTask<SqlExecuteResult> ExecuteAsync(SqlExecuteOptions options, CancellationToken cancellationToken)
    {
        var handle = Resolve(options.Db);
        var (rowsAffected, lastInsertId) = await handle.Database
            .ExecuteAsync(options.Query, options.Values, cancellationToken).ConfigureAwait(false);
        return new SqlExecuteResult(rowsAffected, lastInsertId);
    }

    public async ValueTask<SqlCloseResult> CloseAsync(SqlExecuteOptions options, CancellationToken cancellationToken)
    {
        SqlHandle? handle;
        lock (_gate)
        {
            if (!_handles.Remove(options.Db, out handle))
            {
                return new SqlCloseResult(false);
            }
        }

        await handle.DisposeAsync().ConfigureAwait(false);
        return new SqlCloseResult(true);
    }

    public async ValueTask DisposeAsync()
    {
        SqlHandle[] handles;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            handles = [.. _handles.Values];
            _handles.Clear();
        }

        foreach (var handle in handles)
        {
            await handle.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Resolves the database file for a <c>sqlite://</c> connection string through
    /// <see cref="IFileAccessPolicy"/>, so a capability scope (and not just the permission) must
    /// name the file before the database is created. A database is a writable application artifact
    /// and belongs under <c>appData</c>; rooted paths and escapes are rejected by the policy.
    /// </summary>
    private string ResolveConnectionString(string connectionString, out string? absolutePath)
    {
        absolutePath = null;
        if (SqliteConnectionString.IsInMemory(connectionString))
        {
            // In-memory databases hold no path and therefore no scope concern.
            return connectionString;
        }

        var relative = SqliteConnectionString.ExtractRelativePath(connectionString);
        if (!_filePolicy.TryGetBaseDirectory(_databaseBase, out var baseDirectory, out var isReadOnly))
        {
            throw new InvalidOperationException($"Base directory '{_databaseBase}' is not available on this system.");
        }

        if (isReadOnly)
        {
            throw new PathAccessDeniedException(PathDenialReason.OutsideBase,
                $"Base directory '{_databaseBase}' is read-only and cannot hold a database file.");
        }

        Directory.CreateDirectory(baseDirectory);

        // Authorize rejects a rooted path, a path escaping the base via .., and symlink escapes,
        // so a capability cannot reach a database file outside appData.
        var absolute = _filePolicy.Authorize(FileAccessKind.Write, baseDirectory, relative);
        absolutePath = absolute;
        return $"sqlite://{absolute}";
    }

    private ISqlDriverResolver ResolveDriver(string connectionString)
    {
        foreach (var driver in _drivers)
        {
            if (driver.CanHandle(connectionString))
            {
                return driver;
            }
        }

        var scheme = connectionString.Contains("://", StringComparison.Ordinal)
            ? connectionString[..connectionString.IndexOf("://", StringComparison.Ordinal)]
            : SqliteDriver.Scheme.TrimEnd(':');
        throw new InvalidOperationException($"No SQL driver is registered for scheme '{scheme}'.");
    }

    private SqlHandle Resolve(string id)
    {
        lock (_gate)
        {
            if (!_handles.TryGetValue(id, out var handle))
            {
                throw new InvalidOperationException($"Database handle '{id}' is not open. Call plugin:sql|load first.");
            }

            return handle;
        }
    }

    /// <summary>
    /// A loaded database plus the per-handle statement lock. A <see cref="SqlOpenKind.Shared"/>
    /// database is opened once and reused by every handle that names the same in-memory
    /// connection string, because closing the only connection would destroy the schema.
    /// </summary>
    private sealed class SqlHandle(DbConnection connection, SqlOpenKind kind) : IAsyncDisposable
    {
        public SqliteDatabase Database { get; } = new(connection, kind);

        public ValueTask DisposeAsync() => Database.DisposeAsync();
    }
}

/// <summary>
/// Ado.NET-backed database. Shared connections are guarded because two handles may reference the
/// same in-memory database; per-handle connections still serialize per handle because SQLite
/// permits a single writer.
/// </summary>
internal sealed class SqliteDatabase : ISqlDatabase
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> SharedGates = new(StringComparer.Ordinal);

    private readonly DbConnection _connection;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public SqliteDatabase(DbConnection connection, SqlOpenKind kind)
    {
        _connection = connection;
        if (kind == SqlOpenKind.Shared)
        {
            // All shared connections serialize on one process-wide gate.
            _gate = SharedGates.GetOrAdd("shared", static _ => new SemaphoreSlim(1, 1));
        }
    }

    public async ValueTask<SqlRow[]> QueryAsync(string query, SqlValue[]? values, CancellationToken cancellationToken)
    {
        Validate(query);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var command = CreateCommand(query, values);
            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult, cancellationToken).ConfigureAwait(false);

            var rows = new List<SqlRow>();
            var fieldCount = reader.FieldCount;
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var cells = new SqlValue[fieldCount];
                for (var i = 0; i < fieldCount; i++)
                {
                    cells[i] = Map(reader.GetValue(i));
                }

                rows.Add(new SqlRow(cells));
            }

            return [.. rows];
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<(long RowsAffected, long LastInsertId)> ExecuteAsync(string query, SqlValue[]? values, CancellationToken cancellationToken)
    {
        Validate(query);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var command = CreateCommand(query, values);
            var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            long lastInsertId = 0;
            if (affected > 0)
            {
                lastInsertId = await ReadLastInsertIdAsync(cancellationToken).ConfigureAwait(false);
            }

            return (affected, lastInsertId);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// SQLite exposes the last insert rowid through <c>last_insert_rowid()</c> on the same
    /// connection, which is only meaningful for SQLite. Other drivers have no equivalent here, so
    /// the value is reported as 0 rather than guessing.
    /// </summary>
    private async ValueTask<long> ReadLastInsertIdAsync(CancellationToken cancellationToken)
    {
        if (_connection is not Microsoft.Data.Sqlite.SqliteConnection)
        {
            return 0;
        }

        await using var command = _connection.CreateCommand();
        command.CommandText = "SELECT last_insert_rowid()";
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is long value ? value : 0;
    }

    private DbCommand CreateCommand(string query, SqlValue[]? values)
    {
        var command = _connection.CreateCommand();

        if (values is { Length: > 0 })
        {
            // Values are bound through the parameter collection. The query text is never
            // concatenated with caller data, so a value containing quotes or SQL keywords stays
            // data. The type tag drives the CLR binding type so SQLite does not coerce everything
            // to TEXT.
            for (var i = 0; i < values.Length; i++)
            {
                var value = values[i];
                var parameter = command.CreateParameter();

                // The provider requires an explicit name, so each positional `?` in the caller's
                // statement is rewritten to `:taruiN` and matched by the same name. A name is
                // mandatory here; SQLite's anonymous form cannot be bound through this API.
                parameter.ParameterName = $":tarui{i}";

                if (value.IsNull || value.Value is null)
                {
                    parameter.Value = DBNull.Value;
                }
                else
                {
                    parameter.Value = ToClrValue(value);
                }

                command.Parameters.Add(parameter);
            }
        }

        command.CommandText = RewritePositionalPlaceholders(query, values?.Length ?? 0);
        return command;
    }

    /// <summary>
    /// Rewrites the caller's anonymous <c>?</c> placeholders to <c>:tarui0</c>, <c>:tarui1</c>,
    /// ... so they line up with the named parameters in <see cref="CreateCommand"/>. String and
    /// identifier literals are skipped so a <c>?</c> inside quoted text is never treated as a
    /// placeholder, and a placeholder inside a comment is left alone.
    /// </summary>
    internal static string RewritePositionalPlaceholders(string query, int valueCount)
    {
        if (valueCount == 0 || !query.Contains('?', StringComparison.Ordinal))
        {
            return query;
        }

        var builder = new System.Text.StringBuilder(query.Length + (valueCount * 4));
        var index = 0;
        var inSingleQuote = false;
        var inDoubleQuote = false;
        var inLineComment = false;
        var inBlockComment = false;

        for (var i = 0; i < query.Length; i++)
        {
            var current = query[i];
            var next = i + 1 < query.Length ? query[i + 1] : '\0';

            if (inLineComment)
            {
                builder.Append(current);
                if (current is '\n')
                {
                    inLineComment = false;
                }

                continue;
            }

            if (inBlockComment)
            {
                builder.Append(current);
                if (current == '*' && next == '/')
                {
                    builder.Append(next);
                    i++;
                    inBlockComment = false;
                }

                continue;
            }

            if (!inSingleQuote && !inDoubleQuote)
            {
                if (current == '-' && next == '-')
                {
                    builder.Append(current).Append(next);
                    i++;
                    inLineComment = true;
                    continue;
                }

                if (current == '/' && next == '*')
                {
                    builder.Append(current).Append(next);
                    i++;
                    inBlockComment = true;
                    continue;
                }

                if (current == '?')
                {
                    builder.Append(":tarui").Append(index++);
                    continue;
                }
            }

            // Track quoting so a `?` inside a literal is not rewritten. A doubled quote inside a
            // literal is an escaped quote, not the end of the literal.
            if (current == '\'' && !inDoubleQuote)
            {
                if (inSingleQuote && next == '\'')
                {
                    builder.Append(current).Append(next);
                    i++;
                    continue;
                }

                inSingleQuote = !inSingleQuote;
            }
            else if (current == '"' && !inSingleQuote)
            {
                inDoubleQuote = !inDoubleQuote;
            }

            builder.Append(current);
        }

        return builder.ToString();
    }

    private static object ToClrValue(SqlValue value) => value.Type switch
    {
        "integer" => long.TryParse(value.Value, out var integer) ? integer : 0L,
        "real" => double.TryParse(value.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var real) ? real : 0d,
        "blob" => SafeFromBase64(value.Value),
        _ => value.Value ?? string.Empty
    };

    private static byte[] SafeFromBase64(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return [];
        }

        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            return [];
        }
    }

    /// <summary>
    /// Maps one reader value onto the wire cell contract, preserving the storage class so the
    /// renderer can tell SQL NULL from an empty string and integers from reals.
    /// </summary>
    private static SqlValue Map(object? value) => value switch
    {
        null or DBNull => SqlValue.Null,
        long integer => SqlValue.FromInt(integer),
        int integer => SqlValue.FromInt(integer),
        bool boolean => SqlValue.FromInt(boolean ? 1 : 0),
        double real => SqlValue.FromReal(real),
        float real => SqlValue.FromReal(real),
        decimal number => SqlValue.FromReal((double)number),
        byte[] blob => SqlValue.FromBlob(blob),
        DateTime moment => SqlValue.FromText(moment.ToString("O", System.Globalization.CultureInfo.InvariantCulture)),
        _ => SqlValue.FromText(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty)
    };

    private static void Validate(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new InvalidOperationException("A non-empty SQL statement is required.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _connection.DisposeAsync().ConfigureAwait(false);
    }
}