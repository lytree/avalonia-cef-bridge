using Tarui.Contracts;
using Tarui.Ipc;
using Tarui.Plugins.Sql;

namespace Tarui.Sql.Tests;

internal static class Program
{
    public static async Task<int> Main()
    {
        try
        {
            await CreateTableInsertSelectRoundTripAsync();
            await ParameterBindingKeepsValuesAsDataAsync();
            await NullIsDistinctFromEmptyStringAsync();
            await ColumnTypesArePreservedAsync();
            await LastInsertIdIsReportedAsync();
            await UnknownHandleIsRejectedAsync();
            await CloseIsIdempotentAsync();
            await FileDatabasePersistsAcrossServicesAsync();
            await PathTraversalIsRejectedAsync();
            await InMemoryNeedsNoDiskFileAsync();
            await AutoCreateFalseRejectsMissingDatabaseAsync();
            await AutoCreateFalseOpensExistingDatabaseAsync();
            ScopeAuthorizerRespectsAllowDenyAndWildcards();
            PluginRegistersAllFourCommands();
            ConnectionStringHelpersClassifySchemes();
            PlaceholderRewriteSkipsLiteralsAndComments();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.ToString());
            return 1;
        }

        Console.WriteLine("Tarui.Sql self-tests passed.");
        return 0;
    }

    private static async Task CreateTableInsertSelectRoundTripAsync()
    {
        using var root = CreateTempRoot();
        await using var service = CreateService(root);

        var db = await service.LoadAsync(new SqlLoadOptions("sqlite::memory:"), default);
        await service.ExecuteAsync(new SqlExecuteOptions(db,
            "CREATE TABLE notes (id INTEGER PRIMARY KEY, title TEXT NOT NULL)"), default);
        await service.ExecuteAsync(new SqlExecuteOptions(db,
            "INSERT INTO notes (title) VALUES (?)", [SqlValue.FromText("first")]), default);

        var selected = await service.SelectAsync(new SqlExecuteOptions(db, "SELECT title FROM notes"), default);

        Assert(selected.Rows.Length == 1, "Insert then Select must return exactly one row.");
        Assert(selected.Rows[0].Cells[0].Value == "first", "Select must return the inserted title.");
        Assert(selected.Rows[0].Cells[0].Type == "text", "A TEXT column must report the text storage class.");
    }

    private static async Task ParameterBindingKeepsValuesAsDataAsync()
    {
        using var root = CreateTempRoot();
        await using var service = CreateService(root);

        var db = await service.LoadAsync(new SqlLoadOptions("sqlite::memory:"), default);
        await service.ExecuteAsync(new SqlExecuteOptions(db,
            "CREATE TABLE t (v TEXT)"), default);

        // A value that would be destructive if concatenated into the statement. It must be bound
        // as data and stored verbatim, which is the property that makes parameterized SQL safe.
        var hostile = "'; DROP TABLE t; --";
        await service.ExecuteAsync(new SqlExecuteOptions(db,
            "INSERT INTO t (v) VALUES (?)", [SqlValue.FromText(hostile)]), default);

        var selected = await service.SelectAsync(new SqlExecuteOptions(db, "SELECT v FROM t"), default);

        Assert(selected.Rows.Length == 1, "The table must still exist after binding a quote-heavy value.");
        Assert(selected.Rows[0].Cells[0].Value == hostile, "A bound value must be stored verbatim, never interpreted as SQL.");
    }

    private static async Task NullIsDistinctFromEmptyStringAsync()
    {
        using var root = CreateTempRoot();
        await using var service = CreateService(root);

        var db = await service.LoadAsync(new SqlLoadOptions("sqlite::memory:"), default);
        await service.ExecuteAsync(new SqlExecuteOptions(db, "CREATE TABLE t (v TEXT)"), default);
        await service.ExecuteAsync(new SqlExecuteOptions(db, "INSERT INTO t VALUES (?)", [SqlValue.Null]), default);
        await service.ExecuteAsync(new SqlExecuteOptions(db, "INSERT INTO t VALUES (?)", [SqlValue.FromText(string.Empty)]), default);

        var selected = await service.SelectAsync(new SqlExecuteOptions(db, "SELECT v FROM t ORDER BY rowid"), default);

        Assert(selected.Rows.Length == 2, "Both a NULL and an empty string must be stored.");
        Assert(selected.Rows[0].Cells[0].IsNull, "A SQL NULL must be reported with IsNull so the renderer can distinguish it from an empty string.");
        Assert(!selected.Rows[1].Cells[0].IsNull, "An empty string is not NULL and must not be reported as such.");
    }

    private static async Task ColumnTypesArePreservedAsync()
    {
        using var root = CreateTempRoot();
        await using var service = CreateService(root);

        var db = await service.LoadAsync(new SqlLoadOptions("sqlite::memory:"), default);
        await service.ExecuteAsync(new SqlExecuteOptions(db,
            "CREATE TABLE t (i INTEGER, r REAL, b BLOB, s TEXT)"), default);
        await service.ExecuteAsync(new SqlExecuteOptions(db,
            "INSERT INTO t VALUES (?, ?, ?, ?)",
            [
                SqlValue.FromInt(42),
                SqlValue.FromReal(1.5),
                SqlValue.FromBlob([1, 2, 3]),
                SqlValue.FromText("text")
            ]), default);

        var selected = await service.SelectAsync(new SqlExecuteOptions(db, "SELECT i, r, b, s FROM t"), default);
        var cells = selected.Rows[0].Cells;

        Assert(cells[0].Type == "integer" && cells[0].Value == "42", "An INTEGER column must round-trip as integer, not text.");
        Assert(cells[1].Type == "real" && cells[1].Value == "1.5", "A REAL column must round-trip as real.");
        Assert(cells[2].Type == "blob" && cells[3].Value == "text", "BLOB and TEXT must keep distinct storage classes.");
    }

    private static async Task LastInsertIdIsReportedAsync()
    {
        using var root = CreateTempRoot();
        await using var service = CreateService(root);

        var db = await service.LoadAsync(new SqlLoadOptions("sqlite::memory:"), default);
        await service.ExecuteAsync(new SqlExecuteOptions(db, "CREATE TABLE t (id INTEGER PRIMARY KEY, v TEXT)"), default);
        var first = await service.ExecuteAsync(new SqlExecuteOptions(db, "INSERT INTO t (v) VALUES (?)", [SqlValue.FromText("a")]), default);
        var second = await service.ExecuteAsync(new SqlExecuteOptions(db, "INSERT INTO t (v) VALUES (?)", [SqlValue.FromText("b")]), default);

        Assert(first.RowsAffected == 1 && second.RowsAffected == 1, "Each insert must report one affected row.");
        Assert(first.LastInsertId == 1 && second.LastInsertId == 2, "The last insert row id must advance across inserts.");
    }

    private static async Task UnknownHandleIsRejectedAsync()
    {
        using var root = CreateTempRoot();
        await using var service = CreateService(root);

        await AssertThrowsAsync(
            () => service.SelectAsync(new SqlExecuteOptions("db-not-open", "SELECT 1"), default).AsTask(),
            "Using a handle that was never loaded must fail rather than silently return no rows.");
    }

    private static async Task CloseIsIdempotentAsync()
    {
        using var root = CreateTempRoot();
        await using var service = CreateService(root);

        var db = await service.LoadAsync(new SqlLoadOptions("sqlite::memory:"), default);
        var first = await service.CloseAsync(new SqlExecuteOptions(db, "SELECT 1"), default);
        var second = await service.CloseAsync(new SqlExecuteOptions(db, "SELECT 1"), default);

        Assert(first.Closed, "Closing an open handle must report Closed.");
        Assert(!second.Closed, "Closing the same handle twice must be idempotent and report Closed=false.");
    }

    private static async Task FileDatabasePersistsAcrossServicesAsync()
    {
        using var root = CreateTempRoot();

        await using (var writer = CreateService(root))
        {
            var db = await writer.LoadAsync(new SqlLoadOptions("sqlite://app.db"), default);
            await writer.ExecuteAsync(new SqlExecuteOptions(db, "CREATE TABLE t (v TEXT)"), default);
            await writer.ExecuteAsync(new SqlExecuteOptions(db, "INSERT INTO t VALUES (?)", [SqlValue.FromText("persisted")]), default);
        }

        Assert(File.Exists(Path.Combine(root.Dir, "app.db")), "A file-backed database must be created inside the authorized base directory.");

        await using var reader = CreateService(root);
        var reopened = await reader.LoadAsync(new SqlLoadOptions("sqlite://app.db"), default);
        var selected = await reader.SelectAsync(new SqlExecuteOptions(reopened, "SELECT v FROM t"), default);

        Assert(selected.Rows.Length == 1 && selected.Rows[0].Cells[0].Value == "persisted",
            "A file-backed database must survive a new service instance.");
    }

    private static async Task PathTraversalIsRejectedAsync()
    {
        using var root = CreateTempRoot();
        await using var service = CreateService(root);

        // Reaching outside the database base directory must be refused by the file policy before
        // SQLite is asked to open anything.
        await AssertThrowsAsync(
            () => service.LoadAsync(new SqlLoadOptions("sqlite://../escaped.db"), default).AsTask(),
            "A relative path escaping the database base directory must be rejected.");
    }

    private static async Task InMemoryNeedsNoDiskFileAsync()
    {
        using var root = CreateTempRoot();
        await using var service = CreateService(root);

        var db = await service.LoadAsync(new SqlLoadOptions("sqlite::memory:"), default);
        await service.ExecuteAsync(new SqlExecuteOptions(db, "CREATE TABLE t (v TEXT)"), default);

        Assert(Directory.GetFiles(root.Dir).Length == 0, "An in-memory database must not touch the file system.");
    }

    private static async Task AutoCreateFalseRejectsMissingDatabaseAsync()
    {
        using var root = CreateTempRoot();
        await using var service = CreateService(root);

        await AssertThrowsAsync(
            () => service.LoadAsync(new SqlLoadOptions("sqlite://missing.db", AutoCreate: false), default).AsTask(),
            "autoCreate: false must fail when the database file does not exist.");

        Assert(
            !File.Exists(Path.Combine(root.Dir, "missing.db")),
            "autoCreate: false must not create the database file.");
    }

    private static async Task AutoCreateFalseOpensExistingDatabaseAsync()
    {
        using var root = CreateTempRoot();

        await using (var writer = CreateService(root))
        {
            var created = await writer.LoadAsync(new SqlLoadOptions("sqlite://app.db"), default);
            await writer.ExecuteAsync(new SqlExecuteOptions(created, "CREATE TABLE t (v TEXT)"), default);
        }

        await using var service = CreateService(root);
        var reopened = await service.LoadAsync(new SqlLoadOptions("sqlite://app.db", AutoCreate: false), default);
        var selected = await service.SelectAsync(new SqlExecuteOptions(reopened, "SELECT COUNT(*) FROM t"), default);

        Assert(
            selected.Rows.Length == 1,
            "autoCreate: false must still open a database that already exists.");
    }

    private static void ScopeAuthorizerRespectsAllowDenyAndWildcards()
    {
        var options = new SqlLoadOptions("sqlite://app.db");

        Assert(SqlScopeAuthorizer.AllowsLoad(options, [new PathScope("appData", "**")], []),
            "An allow scope covering the database must permit loading it.");
        Assert(!SqlScopeAuthorizer.AllowsLoad(options, [], []),
            "An empty allow list must deny every file-backed database.");
        Assert(!SqlScopeAuthorizer.AllowsLoad(options, [new PathScope("appData", "**")], [new PathScope("appData", "app.db")]),
            "Deny must win over a broad allow.");
        Assert(SqlScopeAuthorizer.AllowsLoad(new SqlLoadOptions("sqlite::memory:"), [], []),
            "An in-memory database touches no path, so an empty allow list must still permit it.");
    }

    private static void PluginRegistersAllFourCommands()
    {
        var commands = new CommandRouterBuilder();
        new SqlPlugin(new ThrowingSqlService()).ConfigureCommands(commands);
        var router = commands.Build();

        var registered = router.Commands.ToHashSet(StringComparer.Ordinal);
        Assert(registered.Contains("plugin:sql|load"), "load must be registered.");
        Assert(registered.Contains("plugin:sql|select"), "select must be registered.");
        Assert(registered.Contains("plugin:sql|execute"), "execute must be registered.");
        Assert(registered.Contains("plugin:sql|close"), "close must be registered.");

        var permissions = router.RegisteredPermissions.ToHashSet(StringComparer.Ordinal);
        Assert(permissions.Contains("plugin:sql|allow-load"), "The load permission must be registered.");
        Assert(permissions.Contains("plugin:sql|allow-select"), "The select permission must be registered.");
        Assert(permissions.Contains("plugin:sql|allow-execute"), "The execute permission must be registered.");
        Assert(permissions.Contains("plugin:sql|allow-close"), "The close permission must be registered.");
    }

    private static void ConnectionStringHelpersClassifySchemes()
    {
        Assert(SqliteConnectionString.IsInMemory("sqlite::memory:"), "sqlite::memory: is an in-memory database.");
        Assert(SqliteConnectionString.IsInMemory("sqlite://"), "An empty sqlite:// tail is an in-memory database.");
        Assert(!SqliteConnectionString.IsInMemory("sqlite://app.db"), "A named file is not in-memory.");
        Assert(SqliteConnectionString.ExtractRelativePath("sqlite://nested/app.db") == "nested/app.db",
            "The database path must be extracted relative to the base directory.");
        Assert(new SqliteDriver().CanHandle("sqlite://app.db"), "The built-in driver must claim the sqlite scheme.");

        // The service hands the provider an already-resolved rooted path. On Unix that surfaces as
        // "sqlite:///tmp/app.db"; losing the leading separator would make the provider look for a
        // relative file and fail with "unable to open database file".
        var rooted = Path.Combine(Path.GetTempPath(), "rooted.db");
        Assert(SqliteConnectionString.Normalize($"sqlite://{rooted}") == $"Data Source={rooted};Mode=ReadWriteCreate",
            "Normalizing a connection string with a rooted path must preserve the rooted path.");
    }

    private static void PlaceholderRewriteSkipsLiteralsAndComments()
    {
        // Only real placeholders are renumbered. A question mark inside a string literal or a
        // comment is text, and rewriting it would corrupt the statement.
        Assert(SqliteDatabase.RewritePositionalPlaceholders("SELECT ? FROM t", 1) == "SELECT :tarui0 FROM t",
            "A positional placeholder must be rewritten to its named form.");

        var withLiteral = SqliteDatabase.RewritePositionalPlaceholders("SELECT 'why?' AS q, ? FROM t", 1);
        Assert(withLiteral == "SELECT 'why?' AS q, :tarui0 FROM t",
            "A question mark inside a string literal must be left untouched.");

        var withComment = SqliteDatabase.RewritePositionalPlaceholders("SELECT ? -- really?\nFROM t", 1);
        Assert(withComment == "SELECT :tarui0 -- really?\nFROM t",
            "A question mark inside a line comment must be left untouched.");

        Assert(SqliteDatabase.RewritePositionalPlaceholders("SELECT 1", 0) == "SELECT 1",
            "A statement with no values must be passed through unchanged.");
    }

    private static async Task AssertThrowsAsync(Func<Task> action, string message)
    {
        try
        {
            await action();
        }
        catch (Exception)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static SqlService CreateService(TestRoot root) =>
        new([new SqliteDriver()], new ScopedPolicy(root), root.BaseName);

    private static TestRoot CreateTempRoot()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"tarui-sql-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return new TestRoot(dir);
    }

    /// <summary>Only satisfies DI construction; the registration test never invokes a command.</summary>
    private sealed class ThrowingSqlService : ISqlService
    {
        public ValueTask<string> LoadAsync(SqlLoadOptions options, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<SqlSelectResult> SelectAsync(SqlExecuteOptions options, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<SqlExecuteResult> ExecuteAsync(SqlExecuteOptions options, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<SqlCloseResult> CloseAsync(SqlExecuteOptions options, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class TestRoot : IDisposable
    {
        public TestRoot(string dir)
        {
            Dir = dir;
            BaseName = $"__sql_test_{Guid.NewGuid():N}";
        }

        public string Dir { get; }

        public string BaseName { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Dir, recursive: true);
            }
            catch
            {
                // Best effort cleanup; a locked SQLite file must not fail the suite.
            }
        }
    }

    /// <summary>Maps the test base name onto the temporary directory and defers to the real policy.</summary>
    private sealed class ScopedPolicy(TestRoot root) : IFileAccessPolicy
    {
        private readonly FileAccessPolicy _inner = new();

        public bool TryGetBaseDirectory(string baseName, out string directoryPath, out bool isReadOnly)
        {
            if (string.Equals(baseName, root.BaseName, StringComparison.Ordinal))
            {
                directoryPath = root.Dir;
                isReadOnly = false;
                return true;
            }

            return _inner.TryGetBaseDirectory(baseName, out directoryPath, out isReadOnly);
        }

        public string? ResolveBase(string baseName) =>
            string.Equals(baseName, root.BaseName, StringComparison.Ordinal) ? root.Dir : _inner.ResolveBase(baseName);

        public string Authorize(FileAccessKind kind, string baseDirectory, string requestPath) =>
            _inner.Authorize(kind, baseDirectory, requestPath);

        public bool IsWithinOperationLimit(FileAccessKind kind, long byteCount) => _inner.IsWithinOperationLimit(kind, byteCount);

        public bool TryReserveTotalBytes(long byteCount) => _inner.TryReserveTotalBytes(byteCount);

        public void ReleaseTotalBytes(long byteCount) => _inner.ReleaseTotalBytes(byteCount);

        public Task WriteAllBytesAtomicAsync(string path, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken) =>
            _inner.WriteAllBytesAtomicAsync(path, bytes, cancellationToken);
    }
}