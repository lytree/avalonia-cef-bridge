using Tarui.WebView.Abstractions;

namespace Tarui.WebViewEvents.Tests;

internal static class Program
{
    public static int Main()
    {
        TestNavigationAllow();
        TestNavigationExternal();
        TestNavigationDenyFallback();
        TestNavigationMaliciousUrls();
        TestDownloadAllow();
        TestDownloadDenyDefault();
        TestDownloadMaliciousUrls();
        TestUrlGlobMatching();
        TestDraggableRegionHit();
        TestDraggableRegionNoDragOverride();
        TestDraggableRegionDegenerate();
        TestDraggableRegionDiffers();

        Console.WriteLine("Tarui.WebViewEvents self-tests passed.");
        return 0;
    }

    private static WebViewRequestPolicy Policy(
        string[]? allowNavigation = null,
        string[]? externalNavigation = null,
        string[]? allowDownload = null,
        WebViewRequestDecision defaultDownload = WebViewRequestDecision.Deny)
    {
        return new WebViewRequestPolicy(new WebViewPolicyOptions(
            AllowedNavigationPatterns: allowNavigation ?? [],
            ExternalNavigationPatterns: externalNavigation ?? [],
            AllowedDownloadHostPatterns: allowDownload ?? [],
            DefaultDownloadDecision: defaultDownload));
    }

    private static void TestNavigationAllow()
    {
        var policy = Policy(allowNavigation: ["http://localhost:*/*", "https://app.example/**"]);

        AssertEqual(
            WebViewRequestDecision.Allow,
            policy.DecideNavigation(new Uri("http://localhost:5173/index.html")),
            "Local dev navigation on any port should be allowed.");

        AssertEqual(
            WebViewRequestDecision.Allow,
            policy.DecideNavigation(new Uri("https://app.example/settings/profile")),
            "Deep navigation under an allow pattern should be allowed.");

        AssertEqual(
            WebViewRequestDecision.Allow,
            policy.DecideNavigation(new Uri("https://app.example/")),
            "Allow pattern root should be allowed.");
    }

    private static void TestNavigationExternal()
    {
        var policy = Policy(
            allowNavigation: ["http://localhost:*/*"],
            externalNavigation: ["https://*.authserver.com/**"]);

        AssertEqual(
            WebViewRequestDecision.External,
            policy.DecideNavigation(new Uri("https://login.authserver.com/sso")),
            "External rule should hand matching navigation to the OS handler.");

        AssertEqual(
            WebViewRequestDecision.Deny,
            policy.DecideNavigation(new Uri("https://unknown.example/index.html")),
            "Navigation to an unrelated host should be denied.");
    }

    private static void TestNavigationDenyFallback()
    {
        var policy = Policy(allowNavigation: ["http://localhost:5173/**"]);

        AssertEqual(
            WebViewRequestDecision.Deny,
            policy.DecideNavigation(new Uri("https://evil.example/steal")),
            "Unmatched navigation must deny by default.");

        AssertEqual(
            WebViewRequestDecision.Deny,
            policy.DecideNavigation(new Uri("http://localhost:8080/index.html")),
            "Allow pattern with a literal port must not match other ports.");
    }

    private static void TestNavigationMaliciousUrls()
    {
        var broad = Policy(allowNavigation: ["http://localhost:*/*", "https://app.example/**"]);

        AssertDenied(() => broad.DecideNavigation(new Uri("javascript:alert(1)")),
            "javascript: scheme must be denied.");
        AssertDenied(() => broad.DecideNavigation(new Uri("data:text/html,<script>alert(1)</script>")),
            "data: scheme must be denied.");
        AssertDenied(() => broad.DecideNavigation(new Uri("file:///C:/Windows/System32/calc.exe")),
            "file: scheme must be denied.");
        AssertDenied(() => broad.DecideNavigation(new Uri("vbscript:msgbox(1)")),
            "vbscript: scheme must be denied.");
        AssertDenied(() => broad.DecideNavigation(new Uri("about:blank")),
            "about: scheme must be denied.");
        AssertDenied(() => broad.DecideNavigation(new Uri("http://example.com/pa\u0001th")),
            "Control characters in the URL must be denied.");

        AssertThrows<WebViewRequestDeniedException>(
            () => broad.DecideNavigation(new Uri("/relative/path", UriKind.Relative)),
            exception => exception.Reason == WebViewDenialReason.MalformedUrl,
            "A relative navigation URL must be reported as malformed.");
    }

    private static void TestDownloadAllow()
    {
        var policy = Policy(allowDownload: ["*.cdn.example", "localhost"]);

        AssertEqual(
            WebViewRequestDecision.Allow,
            policy.DecideDownload(new Uri("https://assets.cdn.example/files/v1/report.pdf")),
            "Download from a host in the allow list should be allowed.");
        AssertEqual(
            WebViewRequestDecision.Allow,
            policy.DecideDownload(new Uri("http://localhost/data.bin")),
            "Exact host allow should permit a download.");
    }

    private static void TestDownloadDenyDefault()
    {
        var policy = Policy(allowDownload: ["assets.cdn.example"]);

        AssertEqual(
            WebViewRequestDecision.Deny,
            policy.DecideDownload(new Uri("https://assets.other.example/files/report.pdf")),
            "Unlisted download hosts must be denied by default.");

        var permissive = Policy(
            allowDownload: [],
            defaultDownload: WebViewRequestDecision.Allow);
        AssertEqual(
            WebViewRequestDecision.Allow,
            permissive.DecideDownload(new Uri("https://anything.example/file.zip")),
            "An explicit allow-by-default policy should honor configured default.");
    }

    private static void TestDownloadMaliciousUrls()
    {
        var permissive = Policy(allowDownload: ["*"], defaultDownload: WebViewRequestDecision.Allow);

        AssertDenied(() => permissive.DecideDownload(new Uri("javascript:alert(1)")),
            "A javascript: download must always be denied.");
        AssertDenied(() => permissive.DecideDownload(new Uri("data:application/octet-stream;base64,AAAA")),
            "A data: download must always be denied.");
    }

    private static void TestUrlGlobMatching()
    {
        // The engine is pure; we validate representative glob semantics through its public surface.
        var policy = Policy(allowNavigation: ["https://example.com/**/assets/*"]);

        AssertEqual(
            WebViewRequestDecision.Allow,
            policy.DecideNavigation(new Uri("https://example.com/app/assets/app.js")),
            "** should span directories and trailing * should match one segment.");

        AssertEqual(
            WebViewRequestDecision.Deny,
            policy.DecideNavigation(new Uri("https://example.com/app/static/app.js")),
            "A glob requiring an assets segment must not match other directories.");
    }

    private static void TestDraggableRegionHit()
    {
        var regions = new DraggableRegion[]
        {
            new(0, 0, 640, 32, DraggableRegionKind.Drag),
        };

        Assert(DraggableRegionSelector.HitTest(regions, 100, 16), "A point inside a drag region should be draggable.");
        Assert(DraggableRegionSelector.HitTest(regions, 639, 31), "A point at the trailing edge should be draggable.");
        Assert(!DraggableRegionSelector.HitTest(regions, 640, 16), "A point at the region boundary must not be draggable.");
        Assert(!DraggableRegionSelector.HitTest(regions, 100, 64), "A point below the drag region must not be draggable.");
    }

    private static void TestDraggableRegionNoDragOverride()
    {
        var titlebar = new DraggableRegion[] { new(0, 0, 640, 32, DraggableRegionKind.Drag) };
        var withCloseButton = new DraggableRegion[]
        {
            new(0, 0, 640, 32, DraggableRegionKind.Drag),
            new(592, 4, 32, 24, DraggableRegionKind.NoDrag),
        };

        Assert(DraggableRegionSelector.HitTest(titlebar, 600, 10), "A drag region alone is draggable.");
        Assert(!DraggableRegionSelector.HitTest(withCloseButton, 600, 10),
            "A no-drag region on top must prevent dragging an interactive control.");
        Assert(DraggableRegionSelector.HitTest(withCloseButton, 100, 10),
            "A no-drag region must not suppress dragging elsewhere.");
    }

    private static void TestDraggableRegionDegenerate()
    {
        var zeroWidth = new DraggableRegion[] { new(0, 0, 0, 32, DraggableRegionKind.Drag) };
        var zeroHeight = new DraggableRegion[] { new(0, 0, 32, 0, DraggableRegionKind.Drag) };
        var zeroNoDrag = new DraggableRegion[] { new(0, 0, 0, 0, DraggableRegionKind.NoDrag) };

        Assert(!DraggableRegionSelector.HitTest(zeroWidth, 0, 0), "Zero-width drag regions must not match.");
        Assert(!DraggableRegionSelector.HitTest(zeroHeight, 0, 0), "Zero-height drag regions must not match.");
        Assert(!DraggableRegionSelector.HitTest(zeroNoDrag, 0, 0), "Degenerate no-drag regions must not override dragging.");
        Assert(!DraggableRegionSelector.HitTest([], 5, 5), "An empty region list must never be draggable.");
    }

    private static void TestDraggableRegionDiffers()
    {
        var a = new[] { new DraggableRegion(0, 0, 640, 32, DraggableRegionKind.Drag) };
        var b = new[] { new DraggableRegion(0, 0, 640, 32, DraggableRegionKind.Drag) };
        var c = new[] { new DraggableRegion(0, 0, 640, 40, DraggableRegionKind.Drag) };
        var d = System.Array.Empty<DraggableRegion>();

        Assert(!DraggableRegionSelector.Differs(a, b), "Identical region sets should not differ.");
        Assert(DraggableRegionSelector.Differs(a, c), "A changed rect must be reported as different.");
        Assert(DraggableRegionSelector.Differs(a, d), "A change from populated to empty must be reported.");
        Assert(DraggableRegionSelector.Differs(d, a), "A change from empty to populated must be reported.");
    }

    private static void AssertDenied(Func<WebViewRequestDecision> action, string message)
    {
        try
        {
            AssertEqual(WebViewRequestDecision.Deny, action(), message);
        }
        catch (WebViewRequestDeniedException)
        {
            return;
        }
    }

    private static void AssertThrows<TException>(Action action, Func<TException, bool> predicate, string message)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException exception)
        {
            if (predicate(exception))
            {
                return;
            }

            throw new InvalidOperationException($"{message} Unexpected: {exception.Message}");
        }

        throw new InvalidOperationException(message);
    }

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message} Expected {expected}; Actual {actual}.");
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}