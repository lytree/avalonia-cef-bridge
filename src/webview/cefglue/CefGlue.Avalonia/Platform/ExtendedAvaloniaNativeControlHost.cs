using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Xilium.CefGlue.Avalonia.Platform
{
    internal class ExtendedAvaloniaNativeControlHost : NativeControlHost
    {
        private readonly IntPtr _browserHandle;
        private bool _isAttached;
        private WindowBase _rootWindow;

        public ExtendedAvaloniaNativeControlHost(IntPtr browserHandle)
        {
            _browserHandle = browserHandle;
            
            if (CefRuntime.Platform == CefRuntimePlatform.MacOS)
            {
                // HACK: In OSX we need to force update of the browser bounds: https://magpcss.org/ceforum/viewtopic.php?f=6&t=16341
                PropertyChanged += OnPropertyChanged;
            
                AttachedToVisualTree += OnAttachedToVisualTree;
            }
        }

        private void OnPropertyChanged(object sender, AvaloniaPropertyChangedEventArgs eventArgs)
        {
            if (eventArgs.Property == BoundsProperty)
            {
                FixNativeNativeControlBounds();
            }
        }

        protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle handle)
        {
            var handleType = CefRuntime.Platform switch
            {
                CefRuntimePlatform.Windows => "HWND",
                CefRuntimePlatform.Linux => "XID",
                CefRuntimePlatform.MacOS => "NSView",
                _ => "CEF"
            };
            return new PlatformHandle(_browserHandle, handleType);
        }

        protected override void DestroyNativeControlCore(IPlatformHandle control)
        {
            // do nothing
        }
        
        private void OnAttachedToVisualTree(object sender, VisualTreeAttachmentEventArgs e)
        {
            _isAttached = true;
            _rootWindow = TopLevel.GetTopLevel(this) as WindowBase;
            if (_rootWindow != null)
            {
                _rootWindow.Opened += OnRootWindowOpened;
            }
            FixNativeNativeControlBounds();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            _isAttached = false;
            if (_rootWindow != null)
            {
                _rootWindow.Opened -= OnRootWindowOpened;
                _rootWindow = null;
            }

            if (CefRuntime.Platform == CefRuntimePlatform.MacOS)
            {
                PropertyChanged -= OnPropertyChanged;
                AttachedToVisualTree -= OnAttachedToVisualTree;
            }
        }
        
        private void OnRootWindowOpened(object sender, EventArgs e)
        {
            FixNativeNativeControlBounds();
        }
        
        private void FixNativeNativeControlBounds()
        {
            if ((Bounds.Height != 0 || Bounds.Width != 0) && _isAttached)
            {
                // try delay native host position update, because running right away seems to have no effect sometimes
                DispatcherTimer.RunOnce(() =>
                {
                    if (_isAttached)
                    {
                        TryUpdateNativeControlPosition();
                    }
                }, TimeSpan.FromMilliseconds(500));
            }
        }
    }
}
