using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace ShowWrite
{
    public partial class SplashWindow : Window
    {
        public DateTime? StartTime { get; private set; }

        public SplashWindow()
        {
            InitializeComponent();
            StartTime = DateTime.Now;

            // 设置窗口位置在屏幕中央
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            this.Left = (SystemParameters.PrimaryScreenWidth - this.Width) / 2;
            this.Top = (SystemParameters.PrimaryScreenHeight - this.Height) / 2;

            // 添加淡入动画
            this.Opacity = 0;
            this.Loaded += (s, e) =>
            {
                var fadeIn = new DoubleAnimation
                {
                    From = 0,
                    To = 1,
                    Duration = TimeSpan.FromMilliseconds(500),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                this.BeginAnimation(OpacityProperty, fadeIn);
            };
        }

        /// <summary>
        /// 显示启动图
        /// </summary>
        public void ShowSplash()
        {
            try
            {
                Logger.Debug("SplashWindow", "显示启动图");

                // 显示窗口
                this.Show();
                this.Activate();
                this.Topmost = true;

                // 强制更新UI
                this.UpdateLayout();

                Logger.Debug("SplashWindow", "启动图显示成功");
            }
            catch (Exception ex)
            {
                Logger.Error("SplashWindow", $"显示启动图失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 关闭启动图（带淡出效果）
        /// </summary>
        public void CloseSplash()
        {
            try
            {
                // 添加淡出效果
                var fadeOut = new DoubleAnimation
                {
                    From = 1.0,
                    To = 0.0,
                    Duration = TimeSpan.FromMilliseconds(300),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };

                fadeOut.Completed += (s, e) =>
                {
                    try
                    {
                        this.Close();
                        Logger.Debug("SplashWindow", "启动图已关闭");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("SplashWindow", $"关闭窗口失败: {ex.Message}", ex);
                    }
                };

                this.BeginAnimation(OpacityProperty, fadeOut);
            }
            catch (Exception ex)
            {
                Logger.Error("SplashWindow", $"关闭启动图失败: {ex.Message}", ex);
                try
                {
                    this.Close();
                }
                catch { }
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // 确保窗口在屏幕中央
            this.Left = (SystemParameters.PrimaryScreenWidth - this.ActualWidth) / 2;
            this.Top = (SystemParameters.PrimaryScreenHeight - this.ActualHeight) / 2;
        }
    }
}