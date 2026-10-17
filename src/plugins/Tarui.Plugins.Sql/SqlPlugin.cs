using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Tarui.Contracts;
using Tarui.Ipc;

namespace Tarui.Plugins.Sql;

/// <summary>
/// SQL commands. Every command maps to a distinct permission so a capability can authorize reads
/// without granting writes, mirroring Tauri's split between <c>sql:allow-load</c>,
/// <c>sql:allow-select</c> and <c>sql:allow-execute</c>.
/// </summary>
public sealed class SqlPlugin(ISqlService service) : ITaruiPlugin
{
    public void ConfigureCommands(CommandRouterBuilder commands)
    {
        commands.Add(
            "plugin:sql|load",
            TaruiJsonContext.Default.SqlLoadOptions,
            TaruiJsonContext.Default.String,
            (options, _, ct) => service.LoadAsync(options, ct),
            "plugin:sql|allow-load",
            SqlScopeAuthorizer.AllowsLoad);

        commands.Add(
            "plugin:sql|select",
            TaruiJsonContext.Default.SqlExecuteOptions,
            TaruiJsonContext.Default.SqlSelectResult,
            (options, _, ct) => service.SelectAsync(options, ct),
            "plugin:sql|allow-select");

        commands.Add(
            "plugin:sql|execute",
            TaruiJsonContext.Default.SqlExecuteOptions,
            TaruiJsonContext.Default.SqlExecuteResult,
            (options, _, ct) => service.ExecuteAsync(options, ct),
            "plugin:sql|allow-execute");

        commands.Add(
            "plugin:sql|close",
            TaruiJsonContext.Default.SqlExecuteOptions,
            TaruiJsonContext.Default.SqlCloseResult,
            (options, _, ct) => service.CloseAsync(options, ct),
            "plugin:sql|allow-close");
    }
}

/// <summary>
/// Authorizes <c>plugin:sql|load</c> against the database file.
///
/// <para>
/// Unlike the file and store plugins, an empty allow list does <em>not</em> mean "any file".
/// Loading a connection string creates and opens a database file, so an empty allow list would
/// silently grant write access to any name the renderer chose. A capability must therefore name
/// the database in an allow scope, which keeps granting <c>sql:allow-load</c> inert until the
/// application declares which databases exist. In-memory databases touch no path and are always
/// permitted. Deny still wins over allow.
/// </para>
/// </summary>
public static class SqlScopeAuthorizer
{
    public static bool AllowsLoad(SqlLoadOptions options, IReadOnlyList<PathScope> allow, IReadOnlyList<PathScope> deny)
    {
        if (SqliteConnectionString.IsInMemory(options.Connection))
        {
            return true;
        }

        var path = SqliteConnectionString.ExtractRelativePath(options.Connection);

        // An explicit allow entry is required. `MatchesScope` treats an empty allow list as
        // "everything allowed", which is the right default for reading an existing file but wrong
        // for opening a database, because load also creates the file. Guard the empty case first,
        // then require the allow list to actually cover the database.
        if (allow.Count == 0 || !FileScopeMatcher.MatchesScope(allow, [], SqlService.DefaultSqlDatabaseBase, path))
        {
            return false;
        }

        // Passing an empty allow list makes this a pure deny check: it reports false when a deny
        // scope covers the database.
        return FileScopeMatcher.MatchesScope([], deny, SqlService.DefaultSqlDatabaseBase, path);
    }
}

/// <summary>
/// Source-generated JSON metadata for the database files this plugin keeps. The plugin itself does
/// not serialize connection state — a handle is an opaque id — but the schema is declared here so
/// an application that persists handles can round-trip them without runtime reflection.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.Unspecified, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(Dictionary<string, string>))]
public partial class SqlPluginJsonContext : JsonSerializerContext;

public static class SqlPluginServiceCollectionExtensions
{
    /// <summary>
    /// Registers the SQL plugin with the built-in SQLite driver. <see cref="AddSqlPlugin"/> must be
    /// called explicitly by the application composition root, matching the rule that no plugin is
    /// discovered by assembly scanning.
    /// </summary>
    public static IServiceCollection AddSqlPlugin(this IServiceCollection services) => services
        .AddFileAccessPolicy()
        .AddSingleton<ISqlDriverResolver, SqliteDriver>()
        .AddSingleton<ISqlService, SqlService>()
        .AddPlugin<SqlPlugin>();

    /// <summary>
    /// Registers an additional driver (for example PostgreSQL or MySQL). Drivers are consulted in
    /// registration order and the first match wins, so a custom driver registered before the
    /// default SQLite driver can take over the <c>sqlite:</c> scheme.
    /// </summary>
    public static IServiceCollection AddSqlDriver<TDriver>(this IServiceCollection services)
        where TDriver : class, ISqlDriverResolver
    {
        services.AddSingleton<ISqlDriverResolver, TDriver>();
        return services;
    }
}