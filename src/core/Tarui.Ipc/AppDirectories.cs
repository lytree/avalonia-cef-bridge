namespace Tarui.Ipc;

/// <summary>
/// Per-application directory overrides, mirroring Tauri v2's <c>app.appDirectoriesOverride</c>.
/// A portable application can relocate the directories behind the <c>app_*_dir</c> path kinds, and
/// because every <c>appData</c>/<c>appLocalData</c>/<c>appConfig</c>/<c>appCache</c>/<c>appLog</c>
/// base used by the fs/store/sql/persisted-scope authorization is resolved through
/// <see cref="IFileAccessPolicy"/> and the system plugin's path service, the override applies to all of
/// them at once instead of growing a second path layer.
/// <para>
/// Values are read from <c>Tarui:Application:Directories:*</c>. Each value must be an absolute path;
/// a relative, empty or control-character value fails fast during <see cref="Normalize"/> so a
/// misconfiguration cannot silently fall back to the per-user default.
/// </para>
/// </summary>
public sealed record AppDirectoryOptions
{
    /// <summary>Overrides the <c>appData</c> base. Null keeps the platform default.</summary>
    public string? AppData { get; init; }

    /// <summary>Overrides the <c>appLocalData</c> base. Null keeps the platform default.</summary>
    public string? AppLocalData { get; init; }

    /// <summary>Overrides the <c>appConfig</c> base. Null keeps the platform default.</summary>
    public string? AppConfig { get; init; }

    /// <summary>Overrides the <c>appCache</c> base. Null keeps the platform default.</summary>
    public string? AppCache { get; init; }

    /// <summary>Overrides the <c>appLog</c> base. Null keeps the platform default.</summary>
    public string? AppLog { get; init; }

    /// <summary>
    /// Returns the configured override for <paramref name="baseName"/>, or null when that base keeps
    /// its platform default. Base names are matched case-insensitively, like the IPC path kinds.
    /// </summary>
    public string? TryGetOverride(string baseName) => baseName.ToLowerInvariant() switch
    {
        "appdata" => AppData,
        "applocaldata" => AppLocalData,
        "appconfig" => AppConfig,
        "appcache" => AppCache,
        "applog" => AppLog,
        _ => null,
    };

    /// <summary>
    /// Returns a copy whose configured values are trimmed and made absolute. Throws
    /// <see cref="InvalidOperationException"/> for an empty, relative or control-character value so
    /// invalid configuration is reported at bootstrap with the offending key name.
    /// </summary>
    public AppDirectoryOptions Normalize() => this with
    {
        AppData = NormalizeValue(AppData, "Tarui:Application:Directories:AppData"),
        AppLocalData = NormalizeValue(AppLocalData, "Tarui:Application:Directories:AppLocalData"),
        AppConfig = NormalizeValue(AppConfig, "Tarui:Application:Directories:AppConfig"),
        AppCache = NormalizeValue(AppCache, "Tarui:Application:Directories:AppCache"),
        AppLog = NormalizeValue(AppLog, "Tarui:Application:Directories:AppLog"),
    };

    private static string? NormalizeValue(string? value, string key)
    {
        if (value is null)
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            throw new InvalidOperationException(
                $"Configuration key '{key}' must be an absolute path, but the value is empty.");
        }

        if (trimmed.Any(char.IsControl))
        {
            throw new InvalidOperationException(
                $"Configuration key '{key}' must not contain control characters.");
        }

        if (!Path.IsPathRooted(trimmed))
        {
            throw new InvalidOperationException(
                $"Configuration key '{key}' must be an absolute path, but '{value}' was found.");
        }

        return Path.GetFullPath(trimmed);
    }
}
