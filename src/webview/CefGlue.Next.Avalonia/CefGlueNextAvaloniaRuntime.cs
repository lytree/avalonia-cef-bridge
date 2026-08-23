using Xilium.CefGlue;
using Xilium.CefGlue.Avalonia;
using Xilium.CefGlue.BrowserProcess;
using Xilium.CefGlue.Common;

namespace CefGlue.Next.Avalonia;

public static class CefGlueNextAvaloniaRuntime
{
    private static readonly object Gate = new();
    private static CefGlueNextAvaloniaRuntimeOptions? _options;
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
                _schemeFactories = schemeFactories;
            }
            catch
            {
                // CefRuntimeLoader only records a successful initialization. A failed
                // native startup can therefore be retried with a corrected configuration.
                _options = null;
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
            _schemeFactories = [];
        }
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
