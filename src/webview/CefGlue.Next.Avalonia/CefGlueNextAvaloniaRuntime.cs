using Xilium.CefGlue;
using Xilium.CefGlue.Avalonia;
using Xilium.CefGlue.BrowserProcess;
using Xilium.CefGlue.Common;

namespace CefGlue.Next.Avalonia;

public static class CefGlueNextAvaloniaRuntime
{
    private static readonly object Gate = new();
    private static CefGlueNextAvaloniaRuntimeOptions? _options;
    private static string? _optionsFingerprint;
    private static IReadOnlyList<CefGlueNextAvaloniaSchemeHandlerFactory> _schemeFactories = [];
    private static bool _shutdownRequested;

    public static bool IsInitialized => CefRuntime.IsInitialized;

    public static CefGlueNextAvaloniaRuntimeOptions? Options => _options;

    public static bool RunSubProcess(string[] args, bool exitAfterRun = true)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (!args.Any(static argument => argument.StartsWith("--type=", StringComparison.Ordinal)))
        {
            return false;
        }

        CefSubProcess.Run(args, exitAfterRun);
        return true;
    }

    public static void Initialize(CefGlueNextAvaloniaRuntimeOptions? options = null)
    {
        lock (Gate)
        {
            if (_shutdownRequested)
            {
                throw new InvalidOperationException("CEF runtime has already been shut down for this process.");
            }

            if (CefRuntime.IsInitialized)
            {
                // A null options argument means "no explicit configuration requested" — the caller
                // just wants to make sure the runtime is up. Reuse the original startup options
                // instead of comparing against freshly-constructed defaults.
                if (options is null)
                {
                    return;
                }

                // An explicit second call must be a no-op only when the options are byte-equivalent
                // to the ones used for the original startup. Schemes are especially sensitive:
                // silently dropping new ones on a second call leaves the app with a half-configured
                // browser.
                var fingerprint = ComputeFingerprint(options);
                if (!string.Equals(_optionsFingerprint, fingerprint, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "CEF runtime is already initialized with a different configuration. " +
                        "Mismatching options across Initialize calls would silently leak stale " +
                        "scheme handlers or paths; either reuse the original CefGlueNextAvaloniaRuntimeOptions " +
                        "instance or call Shutdown() before reinitializing.");
                }
                return;
            }

            options ??= new CefGlueNextAvaloniaRuntimeOptions();
            CefGlueNextAvaloniaRuntimeOptions.ValidateSchemes(options.Schemes);
            var runtimeDirectory = options.RuntimeDirectory ?? CefGlueNextAvaloniaRuntimeOptions.GetDefaultRuntimeDirectory();
            if (Directory.Exists(runtimeDirectory))
            {
                CefRuntimeLocator.SetRuntimeDirectory(runtimeDirectory);
            }

            var resourcesDirectory = options.ResolveResourcesDirectory();
            var localesDirectory = options.ResolveLocalesDirectory(resourcesDirectory);
            var cacheDirectory = options.ResolveCacheDirectory();
            var customSchemes = CefGlueNextAvaloniaSchemeMapper.Create(options.Schemes, out var schemeFactories);

            CefRuntimeLoader.Initialize(
                new CefSettings
                {
                    RootCachePath = cacheDirectory,
                    BrowserSubprocessPath = ResolveSubprocessPath(options),
                    WindowlessRenderingEnabled = options.WindowlessRenderingEnabled,
                    NoSandbox = options.NoSandbox,
                    LogSeverity = MapLogSeverity(options.LogSeverity),
                    LogFile = options.LogFile ?? Path.Combine(cacheDirectory, "cef.log"),
                    ResourcesDirPath = resourcesDirectory,
                    LocalesDirPath = localesDirectory
                },
                options.CommandLineFlags.ToArray(),
                customSchemes);

            try
            {
                CefRuntimeLoader.Load(new AvaloniaBrowserProcessHandler());
                _options = options;
                _optionsFingerprint = ComputeFingerprint(options);
                _schemeFactories = schemeFactories;
            }
            catch
            {
                // CefRuntimeLoader only records a successful initialization. A failed
                // native startup can therefore be retried with a corrected configuration.
                _options = null;
                _optionsFingerprint = null;
                _schemeFactories = [];
                throw;
            }
        }
    }

    public static void Shutdown()
    {
        lock (Gate)
        {
            if (_shutdownRequested)
            {
                return;
            }

            _shutdownRequested = true;
            CefRuntimeLoader.Shutdown();
            _options = null;
            _optionsFingerprint = null;
            _schemeFactories = [];
        }
    }

    /// <summary>
    /// Produces a stable string that uniquely identifies the parts of the runtime options that
    /// affect native startup. Used to reject conflicting reinitialization attempts that would
    /// otherwise silently drop schemes, subprocess paths or cache roots.
    /// </summary>
    internal static string ComputeFingerprint(CefGlueNextAvaloniaRuntimeOptions options)
    {
        var builder = new System.Text.StringBuilder();
        builder.Append("rd=").Append(options.RuntimeDirectory ?? string.Empty).Append('|');
        builder.Append("rsd=").Append(options.ResourcesDirectory ?? string.Empty).Append('|');
        builder.Append("lcd=").Append(options.LocalesDirectory ?? string.Empty).Append('|');
        builder.Append("cd=").Append(options.CacheDirectory ?? string.Empty).Append('|');
        builder.Append("bsp=").Append(options.BrowserSubprocessPath ?? string.Empty).Append('|');
        builder.Append("wl=").Append(options.WindowlessRenderingEnabled).Append('|');
        builder.Append("ns=").Append(options.NoSandbox).Append('|');
        builder.Append("lf=").Append(options.LogFile ?? string.Empty).Append('|');
        builder.Append("ls=").Append(options.LogSeverity).Append('|');
        builder.Append("flags=");
        foreach (var flag in options.CommandLineFlags)
        {
            builder.Append(flag.Key).Append('=').Append(flag.Value).Append(';');
        }
        builder.Append("schemes=");
        foreach (var scheme in options.Schemes)
        {
            builder.Append(scheme.SchemeName).Append('@').Append(scheme.DomainName ?? string.Empty).Append(':');
            builder.Append(scheme.IsStandard ? 'S' : '-');
            builder.Append(scheme.IsLocal ? 'L' : '-');
            builder.Append(scheme.IsDisplayIsolated ? 'D' : '-');
            builder.Append(scheme.IsSecure ? 'P' : '-');
            builder.Append(scheme.IsCorsEnabled ? 'C' : '-');
            builder.Append(scheme.IsCspBypassing ? 'B' : '-');
            builder.Append(scheme.IsFetchEnabled ? 'F' : '-');
            builder.Append(';');
        }
        return builder.ToString();
    }

    private static string? ResolveSubprocessPath(CefGlueNextAvaloniaRuntimeOptions options)
    {
        var path = options.ResolveBrowserSubprocessPath();
        if (path is null)
        {
            return null;
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"CEF browser subprocess path does not exist: {path}", path);
        }

        return path;
    }

    private static CefLogSeverity MapLogSeverity(CefGlueNextAvaloniaLogSeverity severity) => severity switch
    {
        CefGlueNextAvaloniaLogSeverity.Verbose => CefLogSeverity.Verbose,
        CefGlueNextAvaloniaLogSeverity.Info => CefLogSeverity.Info,
        CefGlueNextAvaloniaLogSeverity.Warning => CefLogSeverity.Warning,
        CefGlueNextAvaloniaLogSeverity.Error => CefLogSeverity.Error,
        CefGlueNextAvaloniaLogSeverity.Fatal => CefLogSeverity.Fatal,
        CefGlueNextAvaloniaLogSeverity.Disable => CefLogSeverity.Disable,
        _ => CefLogSeverity.Default
    };
}
