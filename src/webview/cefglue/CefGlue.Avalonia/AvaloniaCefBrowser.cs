using System;
using Avalonia.Controls;
using Xilium.CefGlue.Avalonia.Platform;
using Xilium.CefGlue.Common;
using Xilium.CefGlue.Common.Platform;

namespace Xilium.CefGlue.Avalonia
{
    /// <summary>
    /// The Avalonia CEF browser.
    /// </summary>
    public class AvaloniaCefBrowser(Func<CefRequestContext> cefRequestContextFactory = null)
        : BaseCefBrowser(cefRequestContextFactory)
    {
        static AvaloniaCefBrowser()
        {
            if (CefRuntime.Platform == CefRuntimePlatform.MacOS && !CefRuntimeLoader.IsLoaded)
            {
                CefRuntimeLoader.Load(new AvaloniaBrowserProcessHandler());
            }
        }

        internal override IControl CreateControl()
        {
            return new AvaloniaControl(this, VisualChildren);
        }

        internal override IOffScreenControlHost CreateOffScreenControlHost()
        {
            throw new NotSupportedException("The tarui.net CefGlue port supports windowed rendering only.");
        }

        public override IOffScreenKeyboardHandler CreateOffScreenKeyboardHandler(object control)
        {
            throw new NotSupportedException("The tarui.net CefGlue port supports windowed rendering only.");
        }

        internal override IOffScreenPopupHost CreatePopupHost()
        {
            throw new NotSupportedException("The tarui.net CefGlue port supports windowed rendering only.");
        }
    }
}
