using System;
using Avalonia.Threading;
using Xilium.CefGlue.Common.Handlers;

namespace Xilium.CefGlue.Avalonia
{
    public sealed class AvaloniaBrowserProcessHandler : BrowserProcessHandler
    {
        private readonly object _gate = new();
        private DispatcherTimer _timer;

        protected override void OnScheduleMessagePumpWork(long delayMs)
        {
            Dispatcher.UIThread.Post(() => Schedule(Math.Max(1, delayMs)));
        }

        private void Schedule(long delayMs)
        {
            lock (_gate)
            {
                _timer?.Stop();
                _timer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(delayMs)
                };
                _timer.Tick += OnTick;
                _timer.Start();
            }
        }

        private void OnTick(object sender, EventArgs eventArgs)
        {
            lock (_gate)
            {
                _timer?.Stop();
                _timer = null;
            }
            CefRuntime.DoMessageLoopWork();
        }
    }
}
