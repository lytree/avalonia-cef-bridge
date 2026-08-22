namespace Tarui.Cli;

/// <summary>
/// Orchestrates <c>tarui dev</c>: starts the frontend dev server, waits for it to
/// become reachable, then launches the desktop app with dev-mode environment
/// variables and tears both processes down together on Ctrl+C.
/// </summary>
internal sealed class DevCommand
{
    private readonly CliConsole _console;

    public DevCommand(CliConsole console) => _console = console;

    public async Task<int> RunAsync(CliOptions options)
    {
        var paths = CliPaths.Resolve(options.ManifestPath);
        var manifest = ManifestLoader.LoadValidated(paths);
        var devUrl = ManifestLoader.ResolveDevUrl(manifest);
        var desktopProject = ManifestLoader.ResolveDesktopProject(manifest, options.Project, paths);
        var frontendCommand = manifest.Build.BeforeDevCommand;

        if (string.IsNullOrWhiteSpace(frontendCommand))
        {
            _console.Warn("No build.beforeDevCommand configured; skipping the dev server.");
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        ProcessSession? webSession = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(frontendCommand))
            {
                webSession = await StartDevServerAsync(manifest, frontendCommand, paths, devUrl, cancellation.Token)
                    .ConfigureAwait(false);
            }

            var appArguments = options.NoWatch
                ? new[] { "run", "--project", desktopProject }
                : new[] { "watch", "run", "--project", desktopProject };
            _console.Section();
            _console.Info($"Starting desktop app: dotnet {string.Join(' ', appArguments)}");
            await using var appSession = ProcessSession.Start(
                "dotnet",
                appArguments,
                paths.ManifestDirectory,
                new Dictionary<string, string>
                {
                    ["TARUI_WEB_MODE"] = "http",
                    ["TARUI_WEB_URL"] = devUrl.ToString()
                },
                _console.Out,
                "[app] ",
                cancellation.Token);
            _console.Info("Press Ctrl+C to stop.");

            return await WaitForSessionExitAsync(appSession, webSession, cancellation.Token).ConfigureAwait(false);
        }
        finally
        {
            if (webSession is not null)
            {
                await webSession.StopAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task<ProcessSession> StartDevServerAsync(
        AppManifest manifest,
        string command,
        CliPaths paths,
        Uri devUrl,
        CancellationToken cancellationToken)
    {
        var (shellFile, shellArguments) = ShellCommand.For(command);
        var workingDirectory = paths.FrontendWorkingDirectory(manifest.Build);
        _console.Section();
        _console.Info($"Starting dev server: {command}");
        _console.Info($"  cwd: {workingDirectory}");
        var session = ProcessSession.Start(
            shellFile,
            shellArguments,
            workingDirectory,
            output: _console.Out,
            linePrefix: "[web] ",
            cancellationToken: cancellationToken);

        _console.Info($"Waiting for dev server at {devUrl} ...");
        var reachable = await DevServerProbe.WaitUntilReachableAsync(
            devUrl,
            TimeSpan.FromSeconds(60),
            onAttempt: attempt => _console.Info($"  attempt {attempt} ..."),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!reachable)
        {
            throw new CliException(
                $"Dev server at {devUrl} did not become reachable within 60s. Check the [web] output above.");
        }

        _console.Info("Dev server is ready.");
        return session;
    }

    private async Task<int> WaitForSessionExitAsync(
        ProcessSession appSession,
        ProcessSession? webSession,
        CancellationToken cancellationToken)
    {
        var appExit = appSession.WaitForExitAsync();
        var webExit = webSession?.WaitForExitAsync();

        while (!cancellationToken.IsCancellationRequested)
        {
            var candidates = new List<Task> { appExit, Task.Delay(200, cancellationToken) };
            if (webExit is not null)
            {
                candidates.Add(webExit);
            }

            var completed = await Task.WhenAny(candidates).ConfigureAwait(false);
            if (completed == appExit)
            {
                var exitCode = await appExit.ConfigureAwait(false);
                _console.Warn($"Desktop app exited with code {exitCode}.");
                return exitCode;
            }

            if (completed == webExit)
            {
                var exitCode = await webExit!.ConfigureAwait(false);
                _console.Warn($"Dev server exited with code {exitCode}.");
                return exitCode;
            }
        }

        _console.Info("Stopping processes ...");
        await appSession.StopAsync().ConfigureAwait(false);
        return 0;
    }
}
