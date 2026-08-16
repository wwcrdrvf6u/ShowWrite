using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using System;
using System.Management;

namespace ShowWrite
{
    public partial class SettingsWindow : Window
    {
        private StackPanel? _generalPage;
        private StackPanel? _penPage;
        private StackPanel? _cameraPage;
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
        private SplitView? _settingsSplitView;

        public SettingsWindow()
        {
            InitializeComponent();

            RequestedThemeVariant = ThemeManager.CurrentTheme == ThemeType.Dark
                ? ThemeVariant.Dark
                : ThemeVariant.Light;

            _generalPage = this.FindControl<StackPanel>("GeneralPage");
            _penPage = this.FindControl<StackPanel>("PenPage");
            _cameraPage = this.FindControl<StackPanel>("CameraPage");
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
            _settingsSplitView = this.FindControl<SplitView>("SettingsSplitView");

            LoadPenSettings();
            LoadThemeSettings();
            LoadSystemInfo();
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
            if (_penPage != null) _penPage.IsVisible = tag == "pen";
            if (_cameraPage != null) _cameraPage.IsVisible = tag == "camera";
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
