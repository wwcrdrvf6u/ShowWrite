using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ShowWrite
{
    public partial class App : Application
    {
        public static MainWindow? MainWindow { get; private set; }
        public static TrayIcon? RandomNoteTrayIcon { get; private set; }
        public static NativeMenuItem? StartMenuItem { get; private set; }
        public static NativeMenuItem? StopMenuItem { get; private set; }
        public static NativeMenuItem? SettingsMenuItem { get; private set; }
        public static NativeMenuItem? ExitMenuItem { get; private set; }
        public static RandomNoteModeWindow? RandomNoteWindow { get; private set; }

        public override void Initialize()
        {
            ThemeManager.InitializeTheme(this);
            AvaloniaXamlLoader.Load(this);

            var config = Config.Load();
            ThemeType themeType = config.Theme switch
            {
                "Light" => ThemeType.Light,
                "LightMinimal" => ThemeType.LightMinimal,
                _ => ThemeType.Dark
            };

            if (themeType != ThemeType.Dark)
            {
                ThemeManager.SetTheme(themeType);
                ThemeManager.ApplyTheme(this, themeType);
            }

            if (TrayIcon.GetIcons(this)?.FirstOrDefault() is TrayIcon trayIcon)
            {
                RandomNoteTrayIcon = trayIcon;

                if (trayIcon.Menu is NativeMenu menu)
                {
                    StartMenuItem = menu.Items.OfType<NativeMenuItem>()
                        .FirstOrDefault(item => item.Header?.ToString() == "开始录制");
                    StopMenuItem = menu.Items.OfType<NativeMenuItem>()
                        .FirstOrDefault(item => item.Header?.ToString() == "结束录制");
                    SettingsMenuItem = menu.Items.OfType<NativeMenuItem>()
                        .FirstOrDefault(item => item.Header?.ToString() == "设置");
                    ExitMenuItem = menu.Items.OfType<NativeMenuItem>()
                        .FirstOrDefault(item => item.Header?.ToString() == "退出");
                }
            }
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                if (Program.RandomNoteMode)
                {
                    var randomNoteWindow = new RandomNoteModeWindow();
                    desktop.MainWindow = randomNoteWindow;
                    RandomNoteWindow = randomNoteWindow;
                    randomNoteWindow.Show();
                    randomNoteWindow.Hide();
                }
                else
                {
                    // 启动图优先显示，异步连接摄像头
                    var splash = new SplashWindow();
                    desktop.MainWindow = splash;
                    splash.Show();

                    var cameraService = new CameraService();
                    StartNormalModeAsync(desktop, splash, cameraService);
                }
            }

            base.OnFrameworkInitializationCompleted();
        }

        /// <summary>
        /// 异步启动主窗口流程：等待摄像头连接完成（或超时/出错）后再关闭启动图并显示主窗口。
        /// </summary>
        private async void StartNormalModeAsync(
            IClassicDesktopStyleApplicationLifetime desktop,
            SplashWindow splash,
            CameraService cameraService)
        {
            var tcs = new TaskCompletionSource<bool>();
            int finished = 0;

            // 超时保护（8 秒），防止摄像头连接卡死导致无法进入主程序
            using var timeoutCts = new CancellationTokenSource();
            timeoutCts.Token.Register(() =>
            {
                if (Interlocked.CompareExchange(ref finished, 1, 0) == 0)
                    tcs.TrySetResult(false);
            });
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(8));

            Action cameraStartedHandler = () =>
            {
                if (Interlocked.CompareExchange(ref finished, 1, 0) == 0)
                    tcs.TrySetResult(true);
            };
            Action<string> errorHandler = _ =>
            {
                if (Interlocked.CompareExchange(ref finished, 1, 0) == 0)
                    tcs.TrySetResult(false);
            };

            cameraService.CameraStarted += cameraStartedHandler;
            cameraService.ErrorOccurred += errorHandler;

            // 异步启动摄像头连接过程
            cameraService.DetectAndConnectCamera();

            // 等待摄像头连接完成或超时
            await tcs.Task;

            // 取消临时事件订阅
            cameraService.CameraStarted -= cameraStartedHandler;
            cameraService.ErrorOccurred -= errorHandler;

            try
            {
                // 关闭启动图，启动主窗口
                var mainWindow = new MainWindow(cameraService);
                desktop.MainWindow = mainWindow;
                MainWindow = mainWindow;

                if (Program.FilesToOpen.Count > 0)
                {
                    mainWindow.OpenFiles(Program.FilesToOpen);
                }

                mainWindow.Show();
                splash.Close();

                // 启动完成后异步检查并更新启动图（不占用启动时间）
                _ = BootImageUpdater.CheckAndUpdateAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[App] 主窗口启动失败: {ex.Message}");
                splash.Close();
            }
        }
    }

    public class RandomNoteModeWindow : Window
    {
        private CameraService? _cameraService;
        private GlobalHotKey? _globalHotKey;
        private bool _isShuttingDown = false;

        public RandomNoteModeWindow()
        {
            WindowDecorations = WindowDecorations.None;
            WindowState = WindowState.Minimized;
            ShowInTaskbar = false;
            Width = 0;
            Height = 0;
            Opacity = 0;

            Loaded += (s, e) => InitializeRandomNote();
        }

        private void InitializeRandomNote()
        {
            var config = Config.Load();
            var settings = config.RandomNote;

            _cameraService = new CameraService();
            Application.Current!.Resources["CameraService"] = _cameraService;

            _cameraService.DetectAndConnectCamera();

            InitializeTrayIcon();
            InitializeGlobalHotKey();
        }

        private void InitializeTrayIcon()
        {
            var trayIcon = App.RandomNoteTrayIcon;
            if (trayIcon != null)
            {
                trayIcon.IsVisible = true;

                if (trayIcon.Menu is NativeMenu menu)
                {
                    foreach (var item in menu.Items.OfType<NativeMenuItem>())
                    {
                        if (item.Header?.ToString() == "开始录制")
                        {
                            item.Click += (s, e) => StartRecording();
                        }
                        else if (item.Header?.ToString() == "结束录制")
                        {
                            item.Click += (s, e) => StopRecording();
                            item.IsEnabled = false;
                        }
                        else if (item.Header?.ToString() == "设置")
                        {
                            item.Click += (s, e) => OpenSettings();
                        }
                        else if (item.Header?.ToString() == "退出")
                        {
                            item.Click += (s, e) => Shutdown();
                        }
                    }
                }

                trayIcon.Clicked += (s, e) =>
                {
                    var config = Config.Load();
                    if (config.RandomNote.Enabled)
                    {
                        if (RandomNoteSettingsWindow.IsRecording())
                            StopRecording();
                        else
                            StartRecording();
                    }
                };
            }
        }

        private void InitializeGlobalHotKey()
        {
            try
            {
                var handle = TryGetWindowHandle();
                if (handle != IntPtr.Zero)
                {
                    _globalHotKey = new GlobalHotKey(handle);
                    var settings = Config.Load().RandomNote;
                    if (!string.IsNullOrEmpty(settings.ShortcutKey))
                    {
                        if (_globalHotKey.Register(settings.ShortcutKey))
                        {
                            _globalHotKey.HotKeyPressed += OnGlobalHotKeyPressed;
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private void OnGlobalHotKeyPressed()
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                var settings = Config.Load().RandomNote;
                RandomNoteSettingsWindow.TryTriggerShortcut(settings.ShortcutKey, _cameraService!, ShowNotification);
            });
        }

        private IntPtr TryGetWindowHandle()
        {
            var platformHandle = this.TryGetPlatformHandle();
            return platformHandle?.Handle ?? IntPtr.Zero;
        }

        private void StartRecording()
        {
            if (_cameraService == null) return;
            RandomNoteSettingsWindow.StartRecordingFromTray(_cameraService, ShowNotification);
            UpdateTrayMenuState(true);
        }

        private void StopRecording()
        {
            RandomNoteSettingsWindow.StopRecording();
            UpdateTrayMenuState(false);
        }

        private void UpdateTrayMenuState(bool isRecording)
        {
            if (App.StartMenuItem != null)
                App.StartMenuItem.IsEnabled = !isRecording;
            if (App.StopMenuItem != null)
                App.StopMenuItem.IsEnabled = isRecording;
        }

        private void OpenSettings()
        {
            if (_cameraService == null) return;
            var settingsWindow = new RandomNoteSettingsWindow(_cameraService);
            settingsWindow.Show();
        }

        private void ShowNotification(string message)
        {
            RandomNoteSettingsWindow.ShowNotification("闪记", message);
        }

        public void Shutdown()
        {
            _isShuttingDown = true;
            this.Close();
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            if (_isShuttingDown)
            {
                _globalHotKey?.Dispose();
                _cameraService?.StopCapture();
                _cameraService?.Dispose();

                if (App.RandomNoteTrayIcon != null)
                {
                    App.RandomNoteTrayIcon.IsVisible = false;
                }
            }
            base.OnClosing(e);
        }
    }
}
