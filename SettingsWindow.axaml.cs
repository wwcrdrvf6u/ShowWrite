using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using System;
using System.Collections.Generic;
using System.IO;
using System.Management;
using System.Threading;

namespace ShowWrite
{
    public partial class SettingsWindow : Window
    {
        private StackPanel? _generalPage;
        private StackPanel? _appearancePage;
        private StackPanel? _penPage;
        private StackPanel? _ocrPage;
        private StackPanel? _aboutPage;

        private NumericUpDown? _denominatorInput;
        private NumericUpDown? _ratioMinInput;
        private NumericUpDown? _ratioMaxInput;
        private NumericUpDown? _speedThresholdFastInput;
        private NumericUpDown? _speedThresholdSlowInput;
        private NumericUpDown? _ratioChangeCoefficientInput;
        private CheckBox? _enablePalmEraserInput;
        private NumericUpDown? _palmEraserThresholdInput;
        private TextBlock? _motherboardSerialText;
        private TextBlock? _uuidText;
        private ComboBox? _themeComboBox;
        private ComboBox? _defaultCameraComboBox;
        private CheckBox? _showPipButtonInput;
        private CheckBox? _cameraKeepAliveInput;
        private CheckBox? _showPhotoScrollbarInput;
        private NumericUpDown? _pdfImportDpiInput;
        private TextBox? _bootImageApiInput;
        private TextBlock? _bootLocalVersionText;
        private TextBlock? _bootStatusText;
        private SplitView? _settingsSplitView;
        private List<int> _availableCameras = new();

        private readonly CameraService? _cameraService;

        private CheckBox? _enableOcrDirectoryInput;
        private StackPanel? _ocrModelListPanel;
        private Button? _ocrRedownloadBtn;
        private ProgressBar? _ocrDownloadProgress;
        private TextBlock? _ocrDownloadStatus;
        private TextBlock? _ocrModelsDirText;
        private RadioButton? _modelSetMobile;
        private RadioButton? _modelSetServer;
        private RadioButton? _modelSetHybrid;
        private RadioButton? _modelSetCustom;
        private TextBlock? _modelSetMobileStatus;
        private TextBlock? _modelSetServerStatus;
        private TextBlock? _modelSetHybridStatus;
        private Button? _modelSetMobileDownload;
        private Button? _modelSetServerDownload;
        private Button? _modelSetHybridDownload;
        private StackPanel? _customModelPanel;
        private TextBox? _customDetInput;
        private TextBox? _customRecInput;
        private TextBox? _customDictInput;

        public SettingsWindow()
        {
            InitializeComponent();

            RequestedThemeVariant = ThemeManager.CurrentTheme == ThemeType.Dark
                ? ThemeVariant.Dark
                : ThemeVariant.Light;

            _generalPage = this.FindControl<StackPanel>("GeneralPage");
            _appearancePage = this.FindControl<StackPanel>("AppearancePage");
            _penPage = this.FindControl<StackPanel>("PenPage");
            _ocrPage = this.FindControl<StackPanel>("OcrPage");
            _aboutPage = this.FindControl<StackPanel>("AboutPage");

            _denominatorInput = this.FindControl<NumericUpDown>("DenominatorInput");
            _ratioMinInput = this.FindControl<NumericUpDown>("RatioMinInput");
            _ratioMaxInput = this.FindControl<NumericUpDown>("RatioMaxInput");
            _speedThresholdFastInput = this.FindControl<NumericUpDown>("SpeedThresholdFastInput");
            _speedThresholdSlowInput = this.FindControl<NumericUpDown>("SpeedThresholdSlowInput");
            _ratioChangeCoefficientInput = this.FindControl<NumericUpDown>("RatioChangeCoefficientInput");
            _enablePalmEraserInput = this.FindControl<CheckBox>("EnablePalmEraserInput");
            _palmEraserThresholdInput = this.FindControl<NumericUpDown>("PalmEraserThresholdInput");
            _motherboardSerialText = this.FindControl<TextBlock>("MotherboardSerialText");
            _uuidText = this.FindControl<TextBlock>("UuidText");
            _themeComboBox = this.FindControl<ComboBox>("ThemeComboBox");
            _defaultCameraComboBox = this.FindControl<ComboBox>("DefaultCameraComboBox");
            _showPipButtonInput = this.FindControl<CheckBox>("ShowPipButtonInput");
            _cameraKeepAliveInput = this.FindControl<CheckBox>("CameraKeepAliveInput");
            _showPhotoScrollbarInput = this.FindControl<CheckBox>("ShowPhotoScrollbarInput");
            _pdfImportDpiInput = this.FindControl<NumericUpDown>("PdfImportDpiInput");
            _bootImageApiInput = this.FindControl<TextBox>("BootImageApiInput");
            _bootLocalVersionText = this.FindControl<TextBlock>("BootLocalVersionText");
            _bootStatusText = this.FindControl<TextBlock>("BootStatusText");
            _settingsSplitView = this.FindControl<SplitView>("SettingsSplitView");

            _enableOcrDirectoryInput = this.FindControl<CheckBox>("EnableOcrDirectoryInput");
            _ocrModelListPanel = this.FindControl<StackPanel>("OcrModelListPanel");
            _ocrRedownloadBtn = this.FindControl<Button>("OcrRedownloadBtn");
            _ocrDownloadProgress = this.FindControl<ProgressBar>("OcrDownloadProgress");
            _ocrDownloadStatus = this.FindControl<TextBlock>("OcrDownloadStatus");
            _ocrModelsDirText = this.FindControl<TextBlock>("OcrModelsDirText");
            _modelSetMobile = this.FindControl<RadioButton>("ModelSetMobile");
            _modelSetServer = this.FindControl<RadioButton>("ModelSetServer");
            _modelSetHybrid = this.FindControl<RadioButton>("ModelSetHybrid");
            _modelSetCustom = this.FindControl<RadioButton>("ModelSetCustom");
            _modelSetMobileStatus = this.FindControl<TextBlock>("ModelSetMobileStatus");
            _modelSetServerStatus = this.FindControl<TextBlock>("ModelSetServerStatus");
            _modelSetHybridStatus = this.FindControl<TextBlock>("ModelSetHybridStatus");
            _modelSetMobileDownload = this.FindControl<Button>("ModelSetMobileDownload");
            _modelSetServerDownload = this.FindControl<Button>("ModelSetServerDownload");
            _modelSetHybridDownload = this.FindControl<Button>("ModelSetHybridDownload");
            _customModelPanel = this.FindControl<StackPanel>("CustomModelPanel");
            _customDetInput = this.FindControl<TextBox>("CustomDetInput");
            _customRecInput = this.FindControl<TextBox>("CustomRecInput");
            _customDictInput = this.FindControl<TextBox>("CustomDictInput");

            LoadPenSettings();
            LoadThemeSettings();
            LoadGeneralSettings();
            LoadSystemInfo();
            LoadOcrSettings();
            RefreshOcrStatus();
        }

        public SettingsWindow(CameraService cameraService) : this()
        {
            _cameraService = cameraService;
        }

        private void TogglePane_Click(object? sender, RoutedEventArgs e)
        {
            if (_settingsSplitView != null)
            {
                _settingsSplitView.IsPaneOpen = !_settingsSplitView.IsPaneOpen;
            }
        }

        private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                BeginMoveDrag(e);
            }
        }

        private void NavListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (NavListBox == null) return;

            var selectedItem = NavListBox.SelectedItem as ListBoxItem;
            if (selectedItem == null) return;

            var tag = selectedItem.Tag?.ToString();

            if (_generalPage != null) _generalPage.IsVisible = tag == "general";
            if (_appearancePage != null) _appearancePage.IsVisible = tag == "appearance";
            if (_penPage != null) _penPage.IsVisible = tag == "pen";
            if (_ocrPage != null)
            {
                _ocrPage.IsVisible = tag == "ocr";
                if (tag == "ocr") RefreshOcrStatus();
            }
            if (_aboutPage != null) _aboutPage.IsVisible = tag == "about";
        }

        private void LoadPenSettings()
        {
            var config = Config.Load();
            var settings = config.PenSettings ?? new PenSettings();

            if (_denominatorInput != null) _denominatorInput.Value = settings.Denominator;
            if (_ratioMinInput != null) _ratioMinInput.Value = (decimal)settings.RatioMin;
            if (_ratioMaxInput != null) _ratioMaxInput.Value = (decimal)settings.RatioMax;
            if (_speedThresholdFastInput != null) _speedThresholdFastInput.Value = (decimal)settings.SpeedThresholdFast;
            if (_speedThresholdSlowInput != null) _speedThresholdSlowInput.Value = (decimal)settings.SpeedThresholdSlow;
            if (_ratioChangeCoefficientInput != null) _ratioChangeCoefficientInput.Value = (decimal)settings.RatioChangeCoefficient;
            if (_enablePalmEraserInput != null) _enablePalmEraserInput.IsChecked = settings.EnablePalmEraser;
            if (_palmEraserThresholdInput != null) _palmEraserThresholdInput.Value = (decimal)settings.PalmEraserThreshold;
        }

        private void LoadThemeSettings()
        {
            var config = Config.Load();
            var theme = config.Theme ?? "Dark";

            if (_themeComboBox != null)
            {
                for (int i = 0; i < _themeComboBox.ItemCount; i++)
                {
                    var item = _themeComboBox.Items[i] as ComboBoxItem;
                    if (item?.Tag?.ToString() == theme)
                    {
                        _themeComboBox.SelectedIndex = i;
                        break;
                    }
                }
            }
        }

        /// <summary>加载常规设置（默认摄像头、板中板按钮）。</summary>
        private void LoadGeneralSettings()
        {
            var config = Config.Load();
            if (_showPipButtonInput != null) _showPipButtonInput.IsChecked = config.ShowPictureInPicture;
            if (_cameraKeepAliveInput != null) _cameraKeepAliveInput.IsChecked = config.CameraKeepAlive;
            PopulateCameraComboBox(config);

            // 外观：照片栏滚动条、启动图
            if (_showPhotoScrollbarInput != null) _showPhotoScrollbarInput.IsChecked = config.ShowPhotoPanelScrollbar;
            if (_pdfImportDpiInput != null) _pdfImportDpiInput.Value = config.PdfImportDpi;
            if (_bootImageApiInput != null) _bootImageApiInput.Text = BootImageUpdater.GetApiUrl();
            if (_bootLocalVersionText != null) _bootLocalVersionText.Text = BootImageUpdater.GetLocalVersion() ?? "(未安装)";
        }

        // ---------- 启动图 ----------

        /// <summary>刷新版本：请求 API 显示远端最新启动图版本。</summary>
        private async void RefreshBootVersion_Click(object? sender, RoutedEventArgs e)
        {
            if (_bootStatusText != null) _bootStatusText.Text = "正在获取版本...";
            var url = string.IsNullOrWhiteSpace(_bootImageApiInput?.Text) ? null : _bootImageApiInput.Text.Trim();
            var server = await BootImageUpdater.FetchServerInfoAsync(url);
            if (_bootStatusText == null) return;
            if (server == null || string.IsNullOrEmpty(server.Version))
            {
                _bootStatusText.Text = "获取版本失败，请检查网络或 API 地址";
                return;
            }
            _bootStatusText.Text = $"最新版本: {server.Version}";
        }

        /// <summary>更新启动图：跳过版本比对强制下载并应用。</summary>
        private async void UpdateBootImage_Click(object? sender, RoutedEventArgs e)
        {
            if (_bootStatusText != null) _bootStatusText.Text = "正在更新启动图...";
            var url = string.IsNullOrWhiteSpace(_bootImageApiInput?.Text) ? null : _bootImageApiInput.Text.Trim();
            var ok = await BootImageUpdater.CheckAndUpdateAsync(url, force: true);
            if (_bootLocalVersionText != null) _bootLocalVersionText.Text = BootImageUpdater.GetLocalVersion() ?? "(未安装)";
            if (_bootStatusText != null) _bootStatusText.Text = ok ? "启动图已更新" : "更新失败，请检查网络或 API 地址";
        }

        /// <summary>用缓存的摄像头列表填充下拉框。</summary>
        private void PopulateCameraComboBox(Config config)
        {
            if (_defaultCameraComboBox == null) return;
            _availableCameras = new List<int>(config.AvailableCameraIndices);
            _defaultCameraComboBox.Items.Clear();

            if (_availableCameras.Count == 0)
            {
                _defaultCameraComboBox.Items.Add(new ComboBoxItem { Content = "未检测到摄像头（请点击刷新）", IsEnabled = false });
                _defaultCameraComboBox.SelectedIndex = 0;
                return;
            }

            for (int i = 0; i < _availableCameras.Count; i++)
            {
                int deviceIdx = _availableCameras[i];
                string displayName = config.AvailableCameraNames.TryGetValue(deviceIdx, out var n) && !string.IsNullOrEmpty(n)
                    ? n
                    : $"摄像头 {deviceIdx}";
                _defaultCameraComboBox.Items.Add(new ComboBoxItem { Content = displayName });
            }

            int selected = config.CurrentCameraIndex;
            if (selected < 0 || selected >= _availableCameras.Count) selected = 0;
            _defaultCameraComboBox.SelectedIndex = selected;
        }

        /// <summary>重新扫描摄像头并刷新下拉框。</summary>
        private async void RefreshCameraList_Click(object? sender, RoutedEventArgs e)
        {
            if (_defaultCameraComboBox == null) return;
            if (sender is Button btn) btn.IsEnabled = false;
            _defaultCameraComboBox.Items.Clear();
            _defaultCameraComboBox.Items.Add(new ComboBoxItem { Content = "正在扫描摄像头...", IsEnabled = false });
            _defaultCameraComboBox.SelectedIndex = 0;

            var scanned = await System.Threading.Tasks.Task.Run(() =>
            {
                var cameras = new List<int>();
                for (int i = 0; i < 10; i++)
                {
                    try
                    {
                        using var test = new OpenCvSharp.VideoCapture(i, OpenCvSharp.VideoCaptureAPIs.MSMF);
                        if (test.IsOpened())
                            cameras.Add(i);
                    }
                    catch { }
                }
                return cameras;
            });

            var config = Config.Load();
            config.AvailableCameraIndices = scanned;
            config.AvailableCameraNames = _cameraService?.GetCameraDeviceNames() ?? new Dictionary<int, string>();
            config.LastScanTime = DateTime.Now;
            // 当前默认摄像头不在新列表中时回退到第一台
            int currentDevice = (config.CurrentCameraIndex >= 0 && config.CurrentCameraIndex < _availableCameras.Count)
                ? _availableCameras[config.CurrentCameraIndex] : -1;
            config.CurrentCameraIndex = scanned.IndexOf(currentDevice) >= 0 ? scanned.IndexOf(currentDevice) : 0;
            config.Save();

            PopulateCameraComboBox(config);
            if (sender is Button b) b.IsEnabled = true;
        }

        private void SavePenSettings_Click(object? sender, RoutedEventArgs e)
        {
            var config = Config.Load();
            config.PenSettings ??= new PenSettings();

            if (_denominatorInput != null) config.PenSettings.Denominator = (int)_denominatorInput.Value;
            if (_ratioMinInput != null) config.PenSettings.RatioMin = (float)_ratioMinInput.Value;
            if (_ratioMaxInput != null) config.PenSettings.RatioMax = (float)_ratioMaxInput.Value;
            if (_speedThresholdFastInput != null) config.PenSettings.SpeedThresholdFast = (float)_speedThresholdFastInput.Value;
            if (_speedThresholdSlowInput != null) config.PenSettings.SpeedThresholdSlow = (float)_speedThresholdSlowInput.Value;
            if (_ratioChangeCoefficientInput != null) config.PenSettings.RatioChangeCoefficient = (float)_ratioChangeCoefficientInput.Value;
            if (_enablePalmEraserInput != null) config.PenSettings.EnablePalmEraser = _enablePalmEraserInput.IsChecked ?? true;
            if (_palmEraserThresholdInput != null) config.PenSettings.PalmEraserThreshold = (double)_palmEraserThresholdInput.Value;

            config.Ocr ??= new OcrSettings();
            if (_enableOcrDirectoryInput != null) config.Ocr.EnableOcrDirectory = _enableOcrDirectoryInput.IsChecked ?? true;

            if (_showPipButtonInput != null) config.ShowPictureInPicture = _showPipButtonInput.IsChecked ?? false;
            if (_cameraKeepAliveInput != null) config.CameraKeepAlive = _cameraKeepAliveInput.IsChecked ?? false;
            if (_showPhotoScrollbarInput != null) config.ShowPhotoPanelScrollbar = _showPhotoScrollbarInput.IsChecked ?? true;
            if (_pdfImportDpiInput != null) config.PdfImportDpi = Math.Clamp((int)_pdfImportDpiInput.Value, 72, 600);
            if (_bootImageApiInput != null && !string.IsNullOrWhiteSpace(_bootImageApiInput.Text))
            {
                config.BootImageApiUrl = _bootImageApiInput.Text.Trim();
            }
            if (_defaultCameraComboBox != null && _defaultCameraComboBox.SelectedIndex >= 0 && _defaultCameraComboBox.SelectedIndex < _availableCameras.Count)
            {
                config.CurrentCameraIndex = _defaultCameraComboBox.SelectedIndex;
            }

            if (_themeComboBox != null)
            {
                var selectedItem = _themeComboBox.SelectedItem as ComboBoxItem;
                if (selectedItem != null)
                {
                    config.Theme = selectedItem.Tag?.ToString() ?? "Dark";

                    ThemeType themeType = config.Theme switch
                    {
                        "Light" => ThemeType.Light,
                        "LightMinimal" => ThemeType.LightMinimal,
                        "NoBackground" => ThemeType.NoBackground,
                        _ => ThemeType.Dark
                    };

                    ThemeManager.SetTheme(themeType);
                    if (Avalonia.Application.Current != null)
                    {
                        ThemeManager.ApplyTheme(Avalonia.Application.Current, themeType);
                    }
                }
            }

            config.Save();

            Close();
        }

        private void ResetPenSettings_Click(object? sender, RoutedEventArgs e)
        {
            var defaultSettings = new PenSettings();

            if (_denominatorInput != null) _denominatorInput.Value = defaultSettings.Denominator;
            if (_ratioMinInput != null) _ratioMinInput.Value = (decimal)defaultSettings.RatioMin;
            if (_ratioMaxInput != null) _ratioMaxInput.Value = (decimal)defaultSettings.RatioMax;
            if (_speedThresholdFastInput != null) _speedThresholdFastInput.Value = (decimal)defaultSettings.SpeedThresholdFast;
            if (_speedThresholdSlowInput != null) _speedThresholdSlowInput.Value = (decimal)defaultSettings.SpeedThresholdSlow;
            if (_ratioChangeCoefficientInput != null) _ratioChangeCoefficientInput.Value = (decimal)defaultSettings.RatioChangeCoefficient;
            if (_enablePalmEraserInput != null) _enablePalmEraserInput.IsChecked = defaultSettings.EnablePalmEraser;
            if (_palmEraserThresholdInput != null) _palmEraserThresholdInput.Value = (decimal)defaultSettings.PalmEraserThreshold;
        }

        // ---------- OCR 模型管理 ----------
        private void LoadOcrSettings()
        {
            var config = Config.Load();
            config.Ocr ??= new OcrSettings();
            if (_enableOcrDirectoryInput != null) _enableOcrDirectoryInput.IsChecked = config.Ocr.EnableOcrDirectory;
            if (_ocrModelsDirText != null) _ocrModelsDirText.Text = OcrService.ModelsRoot;
            if (_customDetInput != null) _customDetInput.Text = config.Ocr.CustomDetPath ?? "";
            if (_customRecInput != null) _customRecInput.Text = config.Ocr.CustomRecPath ?? "";
            if (_customDictInput != null) _customDictInput.Text = config.Ocr.CustomDictPath ?? "";

            ApplyModelSetSelection(config.Ocr.ModelSet);
            UpdateModelSetStatuses();
            RefreshOcrStatus();
        }

        private void ApplyModelSetSelection(string key)
        {
            var custom = key == "custom";
            if (_modelSetMobile != null) _modelSetMobile.IsChecked = key == "v4-mobile";
            if (_modelSetServer != null) _modelSetServer.IsChecked = key == "v4-server";
            if (_modelSetHybrid != null) _modelSetHybrid.IsChecked = key == "v4-hybrid";
            if (_modelSetCustom != null) _modelSetCustom.IsChecked = custom;
            if (_customModelPanel != null) _customModelPanel.IsVisible = custom;
        }

        private void UpdateModelSetStatuses()
        {
            var cfg = Config.Load().Ocr;

            var primaryBrush = Avalonia.Application.Current?.FindResource("ThemeTextPrimary") as Avalonia.Media.IBrush;
            void UpdatePaddle(RadioButton? radio, TextBlock? status, Button? dlBtn, string key)
            {
                var downloaded = OcrService.Instance.IsSetDownloaded(key);
                var active = cfg.ModelSet == key;
                if (status != null)
                    status.Text = downloaded ? (active ? "✓ 当前 · 已下载" : "✓ 已下载") : "未下载";
                if (dlBtn != null)
                    dlBtn.Content = new TextBlock { Text = downloaded ? "重新下载" : "下载", FontSize = 12, Foreground = primaryBrush, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            }
            UpdatePaddle(_modelSetMobile, _modelSetMobileStatus, _modelSetMobileDownload, "v4-mobile");
            UpdatePaddle(_modelSetServer, _modelSetServerStatus, _modelSetServerDownload, "v4-server");
            UpdatePaddle(_modelSetHybrid, _modelSetHybridStatus, _modelSetHybridDownload, "v4-hybrid");
        }

        /// <summary>选择某个模型集（单选切换）。</summary>
        private void ModelSetRadio_Click(object? sender, RoutedEventArgs e)
        {
            var config = Config.Load();
            config.Ocr ??= new OcrSettings();

            string key = sender == _modelSetServer ? "v4-server"
                       : sender == _modelSetHybrid ? "v4-hybrid"
                       : sender == _modelSetCustom ? "custom"
                       : "v4-mobile";
            // 切到 custom 时，先把自定义路径写回
            if (key == "custom")
            {
                if (_customDetInput != null) config.Ocr.CustomDetPath = string.IsNullOrWhiteSpace(_customDetInput.Text) ? null : _customDetInput.Text;
                if (_customRecInput != null) config.Ocr.CustomRecPath = string.IsNullOrWhiteSpace(_customRecInput.Text) ? null : _customRecInput.Text;
                if (_customDictInput != null) config.Ocr.CustomDictPath = string.IsNullOrWhiteSpace(_customDictInput.Text) ? null : _customDictInput.Text;
            }
            config.Ocr.ModelSet = key;
            config.Save();
            ApplyModelSetSelection(key);
            UpdateModelSetStatuses();
            RefreshOcrStatus();
        }

        /// <summary>下载指定模型集。</summary>
        private void ModelSetDownload_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string key)
                _ = DownloadSetAsync(key);
        }

        /// <summary>重新下载当前活动模型集。</summary>
        private void OcrRedownload_Click(object? sender, RoutedEventArgs e)
        {
            var cfg = Config.Load().Ocr;
            var key = cfg.ModelSet;
            if (key == "custom")
            {
                if (_ocrDownloadStatus != null) { _ocrDownloadStatus.IsVisible = true; _ocrDownloadStatus.Text = "自定义模式不提供下载，请用浏览按钮指定本地文件"; }
                return;
            }
            _ = DownloadSetAsync(key);
        }

        private async System.Threading.Tasks.Task DownloadSetAsync(string key)
        {
            if (_modelSetMobileDownload != null) _modelSetMobileDownload.IsEnabled = false;
            if (_modelSetServerDownload != null) _modelSetServerDownload.IsEnabled = false;
            if (_modelSetHybridDownload != null) _modelSetHybridDownload.IsEnabled = false;
            if (_ocrRedownloadBtn != null) _ocrRedownloadBtn.IsEnabled = false;
            if (_ocrDownloadProgress != null) { _ocrDownloadProgress.IsVisible = true; _ocrDownloadProgress.IsIndeterminate = true; _ocrDownloadProgress.Value = 0; }
            if (_ocrDownloadStatus != null) { _ocrDownloadStatus.IsVisible = true; _ocrDownloadStatus.Text = "准备下载..."; }

            try
            {
                var progress = new Progress<(int Percent, string Status)>(p =>
                {
                    if (_ocrDownloadProgress != null) { _ocrDownloadProgress.IsIndeterminate = p.Percent <= 0; _ocrDownloadProgress.Value = p.Percent; }
                    if (_ocrDownloadStatus != null) _ocrDownloadStatus.Text = p.Status;
                });

                bool ok = await OcrService.Instance.DownloadSetAsync(key, progress, CancellationToken.None);

                if (_ocrDownloadProgress != null) { _ocrDownloadProgress.IsIndeterminate = false; _ocrDownloadProgress.Value = ok ? 100 : 0; }
                if (_ocrDownloadStatus != null) _ocrDownloadStatus.Text = ok ? "下载完成" : "下载失败，请检查网络";
            }
            catch (Exception ex)
            {
                if (_ocrDownloadStatus != null) _ocrDownloadStatus.Text = $"出错: {ex.Message}";
            }
            finally
            {
                if (_modelSetMobileDownload != null) _modelSetMobileDownload.IsEnabled = true;
                if (_modelSetServerDownload != null) _modelSetServerDownload.IsEnabled = true;
                if (_modelSetHybridDownload != null) _modelSetHybridDownload.IsEnabled = true;
                if (_ocrRedownloadBtn != null) _ocrRedownloadBtn.IsEnabled = true;
                UpdateModelSetStatuses();
                RefreshOcrStatus();
            }
        }

        /// <summary>浏览选择自定义模型文件。</summary>
        private async void BrowseCustom_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button b || b.Tag is not string which) return;
            var stor = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (stor == null) return;
            var filters = which == "dict"
                ? new[] { new FilePickerFileType("文本文件") { Patterns = new[] { "*.txt" } } }
                : new[] { new FilePickerFileType("ONNX 模型") { Patterns = new[] { "*.onnx" } } };
            var files = await stor.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = which == "dict" ? "选择字典文件" : "选择 ONNX 模型文件",
                AllowMultiple = false,
                FileTypeFilter = filters
            });
            if (files.Count == 0) return;
            var path = files[0].Path.LocalPath;
            switch (which)
            {
                case "det": if (_customDetInput != null) _customDetInput.Text = path; break;
                case "rec": if (_customRecInput != null) _customRecInput.Text = path; break;
                case "dict": if (_customDictInput != null) _customDictInput.Text = path; break;
            }
            // 选择文件后立即落到配置（保持 custom 选中状态）
            var config = Config.Load();
            config.Ocr ??= new OcrSettings();
            if (which == "det") config.Ocr.CustomDetPath = path;
            if (which == "rec") config.Ocr.CustomRecPath = path;
            if (which == "dict") config.Ocr.CustomDictPath = path;
            config.Ocr.ModelSet = "custom";
            config.Save();
            ApplyModelSetSelection("custom");
            UpdateModelSetStatuses();
            RefreshOcrStatus();
        }

        /// <summary>刷新当前活动模型文件状态列表。</summary>
        private void RefreshOcrStatus()
        {
            if (_ocrModelListPanel == null) return;
            _ocrModelListPanel.Children.Clear();

            foreach (var (name, path, ready, size) in OcrService.Instance.GetModelStatus())
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("180,*,Auto") };
                var n = new TextBlock
                {
                    Text = name,
                    Foreground = Avalonia.Application.Current?.FindResource("ThemeTextSecondary") as Avalonia.Media.IBrush,
                    FontSize = 13,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                };
                n.SetValue(Grid.ColumnProperty, 0);
                var p = new TextBlock
                {
                    Text = string.IsNullOrEmpty(path) ? "(未设置)" : path,
                    Foreground = Avalonia.Application.Current?.FindResource("ThemeTextTertiary") as Avalonia.Media.IBrush,
                    FontSize = 11,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis
                };
                p.SetValue(Grid.ColumnProperty, 1);
                var s = new TextBlock
                {
                    Text = ready ? (size > 0 ? $"✓ {size / 1024.0 / 1024.0:F2} MB" : "✓") : "✗ 缺失",
                    Foreground = Avalonia.Application.Current?.FindResource(ready ? "ThemePrimary" : "ThemeTextTertiary") as Avalonia.Media.IBrush,
                    FontSize = 12,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                };
                s.SetValue(Grid.ColumnProperty, 2);
                row.Children.Add(n);
                row.Children.Add(p);
                row.Children.Add(s);
                _ocrModelListPanel.Children.Add(row);
            }
        }

        private void LoadSystemInfo()
        {
            try
            {
                var searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_BaseBoard");
                foreach (ManagementObject obj in searcher.Get())
                {
                    var serial = obj["SerialNumber"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(serial))
                    {
                        if (_motherboardSerialText != null)
                            _motherboardSerialText.Text = serial;
                        break;
                    }
                }
                if (_motherboardSerialText != null && _motherboardSerialText.Text == "正在获取...")
                {
                    _motherboardSerialText.Text = "未获取到";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SettingsWindow] 获取主板序列号失败: {ex.Message}");
                if (_motherboardSerialText != null)
                    _motherboardSerialText.Text = "获取失败";
            }

            var uuid = LicenseManager.Instance.CurrentUuid;
            if (_uuidText != null)
            {
                if (!string.IsNullOrEmpty(uuid))
                {
                    _uuidText.Text = uuid;
                }
                else
                {
                    var motherboardSerial = LicenseManager.Instance.MotherboardSerial;
                    if (!string.IsNullOrEmpty(motherboardSerial))
                    {
                        _uuidText.Text = "正在向服务器注册...";
                        _ = TryGetUuidAsync();
                    }
                    else
                    {
                        _uuidText.Text = "未注册 (无法获取主板序列号)";
                    }
                }
            }
        }

        private async System.Threading.Tasks.Task TryGetUuidAsync()
        {
            try
            {
                var uuid = await LicenseManager.Instance.GetOrCreateLicenseAsync();
                if (_uuidText != null)
                {
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        _uuidText.Text = uuid;
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SettingsWindow] 获取UUID失败: {ex.Message}");
                if (_uuidText != null)
                {
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        _uuidText.Text = $"获取失败: {ex.Message}";
                    });
                }
            }
        }
    }
}
