using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace ShowWrite
{
    public partial class NotificationWindow : Window
    {
        private System.Timers.Timer? _closeTimer;
        private DispatcherTimer? _progressTimer;

        public NotificationWindow()
        {
            InitializeComponent();
        }

        public void ShowNotification(string message, int durationMs = 3000)
        {
            MessageText.Text = message;

            Dispatcher.UIThread.Post(() =>
            {
                var mainWindow = App.MainWindow;
                if (mainWindow == null)
                {
                    return;
                }

                var workingArea = mainWindow.Screens.Primary.WorkingArea;
                // Width/Height 是 DIP，需乘以缩放比换算成物理像素，否则高 DPI 下窗口右侧/底部会被屏幕裁切
                var scale = mainWindow.RenderScaling;

                Show(mainWindow);

                Position = new PixelPoint(
                    (int)Math.Round(workingArea.Right - (Width + 20) * scale),
                    (int)Math.Round(workingArea.Bottom - (Height + 20) * scale));

                StartCountdown(durationMs);
            });
        }

        /// <summary>启动自动关闭倒计时，并在底部进度条上展示剩余时间。</summary>
        private void StartCountdown(int durationMs)
        {
            _closeTimer?.Dispose();
            _progressTimer?.Stop();

            var start = DateTime.Now;
            var scaleTransform = new ScaleTransform(1, 1);
            ProgressFill.RenderTransformOrigin = new RelativePoint(0, 0.5, RelativeUnit.Relative);
            ProgressFill.RenderTransform = scaleTransform;

            var progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            progressTimer.Tick += (s, e) =>
            {
                var remaining = 1.0 - (DateTime.Now - start).TotalMilliseconds / durationMs;
                scaleTransform.ScaleX = Math.Max(0, remaining);
                if (remaining <= 0)
                {
                    progressTimer.Stop();
                }
            };
            progressTimer.Start();
            _progressTimer = progressTimer;

            _closeTimer = new System.Timers.Timer(durationMs);
            _closeTimer.Elapsed += (s, e) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    Close();
                });
                _closeTimer?.Dispose();
            };
            _closeTimer.AutoReset = false;
            _closeTimer.Start();
        }

        private void CloseButton_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            _closeTimer?.Dispose();
            Close();
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            _closeTimer?.Dispose();
            _progressTimer?.Stop();
            base.OnClosing(e);
        }
    }
}
