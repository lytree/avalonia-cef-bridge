using System;
using Xilium.CefGlue.BrowserProcess.FrameDelivery;
using Xilium.CefGlue.BrowserProcess.Input;
using Xilium.CefGlue.Common.Shared.Helpers;
using Xilium.CefGlue.Common.Shared.RendererProcessCommunication;

namespace Xilium.CefGlue.BrowserProcess.Handlers
{
    internal sealed class RenderProcessHandler : CefRenderProcessHandler
    {
        private CefBrowser _browser;
        private string _crashPipeName;
        private FrameDeliveryRenderSide _frameDelivery;
        private SharedFrameDeliveryRenderSide _sharedFrameDelivery;
        private readonly InputChannelRenderSide _inputChannel = new();
        private readonly MessageDispatcher _messageDispatcher = new();

        public RenderProcessHandler()
        {
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        }

        protected override void OnWebKitInitialized()
        {
            base.OnWebKitInitialized();
            _frameDelivery = new FrameDeliveryRenderSide(_messageDispatcher);
            _sharedFrameDelivery = new SharedFrameDeliveryRenderSide(_messageDispatcher);
        }

        protected override bool OnProcessMessageReceived(
            CefBrowser browser,
            CefFrame frame,
            CefProcessId sourceProcess,
            CefProcessMessage message)
        {
            using (message)
            using (CefObjectTracker.StartTracking())
            {
                _messageDispatcher.DispatchMessage(browser, frame, sourceProcess, message);
            }

            return base.OnProcessMessageReceived(browser, frame, sourceProcess, message);
        }

        protected override void OnContextCreated(
            CefBrowser browser,
            CefFrame frame,
            CefV8Context context)
        {
            base.OnContextCreated(browser, frame, context);
            if (frame.IsMain)
            {
                _inputChannel.Install(context);
            }
        }

        protected override void OnContextReleased(
            CefBrowser browser,
            CefFrame frame,
            CefV8Context context)
        {
            base.OnContextReleased(browser, frame, context);
        }

        protected override void OnBrowserCreated(CefBrowser browser, CefDictionaryValue extraInfo)
        {
            _crashPipeName = extraInfo?.GetString(Constants.CrashPipeNameKey);
            _browser = browser;
            base.OnBrowserCreated(browser, extraInfo);
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs eventArgs)
        {
            var exception = (Exception)eventArgs.ExceptionObject;
            CefFrame frame = null;
            try
            {
                frame = _browser?.FrameCount > 0 ? _browser.GetMainFrame() : null;
            }
            catch
            {
            }

            if (frame != null)
            {
                try
                {
                    using (CefObjectTracker.StartTracking())
                    {
                        var error = new Messages.UnhandledException
                        {
                            ExceptionType = exception.GetType().FullName,
                            Message = exception.Message,
                            StackTrace = exception.StackTrace
                        };
                        frame.SendProcessMessage(CefProcessId.Browser, error.ToCefProcessMessage());
                        return;
                    }
                }
                catch
                {
                }
            }

            SendExceptionToParentProcess(exception);
        }

        private void SendExceptionToParentProcess(Exception exception)
        {
            if (string.IsNullOrEmpty(_crashPipeName)) return;

            try
            {
                var error = new SerializableException
                {
                    ExceptionType = exception.GetType().FullName,
                    Message = exception.Message,
                    StackTrace = exception.StackTrace
                };
                using var pipeClient = new PipeClient(_crashPipeName);
                pipeClient.SendMessage(error.SerializeToString());
            }
            catch
            {
            }
        }
    }
}
