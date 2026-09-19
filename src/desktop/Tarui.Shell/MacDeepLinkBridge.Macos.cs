#if MACOS || (TARGET_OS_MAC && !TARGET_OS_IPHONE)
#define TARUI_HAS_FOUNDATION
#endif

#if TARUI_HAS_FOUNDATION
using Foundation;
using ObjCRuntime;
#endif

using Microsoft.Extensions.Hosting;

namespace Tarui.Shell;

/// <inheritdoc />
public abstract partial class MacDeepLinkBridge : IHostedService
{
    /// <summary>
    /// Real macOS bridge. Registers an AppleEvent handler on <c>NSAppleEventManager</c> for the
    /// <c>kInternetEventClass</c> / <c>kAEGetURL</c> event pair, parses the URL out of the
    /// descriptor via the injected <see cref="IMacDeepLinkUrlExtractor"/> (so tests can stub
    /// the Cocoa side), and forwards it through <see cref="DeepLinkService.Deliver"/>.
    /// </summary>
    private sealed class Macos : MacDeepLinkBridge
    {
        private readonly DeepLinkService _service;
        private readonly IMacDeepLinkUrlExtractor _extractor;
#if TARUI_HAS_FOUNDATION
        private NSObject? _handler;
#endif
        private bool _started;

        public Macos(DeepLinkService service, IMacDeepLinkUrlExtractor extractor)
        {
            _service = service;
            _extractor = extractor;
        }

        public override Task StartAsync(CancellationToken cancellationToken)
        {
            if (_started)
            {
                return Task.CompletedTask;
            }

            _started = true;
#if TARUI_HAS_FOUNDATION
            // The AppleEvent manager replaces the previously registered handler on every call,
            // so installing unconditionally here is the documented and race-free behaviour.
            _handler = new AppleEventHandler(_service, _extractor);
            NSAppleEventManager.SharedAppleEventManager.SetEventHandler(
                _handler,
                new Selector("handleGetUrlEvent:withReplyEvent:"),
                (AEEventClass)FourCC('G', 'U', 'R', 'L'),
                (AEEventId)FourCC('G', 'U', 'R', 'L'));
#endif
            return Task.CompletedTask;
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            // Removing the handler is best-effort: a host restart will replace it on the next
            // start, so leaving a stale handler around does not leak capabilities across runs.
#if TARUI_HAS_FOUNDATION
            if (_handler is not null)
            {
                try
                {
                    NSAppleEventManager.SharedAppleEventManager.RemoveEventHandler(_handler);
                }
                catch
                {
                    // Removal is best-effort; failure is not fatal during host shutdown.
                }

                _handler = null;
            }
#endif
            return Task.CompletedTask;
        }

#if TARUI_HAS_FOUNDATION
        /// <summary>Builds a four-character code from ASCII characters, as used by AppleEvent manager.</summary>
        private static int FourCC(char a, char b, char c, char d) =>
            (a << 24) | (b << 16) | (c << 8) | d;

        /// <summary>
        /// NSObject subclass carrying the AppleEvent handler method. The selector name must
        /// match the value passed to <c>SetEventHandler</c>; renaming requires updating both.
        /// </summary>
        private sealed class AppleEventHandler : NSObject
        {
            private readonly DeepLinkService _service;
            private readonly IMacDeepLinkUrlExtractor _extractor;

            internal AppleEventHandler(DeepLinkService service, IMacDeepLinkUrlExtractor extractor)
            {
                _service = service;
                _extractor = extractor;
            }

            [Export("handleGetUrlEvent:withReplyEvent:")]
            public void HandleGetUrlEvent(NSAppleEventDescriptor eventDescriptor, NSAppleEventDescriptor replyEvent)
            {
                var url = _extractor.TryExtractUrl(eventDescriptor.Handle);
                if (!string.IsNullOrEmpty(url))
                {
                    _service.Deliver(url);
                }
            }
        }
#endif
    }
}