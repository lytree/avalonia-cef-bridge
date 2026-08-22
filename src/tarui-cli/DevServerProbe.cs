namespace Tarui.Cli;

/// <summary>
/// Polls a development server URL until it answers an HTTP request.
/// Any HTTP response (including 4xx/5xx) counts as reachable.
/// </summary>
internal static class DevServerProbe
{
    private static readonly HttpClient Client = CreateClient();

    public static async Task<bool> IsReachableAsync(Uri url, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await Client.GetAsync(url, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
    }

    public static async Task<bool> WaitUntilReachableAsync(
        Uri url,
        TimeSpan timeout,
        Action<int>? onAttempt = null,
        CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow + timeout;
        var attempt = 0;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await IsReachableAsync(url, cancellationToken).ConfigureAwait(false))
            {
                return true;
            }

            onAttempt?.Invoke(++attempt);
            await Task.Delay(TimeSpan.FromMilliseconds(750), cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(2),
            PooledConnectionLifetime = TimeSpan.FromSeconds(15)
        };
        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(3)
        };
    }
}
