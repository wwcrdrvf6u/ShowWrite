using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ShowWrite;

public partial class CameraSelectWindow : Avalonia.Controls.Window
{
    private readonly CameraService? _cameraService;
    private List<int> _availableCameras = new();
    private int _selectedCameraIndex = -1;

    private DispatcherTimer? _spinnerTimer;

    // 主菜单"拍照"按钮的摄像头图标路径（与 SvgIcon["camera"] 一致）
    private const string CameraIconData =
        "M5 7h1a2 2 0 0 0 2 -2a1 1 0 0 1 1 -1h6a1 1 0 0 1 1 1a2 2 0 0 0 2 2h1a2 2 0 0 1 2 2v9a2 2 0 0 1 -2 2h-14a2 2 0 0 1 -2 -2v-9a2 2 0 0 1 2 -2 " +
        "M9 13a3 3 0 1 0 6 0a3 3 0 0 0 -6 0";

    public event Action<int>? CameraSelected;

    public CameraSelectWindow()
    {
        InitializeComponent();
        ApplyThemeVariant();
    }

    public CameraSelectWindow(CameraService cameraService) : this()
    {
        _cameraService = cameraService;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// 让原生标题栏跟随应用主题（深色/浅色）
    /// </summary>
    private void ApplyThemeVariant()
    {
        RequestedThemeVariant = ThemeManager.CurrentTheme == ThemeType.Dark
            ? ThemeVariant.Dark
            : ThemeVariant.Light;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        LoadCachedCameras();
    }

    protected override void OnClosed(EventArgs e)
    {
        StopSpinner();
        base.OnClosed(e);
    }

    /// <summary>
    /// 从缓存配置加载摄像头列表（不重新扫描）
    /// </summary>
    private void LoadCachedCameras()
    {
        var cameraList = this.FindControl<StackPanel>("CameraList");

        var config = Config.Load();
        _availableCameras = new List<int>(config.AvailableCameraIndices);

        if (cameraList == null) return;
        cameraList.Children.Clear();

        if (_availableCameras.Count == 0)
        {
            ShowStatus("未检测到摄像头，请点击刷新按钮重新扫描", showSpinner: false);
            return;
        }

        HideStatus();
        RenderCameraButtons(cameraList, config.AvailableCameraNames);
    }

    /// <summary>
    /// 扫描摄像头并保存到配置（仅刷新按钮触发）
    /// </summary>
    private async void ScanCameras()
    {
        var cameraList = this.FindControl<StackPanel>("CameraList");
        if (cameraList == null) return;

        cameraList.Children.Clear();
        ShowStatus("正在扫描摄像头...", showSpinner: true);

        var scanned = await Task.Run(() =>
        {
            var cameras = new List<int>();
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    using var test = new VideoCapture(i, VideoCaptureAPIs.MSMF);
                    if (test.IsOpened())
                        cameras.Add(i);
                }
                catch { }
            }
            return cameras;
        });

        _availableCameras = scanned;

        var deviceNames = _cameraService?.GetCameraDeviceNames() ?? new Dictionary<int, string>();

        var config = Config.Load();
        config.AvailableCameraIndices = scanned;
        config.AvailableCameraNames = deviceNames;
        config.LastScanTime = DateTime.Now;
        config.Save();

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            cameraList.Children.Clear();

            if (_availableCameras.Count == 0)
            {
                ShowStatus("未检测到摄像头", showSpinner: false);
                return;
            }

            HideStatus();
            RenderCameraButtons(cameraList, deviceNames);
        });
    }

    private void RenderCameraButtons(StackPanel cameraList, Dictionary<int, string> names)
    {
        var textBrush = this.FindResource("ThemeTextPrimary") as IBrush ?? Brushes.White;
        var primaryBrush = this.FindResource("ThemePrimary") as IBrush ?? Brush.Parse("#0078D4");

        // CurrentCameraIndex 是在列表中的位置，转换为实际设备索引
        int currentIndexPos = _cameraService?.CurrentCameraIndex ?? 0;
        int currentDeviceIndex = (currentIndexPos >= 0 && currentIndexPos < _availableCameras.Count)
            ? _availableCameras[currentIndexPos]
            : -1;

        for (int i = 0; i < _availableCameras.Count; i++)
        {
            var cameraIndex = _availableCameras[i];
            string displayName = names.TryGetValue(cameraIndex, out var n) && !string.IsNullOrEmpty(n)
                ? n
                : $"摄像头 {cameraIndex}";

            var btn = new Button
            {
                Classes = { "camera-btn" },
                Tag = cameraIndex,
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    Children =
                    {
                        new Viewbox
                        {
                            Width = 24,
                            Height = 24,
                            Child = new Path
                            {
                                Stroke = textBrush,
                                StrokeThickness = 2,
                                StrokeLineCap = PenLineCap.Round,
                                StrokeJoin = PenLineJoin.Round,
                                Data = Geometry.Parse(CameraIconData)
                            }
                        },
                        new TextBlock
                        {
                            Text = displayName,
                            FontSize = 14,
                            Foreground = textBrush,
                            VerticalAlignment = VerticalAlignment.Center
                        },
                        new TextBlock
                        {
                            Text = cameraIndex == currentDeviceIndex ? "(当前)" : "",
                            FontSize = 12,
                            Foreground = primaryBrush,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    }
                }
            };

            btn.Click += (s, e) =>
            {
                if (s is Button button && button.Tag is int idx)
                {
                    _selectedCameraIndex = idx;
                    CameraSelected?.Invoke(idx);
                    Close();
                }
            };

            cameraList.Children.Add(btn);
        }
    }

    // ---------- 状态面板 & 加载动画 ----------

    private void ShowStatus(string text, bool showSpinner)
    {
        var statusPanel = this.FindControl<StackPanel>("StatusPanel");
        var statusText = this.FindControl<TextBlock>("StatusText");
        var spinnerPath = this.FindControl<Path>("SpinnerPath");

        if (statusPanel != null)
            statusPanel.IsVisible = true;
        if (statusText != null)
            statusText.Text = text;
        if (spinnerPath != null)
            spinnerPath.IsVisible = showSpinner;

        if (showSpinner)
            StartSpinner();
        else
            StopSpinner();
    }

    private void HideStatus()
    {
        StopSpinner();
        var statusPanel = this.FindControl<StackPanel>("StatusPanel");
        if (statusPanel != null)
            statusPanel.IsVisible = false;
    }

    private void StartSpinner()
    {
        var spinnerPath = this.FindControl<Path>("SpinnerPath");
        if (spinnerPath?.RenderTransform is not RotateTransform rotate) return;

        if (_spinnerTimer == null)
        {
            _spinnerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            _spinnerTimer.Tick += (_, _) =>
            {
                rotate.Angle = (rotate.Angle + 12) % 360;
            };
        }
        _spinnerTimer.Start();
    }

    private void StopSpinner()
    {
        _spinnerTimer?.Stop();
    }

    // ---------- 按钮事件 ----------

    private void RefreshBtn_Click(object? sender, RoutedEventArgs e)
    {
        ScanCameras();
    }

    private void CancelBtn_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
