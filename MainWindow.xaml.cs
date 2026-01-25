using AForge.Imaging.Filters;
using Newtonsoft.Json;
using ShowWrite.Models;
using ShowWrite.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

// 为有冲突的类型添加明确的别名
using WinPoint = System.Windows.Point;
using WinCursors = System.Windows.Input.Cursors;
using WinComboBox = System.Windows.Controls.ComboBox;
using WinOrientation = System.Windows.Controls.Orientation;
using WinButton = System.Windows.Controls.Button;
using WinBrushes = System.Windows.Media.Brushes;
using WinBrush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using MessageBox = System.Windows.MessageBox;
using SystemDrawingImage = System.Drawing.Image;  // 重命名 System.Drawing.Image
using SystemDrawingBrushes = System.Drawing.Brushes;  // 重命名 System.Drawing.Brushes
using WpfImage = System.Windows.Controls.Image;
using Binding = System.Windows.Data.Binding;  // 为 WPF Image 添加别名

namespace ShowWrite
{
    public partial class MainWindow : Window
    {
        // 管理器实例
        private readonly VideoService _videoService = new();
        private DrawingManager _drawingManager;
        private CameraManager _cameraManager;
        private PanZoomManager _panZoomManager;
        private MemoryManager _memoryManager;
        private FrameProcessor _frameProcessor;
        private TouchManager _touchManager;
        private LogManager _logManager;
        private PhotoPopupManager _photoPopupManager;

        // 数据集合
        private readonly ObservableCollection<PhotoWithStrokes> _photos = new();
        private StrokeCollection _liveStrokes = new StrokeCollection();

        // 状态变量
        private bool _isLiveMode = true;
        private bool _isClosing = false;
        private AppConfig config = new AppConfig();

        // 视频帧接收状态
        private bool _isFirstFrameProcessed = false;

        // UI相关
        private SolidColorBrush _noCameraBackground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 40, 40));
        private Button _currentSelectedColorButton = null;
        private string _currentPenColor = "Black";

        // 配置文件路径
        private readonly string configPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");

        // 双击检测
        private DateTime _lastClickTime = DateTime.MinValue;
        private const int DoubleClickDelay = 300; // 毫秒

        // 画面调节参数
        private double _brightness = 0.0;
        private double _contrast = 0.0;
        private int _rotation = 0;
        private bool _mirrorHorizontal = false;
        private bool _mirrorVertical = false;

        // 梯形校正相关
        private bool _isPerspectiveCorrectionMode = false;
        private bool _isEnteringCorrectionMode = false; // 防止重复进入的保护机制
        private System.Drawing.Bitmap _originalCorrectionFrame = null;
        private int _draggingPointIndex = -1;
        private WinPoint[] _correctionPoints = new WinPoint[4];
        private bool _isCorrectionModeInitialized = false;

        // 启动图相关
        private SplashWindow _splashWindow;
        private bool _isSplashShown = false;
        private const int SPLASH_DISPLAY_TIME = 2000; // 启动图显示时间（毫秒）

        // 主题相关
        private ResourceDictionary _currentTheme;

        public MainWindow()
        {
            // 显示启动图
            ShowSplashScreen();

            // 初始化日志系统
            Logger.Initialize(minLogLevel: LogLevel.Debug);
            Logger.Info("MainWindow", "应用程序启动");
            _logManager = new LogManager();

            InitializeComponent();

            // 初始化管理器
            InitializeManagers();

            // 初始化UI和数据绑定
            InitializeUI();

            // 加载配置和启动
            LoadAndStart();

            // 添加窗口加载完成事件
            this.Loaded += MainWindow_Loaded;
            this.SizeChanged += MainWindow_SizeChanged;

            Logger.Info("MainWindow", "主窗口初始化完成");

            // 关闭启动图
            CloseSplashScreen();
        }

        /// <summary>
        /// 显示启动图
        /// </summary>
        private void ShowSplashScreen()
        {
            try
            {
                Logger.Debug("MainWindow", "显示启动图");

                // 创建并显示启动窗口
                _splashWindow = new SplashWindow();
                _splashWindow.Show();
                _isSplashShown = true;

                // 让启动图窗口获得焦点并确保在前台
                _splashWindow.Activate();
                _splashWindow.Topmost = true;

                // 强制更新UI
                _splashWindow.UpdateLayout();

                Logger.Debug("MainWindow", "启动图显示成功");
            }
            catch (Exception ex)
            {
                Logger.Error("MainWindow", $"显示启动图失败: {ex.Message}", ex);
                _splashWindow = null;
            }
        }

        /// <summary>
        /// 关闭启动图
        /// </summary>
        private void CloseSplashScreen()
        {
            try
            {
                if (_splashWindow != null && _isSplashShown)
                {
                    // 计算最小显示时间
                    if (_splashWindow.StartTime.HasValue)
                    {
                        var elapsed = DateTime.Now - _splashWindow.StartTime.Value;
                        int remainingTime = Math.Max(0, SPLASH_DISPLAY_TIME - (int)elapsed.TotalMilliseconds);

                        if (remainingTime > 0)
                        {
                            // 等待剩余时间
                            Task.Delay(remainingTime).ContinueWith(_ =>
                            {
                                Dispatcher.Invoke(() =>
                                {
                                    CloseSplashWindowInternal();
                                });
                            });
                            return;
                        }
                    }

                    CloseSplashWindowInternal();
                }
            }
            catch (Exception ex)
            {
                Logger.Error("MainWindow", $"关闭启动图失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 内部关闭启动图窗口
        /// </summary>
        private void CloseSplashWindowInternal()
        {
            try
            {
                if (_splashWindow != null)
                {
                    _splashWindow.CloseSplash();
                    _splashWindow = null;
                    _isSplashShown = false;
                    Logger.Debug("MainWindow", "启动图已关闭");
                }
            }
            catch (Exception ex)
            {
                Logger.Error("MainWindow", $"内部关闭启动图失败: {ex.Message}", ex);
                try
                {
                    _splashWindow?.Close();
                    _splashWindow = null;
                    _isSplashShown = false;
                }
                catch { }
            }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 确保校正画布有正确的尺寸
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (CorrectionCanvas != null && VideoArea != null)
                {
                    CorrectionCanvas.Width = VideoArea.ActualWidth;
                    CorrectionCanvas.Height = VideoArea.ActualHeight;
                }
            }), DispatcherPriority.Loaded);

            // 确保主窗口在前台
            this.Activate();
            this.Topmost = true;
            this.Topmost = false;
            this.Focus();
        }

        private void SwitchTheme(bool useDarkTheme)
        {
            // 清除现有资源
            this.Resources.MergedDictionaries.Clear();

            // 创建新的资源字典
            var resourceDictionary = new ResourceDictionary();

            // 根据主题加载对应的资源文件
            if (useDarkTheme)
            {
                resourceDictionary.MergedDictionaries.Add(
                    new ResourceDictionary() { Source = new Uri("themes/DarkTheme.xaml", UriKind.Relative) });
            }
            else
            {
                resourceDictionary.MergedDictionaries.Add(
                    new ResourceDictionary() { Source = new Uri("themes/LightTheme.xaml", UriKind.Relative) });
            }

            // 添加画笔设置按钮样式
            var penSettingsStyle = new Style(typeof(Button));
            penSettingsStyle.Setters.Add(new Setter(Button.WidthProperty, 32.0));
            penSettingsStyle.Setters.Add(new Setter(Button.HeightProperty, 32.0));
            penSettingsStyle.Setters.Add(new Setter(Button.MarginProperty, new Thickness(2)));
            penSettingsStyle.Setters.Add(new Setter(Button.BorderThicknessProperty, new Thickness(1)));
            penSettingsStyle.Setters.Add(new Setter(Button.BorderBrushProperty, new SolidColorBrush(System.Windows.Media.Color.FromRgb(85, 85, 85))));
            resourceDictionary.Add("PenSettingsButtonStyle", penSettingsStyle);

            // 应用新的资源字典
            this.Resources = resourceDictionary;
        }

        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // 如果在校正模式下，重新初始化校正点位置
            if (_isPerspectiveCorrectionMode && _isCorrectionModeInitialized)
            {
                InitializeCorrectionPoints();
            }

            // 如果照片悬浮窗打开，重新定位
            if (PhotoPopup != null && PhotoPopup.IsOpen)
            {
                _photoPopupManager.RepositionPhotoPopup();
            }
        }

        /// <summary>
        /// 初始化所有管理器
        /// </summary>
        private void InitializeManagers()
        {
            try
            {
                Logger.Info("MainWindow", "开始初始化管理器");

                // 1. 初始化配置
                if (config == null) config = new AppConfig();

                // 2. 初始化绘制管理器
                _drawingManager = new DrawingManager(Ink, VideoArea, this);

                // 3. 初始化摄像头管理器
                _cameraManager = new CameraManager(_videoService, config);

                // 4. 初始化内存管理器
                _memoryManager = new MemoryManager();

                // 5. 初始化帧处理器
                _frameProcessor = new FrameProcessor(_cameraManager, _memoryManager);

                // 6. 初始化平移缩放管理器
                _panZoomManager = new PanZoomManager(ZoomTransform, PanTransform, VideoArea, _drawingManager);

                // 7. 初始化触控管理器
                _touchManager = new TouchManager(_drawingManager);

                // 8. 初始化照片悬浮窗管理器
                InitializePhotoPopupManager();

                // 订阅事件
                SubscribeToEvents();

                Logger.Info("MainWindow", "管理器初始化完成");
            }
            catch (Exception ex)
            {
                Logger.Error("MainWindow", $"初始化管理器失败: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// 初始化照片悬浮窗管理器
        /// </summary>
        private void InitializePhotoPopupManager()
        {
            try
            {
                _photoPopupManager = new PhotoPopupManager(
                    PhotoPopup,
                    PhotoList,
                    this,
                    _photos,
                    _drawingManager,
                    _cameraManager,
                    _memoryManager,
                    _frameProcessor,
                    _panZoomManager,
                    _logManager);

                // 订阅照片悬浮窗管理器事件
                _photoPopupManager.PhotoSelected += OnPhotoSelected;
                _photoPopupManager.BackToLiveRequested += OnBackToLiveRequested;
                _photoPopupManager.SaveImageRequested += OnSaveImageRequested;

                Logger.Info("MainWindow", "照片悬浮窗管理器初始化完成");
            }
            catch (Exception ex)
            {
                Logger.Error("MainWindow", $"初始化照片悬浮窗管理器失败: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// 照片选择事件处理
        /// </summary>
        private void OnPhotoSelected(PhotoWithStrokes photo)
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    // 显示选中的照片
                    VideoImage.Source = photo?.Image;

                    Logger.Debug("MainWindow", $"照片选择事件处理完成");
                }
                catch (Exception ex)
                {
                    Logger.Error("MainWindow", $"处理照片选择事件失败: {ex.Message}", ex);
                }
            });
        }

        /// <summary>
        /// 返回实时模式请求处理
        /// </summary>
        private void OnBackToLiveRequested()
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    // 重置视频帧记录状态
                    _isFirstFrameProcessed = false;
                    Logger.ResetVideoFrameLogging();

                    // 重启摄像头
                    _cameraManager.RestartCamera();

                    _isLiveMode = true;

                    Logger.Info("MainWindow", "返回实时模式请求处理完成");
                }
                catch (Exception ex)
                {
                    Logger.Error("MainWindow", $"处理返回实时模式请求失败: {ex.Message}", ex);
                }
            });
        }

        /// <summary>
        /// 保存图片请求处理
        /// </summary>
        private void OnSaveImageRequested()
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    // 调用原有的保存图片逻辑
                    SaveImage_Click(null, null);

                    Logger.Debug("MainWindow", "保存图片请求处理完成");
                }
                catch (Exception ex)
                {
                    Logger.Error("MainWindow", $"处理保存图片请求失败: {ex.Message}", ex);
                }
            });
        }

        /// <summary>
        /// 订阅管理器事件
        /// </summary>
        private void SubscribeToEvents()
        {
            try
            {
                Logger.Debug("MainWindow", "开始订阅事件");

                // 摄像头帧事件
                _cameraManager.OnNewFrameProcessed += OnCameraFrameReceived;

                // 绘制管理器事件
                _drawingManager.OnSDKTouchAreaChanged += OnSDKTouchAreaChanged;

                // 触控管理器事件
                _touchManager.OnTouchCountChanged += OnTouchCountChanged;
                _touchManager.OnTouchAreaChanged += OnTouchAreaChanged;
                _touchManager.OnTouchCenterChanged += OnTouchCenterChanged;

                Logger.Debug("MainWindow", "事件订阅完成");
            }
            catch (Exception ex)
            {
                Logger.Error("MainWindow", $"订阅事件失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 取消所有事件订阅
        /// </summary>
        private void UnsubscribeAllEvents()
        {
            try
            {
                Logger.Info("MainWindow", "开始取消所有事件订阅");

                // 取消摄像头管理器事件
                if (_cameraManager != null)
                {
                    _cameraManager.OnNewFrameProcessed -= OnCameraFrameReceived;
                }

                // 取消绘制管理器事件
                if (_drawingManager != null)
                {
                    _drawingManager.OnSDKTouchAreaChanged -= OnSDKTouchAreaChanged;
                }

                // 取消触控管理器事件
                if (_touchManager != null)
                {
                    _touchManager.OnTouchCountChanged -= OnTouchCountChanged;
                    _touchManager.OnTouchAreaChanged -= OnTouchAreaChanged;
                    _touchManager.OnTouchCenterChanged -= OnTouchCenterChanged;
                }

                // 取消照片悬浮窗管理器事件
                if (_photoPopupManager != null)
                {
                    _photoPopupManager.PhotoSelected -= OnPhotoSelected;
                    _photoPopupManager.BackToLiveRequested -= OnBackToLiveRequested;
                    _photoPopupManager.SaveImageRequested -= OnSaveImageRequested;
                }

                // 取消窗口事件
                this.Loaded -= MainWindow_Loaded;
                this.SizeChanged -= MainWindow_SizeChanged;

                Logger.Info("MainWindow", "所有事件订阅已取消");
            }
            catch (Exception ex)
            {
                Logger.Error("MainWindow", $"取消事件订阅失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 初始化UI和数据绑定
        /// </summary>
        private void InitializeUI()
        {
            try
            {
                Logger.Debug("MainWindow", "开始初始化UI");

                // 初始化实时模式笔迹
                _drawingManager.SwitchToPhotoStrokes(_liveStrokes);

                // 应用窗口设置
                WindowStyle = WindowStyle.None;
                WindowState = config.StartMaximized ? WindowState.Maximized : WindowState.Normal;

                // 应用绘制管理器配置
                _drawingManager.ApplyConfig(config);

                // 初始化UI组件
                InitializePenSettingsPopup();
                InitializeTouchInfoPopup();

                // 初始化画笔颜色选择器
                InitializePenColorSelector();

                // 开始触控跟踪
                _touchManager.StartTracking();

                Logger.Debug("MainWindow", "UI初始化完成");
            }
            catch (Exception ex)
            {
                Logger.Error("MainWindow", $"初始化UI失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 加载配置和启动应用
        /// </summary>
        private void LoadAndStart()
        {
            try
            {
                Logger.Debug("MainWindow", "开始加载配置和启动");

                // 加载配置
                LoadConfig();

                // 应用主题
                ApplyTheme();

                // 检查摄像头可用性
                if (!_cameraManager.CheckCameraAvailability())
                {
                    ShowNoCameraBackground();
                }
                else if (config.AutoStartCamera)
                {
                    StartCameraWithFallback();

                    // 启动后应用摄像头配置
                    ApplyCameraConfigOnStartup();
                }

                // 显示 TouchSDK 状态
                UpdateTouchSDKStatus();

                // 调试图层可见性
                TestLayerVisibility();

                Logger.Debug("MainWindow", "配置加载和启动完成");
            }
            catch (Exception ex)
            {
                Logger.Error("MainWindow", $"加载配置和启动失败: {ex.Message}", ex);
            }
        }

        #region 初始化方法

        /// <summary>
        /// 初始化画笔设置悬浮窗
        /// </summary>
        private void InitializePenSettingsPopup()
        {
            try
            {
                // 设置初始笔宽并保存原始宽度
                _panZoomManager.SetOriginalPenWidth(_drawingManager.UserPenWidth);
                PenWidthSlider.Value = _drawingManager.UserPenWidth;
                PenWidthValue.Text = _drawingManager.UserPenWidth.ToString("0");

                Logger.Debug("MainWindow", "画笔设置悬浮窗初始化完成");
            }
            catch (Exception ex)
            {
                Logger.Error("MainWindow", $"初始化画笔设置悬浮窗失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 初始化触控信息悬浮窗
        /// </summary>
        private void InitializeTouchInfoPopup()
        {
            try
            {
                // 设置悬浮窗初始位置在右上角
                TouchInfoPopup.HorizontalOffset = SystemParameters.PrimaryScreenWidth - 200;
                TouchInfoPopup.VerticalOffset = 50;

                Logger.Debug("MainWindow", "触控信息悬浮窗初始化完成");
            }
            catch (Exception ex)
            {
                Logger.Error("MainWindow", $"初始化触控信息悬浮窗失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 图层可见性测试方法
        /// </summary>
        private void TestLayerVisibility()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    Logger.Debug("MainWindow", "=== 图层可见性测试 ===");
                    Logger.Debug("MainWindow", $"VideoArea 子元素数量: {VisualTreeHelper.GetChildrenCount(VideoArea)}");

                    for (int i = 0; i < VisualTreeHelper.GetChildrenCount(VideoArea); i++)
                    {
                        var child = VisualTreeHelper.GetChild(VideoArea, i);
                        Logger.Debug("MainWindow", $"子元素 {i}: {child.GetType().Name}, 可见性: {((UIElement)child).Visibility}");
                    }

                    Logger.Debug("MainWindow", $"VideoImage 源: {VideoImage.Source}");
                    Logger.Debug("MainWindow", $"VideoImage 渲染尺寸: {VideoImage.RenderSize}");
                    Logger.Debug("MainWindow", $"InkCanvas 背景: {Ink.Background}");
                    Logger.Debug("MainWindow", $"InkCanvas 默认绘制属性: {Ink.DefaultDrawingAttributes.Color}, {Ink.DefaultDrawingAttributes.Width}");

                    Logger.Debug("MainWindow", "=== 图层可见性测试结束 ===");
                }
                catch (Exception ex)
                {
                    Logger.Error("MainWindow", $"图层可见性测试失败: {ex.Message}", ex);
                }
            }), DispatcherPriority.Loaded);
        }

        #endregion

        #region 事件处理方法

        /// <summary>
        /// 摄像头帧接收事件
        /// </summary>
        private void OnCameraFrameReceived(System.Drawing.Bitmap frame)
        {
            if (_isClosing || !_isLiveMode || _isPerspectiveCorrectionMode)
            {
                _memoryManager.DisposeFrame(frame, true);
                return;
            }

            Dispatcher.Invoke(() =>
            {
                if (_isLiveMode && !_isClosing && !_isPerspectiveCorrectionMode)
                {
                    try
                    {
                        // 记录第一次视频帧接收状态
                        if (!_isFirstFrameProcessed)
                        {
                            bool frameValid = frame != null && frame.Width > 0 && frame.Height > 0;
                            string frameInfo = frameValid ?
                                $"帧尺寸: {frame.Width}x{frame.Height}" :
                                "无效帧";

                            Logger.LogVideoFrameStatus("Camera", frameValid, frameInfo);
                            _isFirstFrameProcessed = true;
                        }

                        // 处理并显示帧
                        var bitmapImage = _frameProcessor.ProcessFrameToBitmapImage(frame);
                        if (bitmapImage != null && VideoImage != null) // 添加 null 检查
                        {
                            VideoImage.Source = bitmapImage;
                        }

                        // 更新内存管理
                        _memoryManager.UpdateLastProcessedFrame(frame);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("MainWindow", $"视频帧处理错误: {ex.Message}", ex);
                    }
                    finally
                    {
                        // 释放当前帧
                        _memoryManager.DisposeFrame(frame);
                    }
                }
                else
                {
                    _memoryManager.DisposeFrame(frame, true);
                }
            });
        }

        /// <summary>
        /// TouchSDK 面积变化事件
        /// </summary>
        private void OnSDKTouchAreaChanged(double area)
        {
            if (_isClosing) return;

            Dispatcher.Invoke(() =>
            {
                _touchManager.UpdateSDKTouchArea(area);
                UpdateSDKTouchAreaDisplay();
            });
        }

        /// <summary>
        /// 触控点数变化事件
        /// </summary>
        private void OnTouchCountChanged(int count)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateTouchInfoDisplay();
            });
        }

        /// <summary>
        /// 触控面积变化事件
        /// </summary>
        private void OnTouchAreaChanged(double area)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateTouchInfoDisplay();
            });
        }

        /// <summary>
        /// 触控中心变化事件
        /// </summary>
        private void OnTouchCenterChanged(WinPoint center)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateTouchInfoDisplay();
            });
        }

        /// <summary>
        /// 更新触控信息显示
        /// </summary>
        private void UpdateTouchInfoDisplay()
        {
            try
            {
                if (TouchCountText != null)
                {
                    TouchCountText.Text = _touchManager.GetTouchSDKStatusText();
                }

                if (TouchAreaText != null)
                {
                    var area = _touchManager.TouchCount >= 3 ?
                        _touchManager.CalculatePolygonArea(_touchManager.GetCurrentTouchPoints()) : 0;
                    TouchAreaText.Text = $"面积: {area:F0} 像素²";
                }

                if (TouchCenterText != null)
                {
                    var center = _touchManager.CalculateTouchCenter();
                    TouchCenterText.Text = $"中心: ({center.X:F0}, {center.Y:F0})";
                }
            }
            catch (Exception ex)
            {
                Logger.Error("MainWindow", $"更新触控信息显示失败: {ex.Message}", ex);
            }
        }

        #endregion

        #region UI事件处理

        #region 画笔设置悬浮窗交互逻辑

        /// <summary>
        /// 画笔按钮点击事件（修改版）
        /// </summary>
        private void PenBtn_Click(object sender, RoutedEventArgs e)
        {
            // 如果当前不是画笔模式，切换到画笔模式并打开悬浮窗
            if (_drawingManager.CurrentMode != DrawingManager.ToolMode.Pen)
            {
                SetMode(DrawingManager.ToolMode.Pen);

                // 打开画笔设置悬浮窗
                PenSettingsPopup.IsOpen = true;
                Logger.Debug("MainWindow", "切换到画笔模式并打开设置悬浮窗");
            }
            else
            {
                // 如果已经是画笔模式，切换悬浮窗的显示状态
                PenSettingsPopup.IsOpen = !PenSettingsPopup.IsOpen;
                Logger.Debug("MainWindow", $"画笔模式已选中，切换悬浮窗状态: {PenSettingsPopup.IsOpen}");
            }
        }

        /// <summary>
        /// VideoArea鼠标按下事件 - 添加悬浮窗自动隐藏逻辑
        /// </summary>
        private void VideoArea_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_isClosing || _isPerspectiveCorrectionMode) return;

            // 自动隐藏画笔设置悬浮窗
            if (PenSettingsPopup.IsOpen && _drawingManager.CurrentMode == DrawingManager.ToolMode.Pen)
            {
                PenSettingsPopup.IsOpen = false;
                Logger.Debug("MainWindow", "点击VideoArea，自动隐藏画笔设置悬浮窗");
            }

            // 自动隐藏照片悬浮窗
            if (PhotoPopup.IsOpen)
            {
                _photoPopupManager.HidePhotoPopup();
                Logger.Debug("MainWindow", "点击VideoArea，自动隐藏照片悬浮窗");
            }

            // 调用原有的鼠标事件处理
            _panZoomManager.HandleMouseDown(e, _drawingManager.CurrentMode);
            _drawingManager.HandleMouseDown(e);
        }

        /// <summary>
        /// VideoArea鼠标左键按下事件（双击对焦）- 添加悬浮窗自动隐藏逻辑
        /// </summary>
        private void VideoArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_isClosing || _isPerspectiveCorrectionMode) return;

            // 自动隐藏画笔设置悬浮窗
            if (PenSettingsPopup.IsOpen && _drawingManager.CurrentMode == DrawingManager.ToolMode.Pen)
            {
                PenSettingsPopup.IsOpen = false;
                Logger.Debug("MainWindow", "点击VideoArea，自动隐藏画笔设置悬浮窗");
            }

            // 自动隐藏照片悬浮窗
            if (PhotoPopup.IsOpen)
            {
                _photoPopupManager.HidePhotoPopup();
                Logger.Debug("MainWindow", "点击VideoArea，自动隐藏照片悬浮窗");
            }

            // 原有的双击检测逻辑
            var currentTime = DateTime.Now;
            var timeSinceLastClick = (currentTime - _lastClickTime).TotalMilliseconds;

            if (timeSinceLastClick <= DoubleClickDelay)
            {
                // 双击事件 - 自动对焦
                if (_drawingManager.CurrentMode == DrawingManager.ToolMode.Move)
                {
                    try
                    {
                        if (_cameraManager.IsCameraAvailable)
                        {
                            _cameraManager.AutoFocus();
                            Logger.Info("MainWindow", "触发自动对焦");
                            MessageBox.Show("已触发自动对焦。", "对焦");
                        }
                        else
                        {
                            Logger.Warning("MainWindow", "没有可用的摄像头，无法进行自动对焦");
                            MessageBox.Show("没有可用的摄像头，无法进行自动对焦。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("MainWindow", $"自动对焦失败: {ex.Message}", ex);
                        MessageBox.Show("自动对焦失败: " + ex.Message, "错误");
                    }
                }
                _lastClickTime = DateTime.MinValue; // 重置
            }
            else
            {
                _lastClickTime = currentTime;
            }

            // 调用绘制管理器的鼠标按下处理
            _drawingManager.HandleMouseDown(e);
        }

        #endregion

        private void VideoArea_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_isPerspectiveCorrectionMode) return;
            _panZoomManager.HandleMouseMove(e, _drawingManager.CurrentMode);
            _drawingManager.HandleMouseMove(e);
        }

        private void VideoArea_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isPerspectiveCorrectionMode) return;
            _panZoomManager.HandleMouseUp(e, _drawingManager.CurrentMode);
            _drawingManager.HandleMouseUp(e);
        }

        private void Window_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_isPerspectiveCorrectionMode) return;
            _panZoomManager.HandleMouseWheel(e, _drawingManager.CurrentMode, VideoArea);
            _drawingManager.HandleMouseWheel(e);
        }

        // 触控事件
        protected override void OnTouchDown(TouchEventArgs e)
        {
            if (_isClosing || _isPerspectiveCorrectionMode) return;
            base.OnTouchDown(e);
            _touchManager.HandleTouchDown(e, VideoArea);
            _drawingManager.HandleTouchDown(e);
        }

        protected override void OnTouchMove(TouchEventArgs e)
        {
            if (_isClosing || _isPerspectiveCorrectionMode) return;
            base.OnTouchMove(e);
            _touchManager.HandleTouchMove(e, VideoArea);
            _drawingManager.HandleTouchMove(e);
        }

        protected override void OnTouchUp(TouchEventArgs e)
        {
            if (_isClosing || _isPerspectiveCorrectionMode) return;
            base.OnTouchUp(e);
            _touchManager.HandleTouchUp(e);
            _drawingManager.HandleTouchUp(e);
        }

        // 手势操作
        private void VideoArea_ManipulationStarting(object sender, ManipulationStartingEventArgs e)
        {
            if (_isPerspectiveCorrectionMode) return;
            _panZoomManager.HandleManipulationStarting(e, _drawingManager.CurrentMode);
        }

        private void VideoArea_ManipulationDelta(object sender, ManipulationDeltaEventArgs e)
        {
            if (_isPerspectiveCorrectionMode) return;
            _panZoomManager.HandleManipulationDelta(e, _drawingManager.CurrentMode, VideoArea);
        }

        #endregion

        #region 梯形校正功能模块

        // ==================== 梯形校正核心功能 ====================

        /// <summary>
        /// 安全的进入梯形校正模式（带防重入）
        /// </summary>
        private async void SafeEnterPerspectiveCorrectionMode()
        {
            // 防重入检查
            if (_isEnteringCorrectionMode || _isPerspectiveCorrectionMode)
            {
                Logger.Warning("MainWindow", "校正模式正在进入或已在其中，忽略请求");
                return;
            }

            _isEnteringCorrectionMode = true;

            try
            {
                await Task.Delay(100); // 给UI一个响应时间

                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        EnterPerspectiveCorrectionMode();
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("MainWindow", $"安全进入校正模式失败: {ex.Message}", ex);
                        MessageBox.Show($"进入校正模式失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                });
            }
            finally
            {
                _isEnteringCorrectionMode = false;
            }
        }

        /// <summary>
        /// 进入梯形校正模式（修复版）
        /// </summary>
        private void EnterPerspectiveCorrectionMode()
        {
            if (_isClosing) return;

            try
            {
                // 检查是否已经在校正模式下
                if (_isPerspectiveCorrectionMode)
                {
                    Logger.Warning("MainWindow", "已在校正模式下，忽略重复请求");
                    return;
                }

                // 检查摄像头
                if (!_cameraManager.IsCameraAvailable)
                {
                    MessageBox.Show("没有可用的摄像头，无法使用梯形校正功能。", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                Logger.Info("MainWindow", "开始进入梯形校正模式");

                // 获取当前视频帧
                var frame = _cameraManager.GetCurrentFrame();
                if (frame == null)
                {
                    MessageBox.Show("无法获取摄像头画面。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                try
                {
                    // 保存当前帧用于校正
                    _originalCorrectionFrame?.Dispose();
                    _originalCorrectionFrame = (System.Drawing.Bitmap)frame.Clone();

                    // 暂停摄像头
                    _cameraManager.PauseCamera();

                    // 显示原始图像
                    var bitmapImage = _memoryManager.BitmapToBitmapImage(_originalCorrectionFrame);
                    VideoImage.Source = bitmapImage;

                    // 隐藏底部工具栏
                    BottomToolbar.Visibility = Visibility.Collapsed;

                    // 显示校正模式界面
                    PerspectiveCorrectionGrid.Visibility = Visibility.Visible;

                    // 初始化校正点位置
                    InitializeCorrectionPoints();

                    // 设置校正点事件
                    SetupCorrectionPointsEvents();

                    // 设置模式标志
                    _isPerspectiveCorrectionMode = true;
                    _isCorrectionModeInitialized = true;

                    // 禁用InkCanvas
                    Ink.IsEnabled = false;

                    // 更新校正UI
                    UpdateCorrectionUI();

                    Logger.Info("MainWindow", "已进入梯形校正模式");
                }
                catch (Exception ex)
                {
                    Logger.Error("MainWindow", $"初始化校正模式失败: {ex.Message}", ex);
                    MessageBox.Show($"初始化校正模式失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);

                    // 清理资源
                    if (_originalCorrectionFrame != null)
                    {
                        _originalCorrectionFrame.Dispose();
                        _originalCorrectionFrame = null;
                    }

                    // 恢复摄像头
                    _cameraManager.ResumeCamera();

                    // 重置状态
                    _isPerspectiveCorrectionMode = false;
                    _isCorrectionModeInitialized = false;
                }
                finally
                {
                    frame.Dispose();
                }
            }
            catch (Exception ex)
            {
                Logger.Error("MainWindow", $"进入梯形校正模式失败: {ex.Message}", ex);
                MessageBox.Show($"进入梯形校正模式失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 退出梯形校正模式（修复版）
        /// </summary>
        private void ExitPerspectiveCorrectionMode(bool applyCorrection = false)
        {
            try
            {
                Logger.Info("MainWindow", $"退出梯形校正模式，应用校正: {applyCorrection}");

                // 检查是否在校正模式下
                if (!_isPerspectiveCorrectionMode) return;

                // 清理校正点事件
                RemoveCorrectionPointsEvents();

                // 释放鼠标捕获（防止鼠标卡死）
                ReleaseAllMouseCaptures();

                // 启用InkCanvas
                Ink.IsEnabled = true;

                // 隐藏校正模式界面
                PerspectiveCorrectionGrid.Visibility = Visibility.Collapsed;

                // 显示底部工具栏
                BottomToolbar.Visibility = Visibility.Visible;

                // 重置模式标志
                _isPerspectiveCorrectionMode = false;
                _isCorrectionModeInitialized = false;

                // 重置拖动状态
                _draggingPointIndex = -1;

                // 释放背景帧
                if (_originalCorrectionFrame != null)
                {
                    _originalCorrectionFrame.Dispose();
                    _originalCorrectionFrame = null;
                }

                // 恢复摄像头
                if (_isLiveMode)
                {
                    _cameraManager.ResumeCamera();
                }

                // 强制垃圾回收
                GC.Collect();
                GC.WaitForPendingFinalizers();

                Logger.Info("MainWindow", "已退出梯形校正模式");
            }
            catch (Exception ex)
            {
                Logger.Error("MainWindow", $"退出梯形校正模式失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 强制退出校正模式
        /// </summary>
        private void ForceExitCorrectionMode()
        {
            try
            {
                Logger.Warning("MainWindow", "强制退出校正模式");

                // 释放所有鼠标捕获
                ReleaseAllMouseCaptures();

                // 移除校正点事件
                RemoveCorrectionPointsEvents();

                // 重置状态
                _isPerspectiveCorrectionMode = false;
                _isCorrectionModeInitialized = false;
                _draggingPointIndex = -1;

                // 释放背景帧
                if (_originalCorrectionFrame != null)
                {
                    _originalCorrectionFrame.Dispose();
                    _originalCorrectionFrame = null;
                }

                // 恢复UI状态
                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        Ink.IsEnabled = true;
                        PerspectiveCorrectionGrid.Visibility = Visibility.Collapsed;
                        BottomToolbar.Visibility = Visibility.Visible;
                    }
                    catch { }
                });

                Logger.Info("MainWindow", "已强制退出校正模式");
            }
            catch (Exception ex)
            {
                Logger.Error("MainWindow", $"强制退出校正模式失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 释放所有鼠标捕获
        /// </summary>
        private void ReleaseAllMouseCaptures()
        {
            try
            {
                CorrectionCanvas.ReleaseMouseCapture();
                CorrectionPoint0.ReleaseMouseCapture();
                CorrectionPoint1.ReleaseMouseCapture();
                CorrectionPoint2.ReleaseMouseCapture();
                CorrectionPoint3.ReleaseMouseCapture();

                // 释放可能的其他鼠标捕获
                Mouse.OverrideCursor = null;
                Cursor = WinCursors.Arrow;

                Logger.Debug("MainWindow", "所有鼠标捕获已释放");
            }
            catch (Exception ex)
            {
                Logger.Error("MainWindow", $"释放鼠标捕获失败: {ex.Message}", ex);
            }
        }

        // ==================== 校正点管理 ====================

        /// <summary>
        /// 初始化校正点位置
        /// </summary>
        private void InitializeCorrectionPoints()
        {
            try
            {
                // 获取视频区域的尺寸
                double videoWidth = VideoArea.ActualWidth;
                double videoHeight = VideoArea.ActualHeight;

                // 如果视频区域尺寸为0，使用默认值
                if (videoWidth <= 0 || videoHeight <= 0)
                {
                    videoWidth = 800;
                    videoHeight = 600;
                }

                // 设置四个点的初始位置（一个矩形，占视频区域的70%）
                double marginX = videoWidth * 0.15;
                double marginY = videoHeight * 0.15;

                _correctionPoints[0] = new WinPoint(marginX, marginY);
                _correctionPoints[1] = new WinPoint(videoWidth - marginX, marginY);
                _correctionPoints[2] = new WinPoint(videoWidth - marginX, videoHeight - marginY);
                _correctionPoints[3] = new WinPoint(marginX, videoHeight - marginY);

                // 设置校正画布的尺寸
                CorrectionCanvas.Width = videoWidth;
                CorrectionCanvas.Height = videoHeight;

                // 更新UI
                UpdateCorrectionUI();

                Logger.Debug("MainWindow", $"初始化校正点完成: 画布尺寸={videoWidth}x{videoHeight}");
            }
            catch (Exception ex)
            {
                Logger.Error("MainWindow", $"初始化校正点失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 更新校正UI
        /// </summary>
        private void UpdateCorrectionUI()
        {
            try
            {
                // 更新校正点的位置
                Canvas.SetLeft(CorrectionPoint0, _correctionPoints[0].X - 10);
                Canvas.SetTop(CorrectionPoint0, _correctionPoints[0].Y - 10);
                Canvas.SetLeft(CorrectionLabel0, _correctionPoints[0].X + 10);
                Canvas.SetTop(CorrectionLabel0, _correctionPoints[0].Y + 5);

                Canvas.SetLeft(CorrectionPoint1, _correctionPoints[1].X - 10);
                Canvas.SetTop(CorrectionPoint1, _correctionPoints[1].Y - 10);
                Canvas.SetLeft(CorrectionLabel1, _correctionPoints[1].X + 10);
                Canvas.SetTop(CorrectionLabel1, _correctionPoints[1].Y + 5);

                Canvas.SetLeft(CorrectionPoint2, _correctionPoints[2].X - 10);
                Canvas.SetTop(CorrectionPoint2, _correctionPoints[2].Y - 10);
                Canvas.SetLeft(CorrectionLabel2, _correctionPoints[2].X + 10);
                Canvas.SetTop(CorrectionLabel2, _correctionPoints[2].Y + 5);

                Canvas.SetLeft(CorrectionPoint3, _correctionPoints[3].X - 10);
                Canvas.SetTop(CorrectionPoint3, _correctionPoints[3].Y - 10);
                Canvas.SetLeft(CorrectionLabel3, _correctionPoints[3].X + 10);
                Canvas.SetTop(CorrectionLabel3, _correctionPoints[3].Y + 5);

                // 更新多边形
                CorrectionPolygon.Points = new PointCollection(_correctionPoints);
            }
            catch (Exception ex)
            {
                Logger.Error("MainWindow", $"更新校正UI失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 设置校正点事件
        /// </summary>
        private void SetupCorrectionPointsEvents()
        {
            try
            {
                // 为每个校正点添加鼠标事件
                CorrectionPoint0.MouseLeftButtonDown += CorrectionPoint_MouseDown;
                CorrectionPoint1.MouseLeftButtonDown += CorrectionPoint_MouseDown;
                CorrectionPoint2.MouseLeftButtonDown += CorrectionPoint_MouseDown;
                CorrectionPoint3.MouseLeftButtonDown += CorrectionPoint_MouseDown;

                // 为校正点添加拖动事件
                CorrectionCanvas.MouseLeftButtonDown += CorrectionCanvas_MouseDown;
                CorrectionCanvas.MouseMove += CorrectionCanvas_MouseMove;
                CorrectionCanvas.MouseLeftButtonUp += CorrectionCanvas_MouseUp;

                Logger.Debug("MainWindow", "校正点事件已绑定");
            }
            catch (Exception ex)
            {
                Logger.Error("MainWindow", $"设置校正点事件失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 移除校正点事件
        /// </summary>
        private void RemoveCorrectionPointsEvents()
        {
            try
            {
                CorrectionPoint0.MouseLeftButtonDown -= CorrectionPoint_MouseDown;
                CorrectionPoint1.MouseLeftButtonDown -= CorrectionPoint_MouseDown;
                CorrectionPoint2.MouseLeftButtonDown -= CorrectionPoint_MouseDown;
                CorrectionPoint3.MouseLeftButtonDown -= CorrectionPoint_MouseDown;

                CorrectionCanvas.MouseLeftButtonDown -= CorrectionCanvas_MouseDown;
                CorrectionCanvas.MouseMove -= CorrectionCanvas_MouseMove;
                CorrectionCanvas.MouseLeftButtonUp -= CorrectionCanvas_MouseUp;

                Logger.Debug("MainWindow", "校正点事件已移除");
            }
            catch (Exception ex)
            {
                Logger.Error("MainWindow", $"移除校正点事件失败: {ex.Message}", ex);
            }
        }

        // ==================== 校正点拖动事件 ====================

        /// <summary>
        /// 校正点鼠标按下事件（修复版）
        /// </summary>
        private void CorrectionPoint_MouseDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (!_isPerspectiveCorrectionMode || _isClosing) return;

                var point = sender as Ellipse;
                if (point == null) return;

                // 确定是哪个点
                if (point == CorrectionPoint0) _draggingPointIndex = 0;
                else if (point == CorrectionPoint1) _draggingPointIndex = 1;
                else if (point == CorrectionPoint2) _draggingPointIndex = 2;
                else if (point == CorrectionPoint3) _draggingPointIndex = 3;
                else _draggingPointIndex = -1;

                if (_draggingPointIndex >= 0)
                {
                    // 检查是否已有鼠标捕获
                    if (Mouse.Captured != null && Mouse.Captured != point)
                    {
                        Mouse.Captured.ReleaseMouseCapture();
                    }

                    // 捕获鼠标
                    if (point.CaptureMouse())
                    {
                        e.Handled = true;
                    }
                    else
                    {
                        Logger.Warning("MainWindow", "鼠标捕获失败");
                        _draggingPointIndex = -1;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("MainWindow", $"校正点鼠标按下失败: {ex.Message}", ex);
                _draggingPointIndex = -1;
            }
        }

        /// <summary>
        /// 校正画布鼠标按下事件
        /// </summary>
        private void CorrectionCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!_isPerspectiveCorrectionMode) return;

            // 获取点击位置
            var position = e.GetPosition(CorrectionCanvas);

            // 检查是否点击了校正点（10像素范围内的点击都算）
            for (int i = 0; i < 4; i++)
            {
                var point = _correctionPoints[i];
                var distance = Math.Sqrt(Math.Pow(position.X - point.X, 2) + Math.Pow(position.Y - point.Y, 2));

                if (distance <= 10) // 点击半径10像素内的点
                {
                    _draggingPointIndex = i;

                    // 设置鼠标捕获
                    CorrectionCanvas.CaptureMouse();

                    e.Handled = true;
                    return;
                }
            }

            _draggingPointIndex = -1;
        }

        /// <summary>
        /// 校正画布鼠标移动事件（修复版）
        /// </summary>
        private void CorrectionCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            try
            {
                if (!_isPerspectiveCorrectionMode || _draggingPointIndex == -1 || _isClosing) return;

                var position = e.GetPosition(CorrectionCanvas);

                // 限制点在画布范围内（留出10像素边距）
                position.X = Math.Max(10, Math.Min(CorrectionCanvas.ActualWidth - 10, position.X));
                position.Y = Math.Max(10, Math.Min(CorrectionCanvas.ActualHeight - 10, position.Y));

                // 更新校正点位置
                _correctionPoints[_draggingPointIndex] = position;

                // 更新UI显示
                UpdateCorrectionUI();

                e.Handled = true;
            }
            catch (Exception ex)
            {
                Logger.Error("MainWindow", $"校正点鼠标移动失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 校正画布鼠标释放事件（修复版）
        /// </summary>
        private void CorrectionCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (!_isPerspectiveCorrectionMode || _isClosing) return;

                // 释放鼠标捕获
                if (_draggingPointIndex >= 0)
                {
                    // 释放校正点的鼠标捕获
                    if (_draggingPointIndex == 0) CorrectionPoint0.ReleaseMouseCapture();
                    else if (_draggingPointIndex == 1) CorrectionPoint1.ReleaseMouseCapture();
                    else if (_draggingPointIndex == 2) CorrectionPoint2.ReleaseMouseCapture();
                    else if (_draggingPointIndex == 3) CorrectionPoint3.ReleaseMouseCapture();

                    // 释放画布的鼠标捕获
                    CorrectionCanvas.ReleaseMouseCapture();

                    _draggingPointIndex = -1;
                    e.Handled = true;
                }

                // 确保鼠标状态正常
                Mouse.OverrideCursor = null;
                Cursor = WinCursors.Arrow;
            }
            catch (Exception ex)
            {
                Logger.Error("MainWindow", $"校正点鼠标释放失败: {ex.Message}", ex);
            }
            finally
            {
                _draggingPointIndex = -1;
            }
        }

        // ==================== 校正按钮事件 ====================

        /// <summary>
        /// 应用校正按钮点击事件
        /// </summary>
        private void ApplyCorrectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isPerspectiveCorrectionMode || _originalCorrectionFrame == null) return;

            try
            {
                Logger.Info("MainWindow", "开始应用梯形校正");

                // 1. 获取原始图像尺寸
                double imageWidth = _originalCorrectionFrame.Width;
                double imageHeight = _originalCorrectionFrame.Height;

                if (imageWidth <= 0 || imageHeight <= 0)
                {
                    MessageBox.Show("图像尺寸无效，无法应用校正。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 2. 获取图像显示区域
                Rect imageRect = GetImageRectInVideoArea();

                // 3. 将UI坐标转换为图像坐标
                List<AForge.IntPoint> points = new List<AForge.IntPoint>();
                for (int i = 0; i < 4; i++)
                {
                    double x = (_correctionPoints[i].X - imageRect.X) * imageWidth / imageRect.Width;
                    double y = (_correctionPoints[i].Y - imageRect.Y) * imageHeight / imageRect.Height;

                    // 限制在图像范围内
                    x = Math.Max(0, Math.Min(imageWidth - 1, x));
                    y = Math.Max(0, Math.Min(imageHeight - 1, y));

                    points.Add(new AForge.IntPoint((int)x, (int)y));
                }

                // 4. 创建透视校正过滤器
                var filter = new QuadrilateralTransformation(points, (int)imageWidth, (int)imageHeight);

                // 5. 应用到摄像头管理器
                _cameraManager.SetPerspectiveCorrectionFilter(filter);

                // 6. 保存校正配置
                SaveCorrectionConfig(points, (int)imageWidth, (int)imageHeight);

                // 7. 退出校正模式
                ExitPerspectiveCorrectionMode(true);

                MessageBox.Show("梯形校正已成功应用！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);

                Logger.Info("MainWindow", "梯形校正应用完成");
            }
            catch (Exception ex)
            {
                Logger.Error("MainWindow", $"应用梯形校正失败: {ex.Message}", ex);
                MessageBox.Show($"应用校正失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 重置校正按钮点击事件
        /// </summary>
        private void ResetCorrectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isPerspectiveCorrectionMode) return;

            Logger.Info("MainWindow", "重置梯形校正点");
            InitializeCorrectionPoints();
        }

        /// <summary>
        /// 取消校正按钮点击事件
        /// </summary>
        private void CancelCorrectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isPerspectiveCorrectionMode) return;

            Logger.Info("MainWindow", "取消梯形校正");
            ExitPerspectiveCorrectionMode(false);
        }

        /// <summary>
        /// 打开梯形校正菜单项点击事件
        /// </summary>
        private void OpenPerspectiveCorrection_Click(object sender, RoutedEventArgs e)
        {
            if (_isClosing || _isEnteringCorrectionMode) return;

            // 隐藏更多菜单
            MoreMenuPopup.IsOpen = false;

            // 使用安全方法进入梯形校正模式
            SafeEnterPerspectiveCorrectionMode();
        }

        // ==================== 辅助方法 ====================

        /// <summary>
        /// 获取图像在VideoArea中的显示区域
        /// </summary>
        private Rect GetImageRectInVideoArea()
        {
            if (VideoImage.Source == null || VideoArea == null)
            {
                // 如果没有图像源，返回校正画布的尺寸
                return new Rect(0, 0,
                    CorrectionCanvas?.ActualWidth ?? 800,
                    CorrectionCanvas?.ActualHeight ?? 600);
            }

            try
            {
                double imageWidth = VideoImage.Source.Width;
                double imageHeight = VideoImage.Source.Height;

                if (imageWidth <= 0 || imageHeight <= 0)
                {
                    return new Rect(0, 0,
                        CorrectionCanvas?.ActualWidth ?? 800,
                        CorrectionCanvas?.ActualHeight ?? 600);
                }

                double aspectRatio = imageWidth / imageHeight;
                double areaWidth = VideoArea.ActualWidth;
                double areaHeight = VideoArea.ActualHeight;
                double areaAspectRatio = areaWidth / areaHeight;

                double width, height;
                if (aspectRatio > areaAspectRatio)
                {
                    // 宽度受限
                    width = areaWidth;
                    height = areaWidth / aspectRatio;
                }
                else
                {
                    // 高度受限
                    height = areaHeight;
                    width = areaHeight * aspectRatio;
                }

                // 计算居中位置
                double x = (areaWidth - width) / 2;
                double y = (areaHeight - height) / 2;

                return new Rect(x, y, width, height);
            }
            catch (Exception ex)
            {
                Logger.Error("MainWindow", $"获取图像显示区域失败: {ex.Message}", ex);
                return new Rect(0, 0,
                    CorrectionCanvas?.ActualWidth ?? 800,
                    CorrectionCanvas?.ActualHeight ?? 600);
            }
        }

        /// <summary>
        /// 保存校正配置
        /// </summary>
        private void SaveCorrectionConfig(List<AForge.IntPoint> points, int sourceWidth, int sourceHeight)
        {
            try
            {
                var cameraIndex = _cameraManager.CurrentCameraIndex;
                var cameraName = _cameraManager.GetCurrentCameraName();

                // 创建或获取相机配置
                if (!config.CameraConfigs.ContainsKey(cameraIndex))
                {
                    config.CameraConfigs[cameraIndex] = new CameraConfig
                    {
                        CameraIndex = cameraIndex,
                        CameraName = cameraName
                    };
                }

                // 更新校正配置
                config.CameraConfigs[cameraIndex].SetCorrectionPoints(points);
                config.CameraConfigs[cameraIndex].SourceWidth = sourceWidth;
                config.CameraConfigs[cameraIndex].SourceHeight = sourceHeight;
                config.CameraConfigs[cameraIndex].OriginalCameraWidth = _originalCorrectionFrame?.Width ?? 0;
                config.CameraConfigs[cameraIndex].OriginalCameraHeight = _originalCorrectionFrame?.Height ?? 0;
                config.CameraConfigs[cameraIndex].HasCorrection = true;

                // 保存配置
                SaveConfig();

                Logger.Info("MainWindow", $"摄像头 {cameraIndex} ({cameraName}) 的校正配置已保存");
            }
            catch (Exception ex)
            {
                Logger.Error("MainWindow", $"保存校正配置失败: {ex.Message}", ex);
            }
        }

        #endregion

        #region 核心功能方法

        /// <summary>
        /// 拍照功能
        /// </summary>
        private void Capture_Click(object sender, RoutedEventArgs e)
        {
            if (_isClosing) return;

            if (!_cameraManager.IsCameraAvailable)
            {
                MessageBox.Show("没有可用的摄像头，无法拍照。", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Logger.Info("MainWindow", "开始拍照");

            var frame = _cameraManager.GetCurrentFrame();
            if (frame != null)
            {
                try
                {
                    var bitmapImage = _frameProcessor.ProcessFrameToBitmapImage(frame);
                    if (bitmapImage != null)
                    {
                        // 使用 PhotoPopupManager 添加照片
                        var strokes = new StrokeCollection(_drawingManager.GetStrokes());
                        _photoPopupManager.AddPhoto(bitmapImage, strokes);

                        ShowPhotoTip();
                        _memoryManager.TriggerMemoryCleanup();

                        Logger.Info("MainWindow", "拍照成功");
                    }
                }
                finally
                {
                    frame.Dispose();
                }
            }
            else
            {
                Logger.Warning("MainWindow", "无法获取摄像头画面进行拍照");
                MessageBox.Show("无法获取摄像头画面。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 扫码功能
        /// </summary>
        private void ScanQRCode_Click(object sender, RoutedEventArgs e)
        {
            if (_isClosing) return;

            if (!_cameraManager.IsCameraAvailable)
            {
                MessageBox.Show("没有可用的摄像头，无法使用扫码功能。", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Logger.Info("MainWindow", "开始扫码");

            var frame = _cameraManager.GetCurrentFrame();
            if (frame == null) return;

            try
            {
                var result = _frameProcessor.DecodeBarcodeFromBitmap(frame);
                if (result != null)
                {
                    System.Windows.Clipboard.SetText(result.Text ?? string.Empty);
                    Logger.Info("MainWindow", $"扫码成功: {result.BarcodeFormat} - {result.Text}");
                    MessageBox.Show($"识别到：{result.BarcodeFormat}\n{result.Text}\n(已复制到剪贴板)", "扫一扫");
                }
                else
                {
                    Logger.Info("MainWindow", "未检测到二维码/条码");
                    MessageBox.Show("未检测到二维码/条码。", "扫一扫");
                }
            }
            finally
            {
                frame.Dispose();
            }
        }

        #endregion

        #region 颜色选择器相关方法

        /// <summary>
        /// 选择指定的颜色按钮
        /// </summary>
        private void SelectColorButton(string colorName)
        {
            try
            {
                // 隐藏所有对钩
                HideAllCheckIcons();

                // 根据颜色名称找到对应的按钮并显示对钩
                switch (colorName)
                {
                    case "Black":
                        if (CheckIcon_Black != null) CheckIcon_Black.Visibility = Visibility.Visible;
                        _currentSelectedColorButton = GetColorButtonByTag("Black");
                        break;
                    case "Red":
                        if (CheckIcon_Red != null) CheckIcon_Red.Visibility = Visibility.Visible;
                        _currentSelectedColorButton = GetColorButtonByTag("Red");
                        break;
                    case "Green":
                        if (CheckIcon_Green != null) CheckIcon_Green.Visibility = Visibility.Visible;
                        _currentSelectedColorButton = GetColorButtonByTag("Green");
                        break;
                    case "Blue":
                        if (CheckIcon_Blue != null) CheckIcon_Blue.Visibility = Visibility.Visible;
                        _currentSelectedColorButton = GetColorButtonByTag("Blue");
                        break;
                    case "Yellow":
                        if (CheckIcon_Yellow != null) CheckIcon_Yellow.Visibility = Visibility.Visible;
                        _currentSelectedColorButton = GetColorButtonByTag("Yellow");
                        break;
                    case "White":
                        if (CheckIcon_White != null) CheckIcon_White.Visibility = Visibility.Visible;
                        _currentSelectedColorButton = GetColorButtonByTag("White");
                        break;
                    case "Orange":
                        if (CheckIcon_Orange != null) CheckIcon_Orange.Visibility = Visibility.Visible;
                        _currentSelectedColorButton = GetColorButtonByTag("Orange");
                        break;
                    case "Purple":
                        if (CheckIcon_Purple != null) CheckIcon_Purple.Visibility = Visibility.Visible;
                        _currentSelectedColorButton = GetColorButtonByTag("Purple");
                        break;
                    case "Cyan":
                        if (CheckIcon_Cyan != null) CheckIcon_Cyan.Visibility = Visibility.Visible;
                        _currentSelectedColorButton = GetColorButtonByTag("Cyan");
                        break;
                    case "Magenta":
                        if (CheckIcon_Magenta != null) CheckIcon_Magenta.Visibility = Visibility.Visible;
                        _currentSelectedColorButton = GetColorButtonByTag("Magenta");
                        break;
                    case "Brown":
                        if (CheckIcon_Brown != null) CheckIcon_Brown.Visibility = Visibility.Visible;
                        _currentSelectedColorButton = GetColorButtonByTag("Brown");
                        break;
                    case "Pink":
                        if (CheckIcon_Pink != null) CheckIcon_Pink.Visibility = Visibility.Visible;
                        _currentSelectedColorButton = GetColorButtonByTag("Pink");
                        break;
                    case "Gray":
                        if (CheckIcon_Gray != null) CheckIcon_Gray.Visibility = Visibility.Visible;
                        _currentSelectedColorButton = GetColorButtonByTag("Gray");
                        break;
                    case "DarkRed":
                        if (CheckIcon_DarkRed != null) CheckIcon_DarkRed.Visibility = Visibility.Visible;
                        _currentSelectedColorButton = GetColorButtonByTag("DarkRed");
                        break;
                    case "DarkGreen":
                        if (CheckIcon_DarkGreen != null) CheckIcon_DarkGreen.Visibility = Visibility.Visible;
                        _currentSelectedColorButton = GetColorButtonByTag("DarkGreen");
                        break;
                    case "DarkBlue":
                        if (CheckIcon_DarkBlue != null) CheckIcon_DarkBlue.Visibility = Visibility.Visible;
                        _currentSelectedColorButton = GetColorButtonByTag("DarkBlue");
                        break;
                    case "Gold":
                        if (CheckIcon_Gold != null) CheckIcon_Gold.Visibility = Visibility.Visible;
                        _currentSelectedColorButton = GetColorButtonByTag("Gold");
                        break;
                    case "Silver":
                        if (CheckIcon_Silver != null) CheckIcon_Silver.Visibility = Visibility.Visible;
                        _currentSelectedColorButton = GetColorButtonByTag("Silver");
                        break;
                    case "Lime":
                        if (CheckIcon_Lime != null) CheckIcon_Lime.Visibility = Visibility.Visible;
                        _currentSelectedColorButton = GetColorButtonByTag("Lime");
                        break;
                    case "Teal":
                        if (CheckIcon_Teal != null) CheckIcon_Teal.Visibility = Visibility.Visible;
                        _currentSelectedColorButton = GetColorButtonByTag("Teal");
                        break;
                    default:
                        if (CheckIcon_Black != null) CheckIcon_Black.Visibility = Visibility.Visible;
                        _currentSelectedColorButton = GetColorButtonByTag("Black");
                        break;
                }

                // 更新当前画笔颜色
                _currentPenColor = colorName;
            }
            catch (Exception ex)
            {
                Logger.Error("MainWindow", $"选择颜色按钮失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 隐藏所有对钩图标
        /// </summary>
        private void HideAllCheckIcons()
        {
            if (CheckIcon_Black != null) CheckIcon_Black.Visibility = Visibility.Collapsed;
            if (CheckIcon_Red != null) CheckIcon_Red.Visibility = Visibility.Collapsed;
            if (CheckIcon_Green != null) CheckIcon_Green.Visibility = Visibility.Collapsed;
            if (CheckIcon_Blue != null) CheckIcon_Blue.Visibility = Visibility.Collapsed;
            if (CheckIcon_Yellow != null) CheckIcon_Yellow.Visibility = Visibility.Collapsed;
            if (CheckIcon_White != null) CheckIcon_White.Visibility = Visibility.Collapsed;
            if (CheckIcon_Orange != null) CheckIcon_Orange.Visibility = Visibility.Collapsed;
            if (CheckIcon_Purple != null) CheckIcon_Purple.Visibility = Visibility.Collapsed;
            if (CheckIcon_Cyan != null) CheckIcon_Cyan.Visibility = Visibility.Collapsed;
            if (CheckIcon_Magenta != null) CheckIcon_Magenta.Visibility = Visibility.Collapsed;
            if (CheckIcon_Brown != null) CheckIcon_Brown.Visibility = Visibility.Collapsed;
            if (CheckIcon_Pink != null) CheckIcon_Pink.Visibility = Visibility.Collapsed;
            if (CheckIcon_Gray != null) CheckIcon_Gray.Visibility = Visibility.Collapsed;
            if (CheckIcon_DarkRed != null) CheckIcon_DarkRed.Visibility = Visibility.Collapsed;
            if (CheckIcon_DarkGreen != null) CheckIcon_DarkGreen.Visibility = Visibility.Collapsed;
            if (CheckIcon_DarkBlue != null) CheckIcon_DarkBlue.Visibility = Visibility.Collapsed;
            if (CheckIcon_Gold != null) CheckIcon_Gold.Visibility = Visibility.Collapsed;
            if (CheckIcon_Silver != null) CheckIcon_Silver.Visibility = Visibility.Collapsed;
            if (CheckIcon_Lime != null) CheckIcon_Lime.Visibility = Visibility.Collapsed;
            if (CheckIcon_Teal != null) CheckIcon_Teal.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// 根据Tag获取颜色按钮
        /// </summary>
        private Button GetColorButtonByTag(string tag)
        {
            if (PenSettingsPopup == null || PenSettingsPopup.Child == null) return null;

            var border = PenSettingsPopup.Child as Border;
            if (border == null) return null;

            var stackPanel = border.Child as StackPanel;
            if (stackPanel == null) return null;

            var grid = stackPanel.Children.OfType<Grid>().FirstOrDefault();
            if (grid == null) return null;

            // 在Grid中查找具有指定Tag的按钮
            foreach (UIElement child in grid.Children)
            {
                if (child is Button button && button.Tag?.ToString() == tag)
                {
                    return button;
                }
            }

            return null;
        }

        /// <summary>
        /// 根据颜色名称获取颜色
        /// </summary>
        private System.Windows.Media.Color GetColorFromName(string colorName)
        {
            switch (colorName)
            {
                case "Black": return Colors.Black;
                case "Red": return Colors.Red;
                case "Green": return Colors.Green;
                case "Blue": return Colors.Blue;
                case "Yellow": return Colors.Yellow;
                case "White": return Colors.White;
                case "Orange": return Colors.Orange;
                case "Purple": return Colors.Purple;
                case "Cyan": return Colors.Cyan;
                case "Magenta": return Colors.Magenta;
                case "Brown": return Colors.Brown;
                case "Pink": return Colors.Pink;
                case "Gray": return Colors.Gray;
                case "DarkRed": return Colors.DarkRed;
                case "DarkGreen": return Colors.DarkGreen;
                case "DarkBlue": return Colors.DarkBlue;
                case "Gold": return Colors.Gold;
                case "Silver": return Colors.Silver;
                case "Lime": return Colors.Lime;
                case "Teal": return Colors.Teal;
                default: return Colors.Black;
            }
        }

        /// <summary>
        /// 初始化画笔颜色选择器
        /// </summary>
        private void InitializePenColorSelector()
        {
            try
            {
                // 默认选中黑色
                SelectColorButton("Black");
                _currentPenColor = "Black";

                // 设置默认画笔颜色
                _drawingManager.SetPenColor(GetColorFromName("Black"));

                Logger.Debug("MainWindow", "画笔颜色选择器初始化完成");
            }
            catch (Exception ex)
            {
                Logger.Error("MainWindow", $"初始化画笔颜色选择器失败: {ex.Message}", ex);
            }
        }

        #endregion

        #region 悬浮窗事件处理

        /// <summary>
        /// 画笔设置悬浮窗打开事件
        /// </summary>
        private void PenSettingsPopup_Opened(object sender, EventArgs e)
        {
            try
            {
                // 更新颜色选择器的选中状态
                if (!string.IsNullOrEmpty(_currentPenColor))
                {
                    SelectColorButton(_currentPenColor);
                }

                Logger.Debug("MainWindow", "画笔设置悬浮窗已打开");
            }
            catch (Exception ex)
            {
                Logger.Error("MainWindow", $"画笔设置悬浮窗打开事件失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 更多菜单弹出窗口关闭事件
        /// </summary>
        private void MoreMenuPopup_Closed(object sender, EventArgs e)
        {
            Logger.Debug("MainWindow", "更多菜单弹出窗口已关闭");
        }

        #endregion

        #region 其他事件处理方法

        /// <summary>
        /// 文档扫描功能
        /// </summary>
        private void ScanDocument_Click(object sender, RoutedEventArgs e)
        {
            if (_isClosing) return;

            // 检查摄像头可用性
            if (!_cameraManager.IsCameraAvailable)
            {
                MessageBox.Show("没有可用的摄像头，无法使用文档扫描功能。", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Logger.Info("MainWindow", "开始文档扫描");

            var frame = _cameraManager.GetCurrentFrame();
            if (frame == null) return;

            System.Drawing.Bitmap processed = null;
            try
            {
                // 使用帧处理器进行文档扫描处理
                processed = _frameProcessor.ProcessDocumentScan(frame);
                if (processed != null)
                {
                    var bitmapImage = _memoryManager.BitmapToBitmapImage(processed);

                    // 使用 PhotoPopupManager 添加文档扫描结果
                    var strokes = new StrokeCollection(_drawingManager.GetStrokes());
                    _photoPopupManager.AddPhoto(bitmapImage, strokes);

                    ShowPhotoTip();
                    // 触发内存清理
                    _memoryManager.TriggerMemoryCleanup();

                    Logger.Info("MainWindow", "文档扫描完成");
                }
            }
            finally
            {
                frame.Dispose();
                processed?.Dispose();
            }
        }

        /// <summary>
        /// 保存图片功能
        /// </summary>
        private void SaveImage_Click(object sender, RoutedEventArgs e)
        {
            if (_isClosing) return;

            var currentPhoto = _photoPopupManager.CurrentPhoto;
            if (currentPhoto == null)
            {
                MessageBox.Show("请先拍照或选择一张图片。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Logger.Info("MainWindow", "开始保存图片");

            // 使用WPF的SaveFileDialog
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "PNG 图片|*.png|JPEG 图片|*.jpg",
                FileName = $"Capture_{DateTime.Now:yyyyMMdd_HHmmss}.png"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    // 保存包含批注的图片
                    SaveImageWithInk(currentPhoto.Image, currentPhoto.Strokes, dlg.FileName);
                    Logger.Info("MainWindow", $"图片保存成功: {dlg.FileName}");
                    MessageBox.Show("保存成功！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    Logger.Error("MainWindow", $"保存图片失败: {ex.Message}", ex);
                    MessageBox.Show($"保存失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// 保存图片时包含批注
        /// </summary>
        private void SaveImageWithInk(BitmapSource originalImage, StrokeCollection strokes, string filePath)
        {
            if (strokes == null || strokes.Count == 0)
            {
                // 如果没有批注，直接保存原图
                _frameProcessor.SaveBitmapSourceToFile(originalImage, filePath);
                return;
            }

            // 创建包含批注的视觉对象
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                // 绘制原始图片
                context.DrawImage(originalImage, new Rect(0, 0, originalImage.PixelWidth, originalImage.PixelHeight));
                // 绘制批注
                foreach (var stroke in strokes)
                {
                    var geometry = stroke.GetGeometry(stroke.DrawingAttributes);
                    var brush = new SolidColorBrush(stroke.DrawingAttributes.Color);
                    context.DrawGeometry(brush, null, geometry);
                }
            }

            // 渲染为位图
            var renderBitmap = new RenderTargetBitmap(
                originalImage.PixelWidth,
                originalImage.PixelHeight,
                originalImage.DpiX,
                originalImage.DpiY,
                PixelFormats.Pbgra32);
            renderBitmap.Render(visual);

            // 保存到文件
            _frameProcessor.SaveBitmapSourceToFile(renderBitmap, filePath);
        }

        /// <summary>
        /// 关闭触控信息悬浮窗
        /// </summary>
        private void CloseTouchInfo_Click(object sender, RoutedEventArgs e)
        {
            TouchInfoPopup.IsOpen = false;
            Logger.Debug("MainWindow", "关闭触控信息悬浮窗");
        }

        /// <summary>
        /// 切换摄像头
        /// </summary>
        private void SwitchCamera_Click(object sender, RoutedEventArgs e)
        {
            if (_isClosing) return;

            Logger.Info("MainWindow", "开始切换摄像头");

            var cameras = _cameraManager.GetAvailableCameras();
            if (cameras.Count == 0)
            {
                Logger.Warning("MainWindow", "未找到可用摄像头");
                MessageBox.Show("未找到可用摄像头。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                _cameraManager.CheckCameraAvailability();
                ShowNoCameraBackground();
                return;
            }

            // 使用WPF窗口替代WinForms
            var dialog = new Window
            {
                Title = "选择摄像头",
                Width = 400,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow
            };

            var stackPanel = new StackPanel { Margin = new Thickness(10) };

            var comboBox = new WinComboBox
            {
                Margin = new Thickness(0, 0, 0, 10),
                ItemsSource = cameras,
                SelectedIndex = _cameraManager.CurrentCameraIndex
            };

            var buttonPanel = new StackPanel
            {
                Orientation = WinOrientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right
            };

            var okButton = new WinButton
            {
                Content = "确定",
                Width = 80,
                Margin = new Thickness(5, 0, 0, 0),
                IsDefault = true
            };

            var cancelButton = new WinButton
            {
                Content = "取消",
                Width = 80,
                Margin = new Thickness(5, 0, 0, 0),
                IsCancel = true
            };

            buttonPanel.Children.Add(cancelButton);
            buttonPanel.Children.Add(okButton);

            stackPanel.Children.Add(comboBox);
            stackPanel.Children.Add(buttonPanel);
            dialog.Content = stackPanel;

            bool? result = null;

            okButton.Click += (s, args) =>
            {
                result = true;
                dialog.Close();
            };

            cancelButton.Click += (s, args) =>
            {
                result = false;
                dialog.Close();
            };

            dialog.ShowDialog();

            if (result == true && comboBox.SelectedIndex >= 0)
            {
                int newCameraIndex = comboBox.SelectedIndex;

                // 重置视频帧记录状态
                _isFirstFrameProcessed = false;
                Logger.ResetVideoFrameLogging();

                // 切换摄像头
                if (_cameraManager.SwitchCamera(newCameraIndex))
                {
                    Logger.Info("MainWindow", $"已切换到摄像头: {cameras[newCameraIndex]}");
                    MessageBox.Show($"已切换到摄像头: {cameras[newCameraIndex]}", "提示", MessageBoxButton.OK, MessageBoxImage.Information);

                    // 切换摄像头后应用该摄像头的配置
                    ApplyCameraConfigOnStartup();
                }
                else
                {
                    Logger.Error("MainWindow", "切换摄像头失败");
                    MessageBox.Show("切换摄像头失败。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    ShowNoCameraBackground();
                }
            }
        }

        /// <summary>
        /// 打开画面调节窗口
        /// </summary>
        private void OpenAdjustVideo_Click(object sender, RoutedEventArgs e)
        {
            if (_isClosing) return;

            // 检查摄像头可用性
            if (!_cameraManager.IsCameraAvailable)
            {
                MessageBox.Show("没有可用的摄像头，无法使用画面调节功能。", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Logger.Info("MainWindow", "打开画面调节窗口");

            // 创建画面调节窗口
            var wnd = new AdjustVideoWindow(
                _brightness,
                _contrast,
                _rotation,
                _mirrorHorizontal,
                _mirrorVertical
            );
            wnd.Owner = this;
            if (wnd.ShowDialog() == true)
            {
                // 更新画面调节参数
                _brightness = wnd.Brightness;
                _contrast = wnd.Contrast;
                _rotation = wnd.Rotation;
                _mirrorHorizontal = wnd.MirrorH;
                _mirrorVertical = wnd.MirrorV;

                // 应用设置到摄像头管理器
                _cameraManager.SetVideoAdjustments(_brightness, _contrast, _rotation, _mirrorHorizontal, _mirrorVertical);

                // 更新当前摄像头的配置
                UpdateCurrentCameraAdjustments();

                Logger.Info("MainWindow", "画面调节参数已更新");
            }
        }

        /// <summary>
        /// 清除透视校正
        /// </summary>
        private void ClearCorrection_Click(object sender, RoutedEventArgs e)
        {
            if (_isClosing) return;

            Logger.Info("MainWindow", "清除透视校正");

            _cameraManager.ClearPerspectiveCorrection();

            // 清除当前摄像头的校正配置
            int cameraIndex = _cameraManager.CurrentCameraIndex;
            if (config.CameraConfigs.ContainsKey(cameraIndex))
            {
                config.CameraConfigs[cameraIndex].ClearCorrection();
            }

            SaveConfig();

            // 刷新当前画面
            if (_cameraManager.IsCameraAvailable)
            {
                var frame = _cameraManager.GetCurrentFrame();
                if (frame != null)
                {
                    using var processed = _cameraManager.ProcessFrame(frame, applyAdjustments: true);
                    VideoImage.Source = _memoryManager.BitmapToBitmapImage(processed);
                    frame.Dispose();
                }
            }

            Logger.Info("MainWindow", "透视校正已清除");
            MessageBox.Show("透视校正已清除。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// 打开设置窗口
        /// </summary>
        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            if (_isClosing) return;

            Logger.Info("MainWindow", "打开设置窗口");

            var cameras = _cameraManager.GetAvailableCameras();
            var settingsWindow = new SettingsWindow(config, cameras)
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            if (settingsWindow.ShowDialog() == true)
            {
                // 应用窗口设置
                WindowState = config.StartMaximized ? WindowState.Maximized : WindowState.Normal;

                // 应用绘制管理器配置
                _drawingManager.ApplyConfig(config);

                // 保存配置
                SaveConfig();

                // 重新应用主题
                ApplyTheme();

                // 检查是否需要切换摄像头
                if (_cameraManager.CurrentCameraIndex != config.CameraIndex)
                {
                    if (_cameraManager.SwitchCamera(config.CameraIndex))
                    {
                        Logger.Info("MainWindow", $"已切换到摄像头: {cameras[config.CameraIndex]}");
                        MessageBox.Show($"已切换到摄像头: {cameras[config.CameraIndex]}", "提示", MessageBoxButton.OK, MessageBoxImage.Information);

                        // 切换摄像头后应用该摄像头的配置
                        ApplyCameraConfigOnStartup();
                    }
                    else
                    {
                        Logger.Error("MainWindow", "切换摄像头失败");
                        MessageBox.Show("切换摄像头失败。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        ShowNoCameraBackground();
                    }
                }

                Logger.Info("MainWindow", "设置已应用");
            }
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 启动摄像头（带降级处理）
        /// </summary>
        private void StartCameraWithFallback()
        {
            // 重置视频帧记录状态
            _isFirstFrameProcessed = false;
            Logger.ResetVideoFrameLogging();

            if (!_cameraManager.StartCamera())
            {
                Logger.Warning("MainWindow", "未找到可用摄像头");
                MessageBox.Show("未找到可用摄像头。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                ShowNoCameraBackground();
            }
            else
            {
                Logger.Info("MainWindow", "摄像头启动成功");

                // 摄像头启动成功后应用配置
                ApplyCameraConfigOnStartup();
            }
        }

        /// <summary>
        /// 显示无摄像头背景
        /// </summary>
        private void ShowNoCameraBackground()
        {
            Dispatcher.Invoke(() =>
            {
                VideoImage.Source = null;
                VideoArea.Background = _noCameraBackground;

                var textBlock = new TextBlock
                {
                    Text = "未检测到摄像头\n批注功能仍可使用",
                    Foreground = WinBrushes.White,
                    FontSize = 16,
                    FontWeight = FontWeights.Bold,
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };

                VideoArea.IsHitTestVisible = true;
                VideoArea.Focusable = true;
                Logger.Info("MainWindow", "已切换到无摄像头模式，批注功能可用");
            });
        }

        /// <summary>
        /// 显示拍照提示
        /// </summary>
        private async void ShowPhotoTip()
        {
            if (_isClosing) return;

            Logger.Debug("MainWindow", "显示拍照提示");

            PhotoTipPopup.IsOpen = true;
            await Task.Delay(3000);
            PhotoTipPopup.IsOpen = false;
        }

        /// <summary>
        /// 更新 TouchSDK 状态显示
        /// </summary>
        private void UpdateTouchSDKStatus()
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    if (TouchCountText != null)
                    {
                        TouchCountText.Text = _touchManager.GetTouchSDKStatusText();
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("MainWindow", $"更新TouchSDK状态显示失败: {ex.Message}", ex);
                }
            });
        }

        /// <summary>
        /// 更新 SDK 面积显示
        /// </summary>
        private void UpdateSDKTouchAreaDisplay()
        {
            try
            {
                if (SDKTouchAreaText != null)
                {
                    SDKTouchAreaText.Text = $"SDK面积: {_touchManager.SDKTouchArea:F0} 像素²";
                }
            }
            catch (Exception ex)
            {
                Logger.Error("MainWindow", $"更新SDK面积显示失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 加载配置
        /// </summary>
        private void LoadConfig()
        {
            try
            {
                Logger.Debug("MainWindow", "开始加载配置");

                if (!File.Exists(configPath))
                {
                    config = new AppConfig();
                    Logger.Info("MainWindow", "配置文件不存在，使用默认配置");
                    return;
                }

                var json = File.ReadAllText(configPath, Encoding.UTF8);
                var cfg = JsonConvert.DeserializeObject<AppConfig>(json);
                if (cfg == null)
                {
                    config = new AppConfig();
                    Logger.Warning("MainWindow", "配置文件解析失败，使用默认配置");
                    return;
                }

                config = cfg;

                // 确保 CameraConfigs 字典被初始化
                if (config.CameraConfigs == null)
                {
                    config.CameraConfigs = new Dictionary<int, CameraConfig>();
                }

                Logger.Info("MainWindow", $"配置加载成功，包含 {config.CameraConfigs.Count} 个摄像头的配置");

                // 向后兼容：如果有旧的 CameraCorrections，迁移到新的 CameraConfigs
                if (typeof(AppConfig).GetProperty("CameraCorrections") != null)
                {
                    MigrateOldCorrectionConfig(cfg);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("MainWindow", $"加载配置失败: {ex.Message}", ex);
                config = new AppConfig();
            }
        }

        /// <summary>
        /// 迁移旧的校正配置到新的格式
        /// </summary>
        private void MigrateOldCorrectionConfig(AppConfig cfg)
        {
            try
            {
                // 使用反射检查是否有旧属性（旧格式使用 CameraCorrections）
                var oldProperty = cfg.GetType().GetProperty("CameraCorrections");
                if (oldProperty != null)
                {
                    var oldValue = oldProperty.GetValue(cfg);
                    if (oldValue is Dictionary<int, object> oldDict && oldDict.Count > 0)
                    {
                        Logger.Info("MainWindow", "检测到旧的校正配置，正在迁移...");

                        foreach (var kvp in oldDict)
                        {
                            try
                            {
                                // 动态解析旧的配置格式
                                var json = JsonConvert.SerializeObject(kvp.Value);
                                var dynamicObj = JsonConvert.DeserializeObject<dynamic>(json);

                                if (dynamicObj != null)
                                {
                                    if (!config.CameraConfigs.ContainsKey(kvp.Key))
                                    {
                                        config.CameraConfigs[kvp.Key] = new CameraConfig
                                        {
                                            CameraIndex = kvp.Key,
                                            CameraName = $"摄像头 {kvp.Key}",
                                            HasCorrection = true
                                        };

                                        // 尝试解析源尺寸
                                        if (dynamicObj.SourceWidth != null)
                                        {
                                            config.CameraConfigs[kvp.Key].SourceWidth = (int)dynamicObj.SourceWidth;
                                            config.CameraConfigs[kvp.Key].SourceHeight = (int)dynamicObj.SourceHeight;
                                        }

                                        if (dynamicObj.OriginalCameraWidth != null)
                                        {
                                            config.CameraConfigs[kvp.Key].OriginalCameraWidth = (int)dynamicObj.OriginalCameraWidth;
                                            config.CameraConfigs[kvp.Key].OriginalCameraHeight = (int)dynamicObj.OriginalCameraHeight;
                                        }

                                        // 尝试解析校正点
                                        if (dynamicObj.CorrectionPoints != null)
                                        {
                                            var pointsList = new List<AForge.IntPoint>();

                                            foreach (var point in dynamicObj.CorrectionPoints)
                                            {
                                                if (point.X != null && point.Y != null)
                                                {
                                                    pointsList.Add(new AForge.IntPoint((int)point.X, (int)point.Y));
                                                }
                                            }

                                            if (pointsList.Count == 4)
                                            {
                                                config.CameraConfigs[kvp.Key].SetCorrectionPoints(pointsList);
                                            }
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Logger.Error("MainWindow", $"迁移摄像头 {kvp.Key} 配置失败: {ex.Message}", ex);
                            }
                        }

                        Logger.Info("MainWindow", $"已尝试迁移 {oldDict.Count} 个摄像头的校正配置");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("MainWindow", $"迁移旧配置失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 启动时应用摄像头配置（包括校正和调整）
        /// </summary>
        private void ApplyCameraConfigOnStartup()
        {
            try
            {
                if (!_cameraManager.IsCameraAvailable || config == null)
                {
                    Logger.Debug("MainWindow", "摄像头不可用或配置为空，跳过配置应用");
                    return;
                }

                int cameraIndex = _cameraManager.CurrentCameraIndex;

                Logger.Debug("MainWindow", $"尝试为摄像头 {cameraIndex} 应用配置");

                // 检查是否有该摄像头的配置
                if (config.CameraConfigs != null &&
                    config.CameraConfigs.ContainsKey(cameraIndex))
                {
                    var cameraConfig = config.CameraConfigs[cameraIndex];

                    if (cameraConfig != null)
                    {
                        Logger.Info("MainWindow", $"找到摄像头 {cameraIndex} 的配置，正在应用...");

                        // 1. 应用画面调整参数
                        ApplyImageAdjustments(cameraConfig.Adjustments);

                        // 2. 应用透视校正
                        if (cameraConfig.HasCorrection &&
                            cameraConfig.PerspectivePoints != null &&
                            cameraConfig.PerspectivePoints.Count == 4)
                        {
                            ApplyPerspectiveCorrection(cameraConfig);
                        }

                        Logger.Info("MainWindow", $"摄像头 {cameraIndex} 的配置已成功应用");
                    }
                }
                else
                {
                    Logger.Debug("MainWindow", $"摄像头 {cameraIndex} 没有找到配置，使用默认设置");
                }
            }
            catch (Exception ex)
            {
                Logger.Error("MainWindow", $"应用摄像头配置失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 应用画面调整参数
        /// </summary>
        private void ApplyImageAdjustments(ImageAdjustments adjustments)
        {
            try
            {
                if (adjustments == null) return;

                // 更新全局调整参数
                _brightness = (adjustments.Brightness - 100) / 100.0 * 50; // 转换为-50到50的范围
                _contrast = (adjustments.Contrast - 100) / 100.0 * 50; // 转换为-50到50的范围
                _rotation = adjustments.Orientation;
                _mirrorHorizontal = adjustments.FlipHorizontal;

                // 应用到摄像头管理器
                _cameraManager.SetVideoAdjustments(_brightness, _contrast, _rotation, _mirrorHorizontal, _mirrorVertical);

                Logger.Debug("MainWindow", $"已应用画面调整: 亮度={adjustments.Brightness}, 对比度={adjustments.Contrast}, 旋转={adjustments.Orientation}°, 水平翻转={adjustments.FlipHorizontal}");
            }
            catch (Exception ex)
            {
                Logger.Error("MainWindow", $"应用画面调整失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 应用透视校正
        /// </summary>
        private void ApplyPerspectiveCorrection(CameraConfig cameraConfig)
        {
            try
            {
                if (cameraConfig == null || !cameraConfig.HasCorrection) return;

                // 获取校正点
                var correctionPoints = cameraConfig.GetCorrectionPoints();
                if (correctionPoints.Count != 4)
                {
                    Logger.Warning("MainWindow", $"校正点数量无效: {correctionPoints.Count}，应为4");
                    return;
                }

                // 创建透视校正过滤器
                var filter = new QuadrilateralTransformation(
                    correctionPoints,
                    cameraConfig.SourceWidth > 0 ? cameraConfig.SourceWidth : 640,
                    cameraConfig.SourceHeight > 0 ? cameraConfig.SourceHeight : 480);

                // 应用到摄像头管理器
                _cameraManager.SetPerspectiveCorrectionFilter(filter);

                Logger.Info("MainWindow", $"透视校正已应用: 源尺寸={cameraConfig.SourceWidth}x{cameraConfig.SourceHeight}");
            }
            catch (Exception ex)
            {
                Logger.Error("MainWindow", $"应用透视校正失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 更新当前摄像头的调整参数
        /// </summary>
        private void UpdateCurrentCameraAdjustments()
        {
            try
            {
                int cameraIndex = _cameraManager.CurrentCameraIndex;

                // 创建或更新当前摄像头的配置
                if (!config.CameraConfigs.ContainsKey(cameraIndex))
                {
                    config.CameraConfigs[cameraIndex] = new CameraConfig
                    {
                        CameraIndex = cameraIndex,
                        CameraName = _cameraManager.GetCurrentCameraName()
                    };
                }

                // 更新调整参数
                config.CameraConfigs[cameraIndex].Adjustments = new ImageAdjustments
                {
                    Brightness = (int)((_brightness / 50.0 * 100) + 100), // 转换回0-200范围
                    Contrast = (int)((_contrast / 50.0 * 100) + 100),     // 转换回0-200范围
                    Orientation = _rotation,
                    FlipHorizontal = _mirrorHorizontal
                };

                Logger.Debug("MainWindow", $"已更新摄像头 {cameraIndex} 的画面调整参数");
            }
            catch (Exception ex)
            {
                Logger.Error("MainWindow", $"更新摄像头调整参数失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 保存配置
        /// </summary>
        private void SaveConfig()
        {
            try
            {
                Logger.Debug("MainWindow", "开始保存配置");

                // 创建或更新当前摄像头的配置
                int cameraIndex = _cameraManager?.CurrentCameraIndex ?? 0;

                if (!config.CameraConfigs.ContainsKey(cameraIndex))
                {
                    config.CameraConfigs[cameraIndex] = new CameraConfig
                    {
                        CameraIndex = cameraIndex,
                        CameraName = _cameraManager.GetCurrentCameraName()
                    };
                }

                // 更新当前摄像头的调整参数
                config.CameraConfigs[cameraIndex].Adjustments = new ImageAdjustments
                {
                    Brightness = (int)((_brightness / 50.0 * 100) + 100), // 转换回0-200范围
                    Contrast = (int)((_contrast / 50.0 * 100) + 100),     // 转换回0-200范围
                    Orientation = _rotation,
                    FlipHorizontal = _mirrorHorizontal
                };

                var cfg = new AppConfig
                {
                    CameraIndex = cameraIndex,
                    StartMaximized = config.StartMaximized,
                    AutoStartCamera = config.AutoStartCamera,
                    DefaultPenWidth = _drawingManager.UserPenWidth,
                    DefaultPenColor = _drawingManager.PenColor.ToString(),
                    EnableHardwareAcceleration = config.EnableHardwareAcceleration,
                    EnableFrameProcessing = config.EnableFrameProcessing,
                    FrameRateLimit = config.FrameRateLimit,
                    CameraConfigs = config.CameraConfigs,
                    Theme = config.Theme
                };

                var json = JsonConvert.SerializeObject(cfg, Formatting.Indented);
                File.WriteAllText(configPath, json, Encoding.UTF8);

                Logger.Info("MainWindow", "配置保存成功");
            }
            catch (Exception ex)
            {
                Logger.Error("MainWindow", $"保存配置失败: {ex.Message}", ex);
            }
        }

        #endregion

        #region 主题相关方法

        /// <summary>
        /// 应用主题
        /// </summary>
        private void ApplyTheme()
        {
            if (config == null) return;

            try
            {
                // 移除当前主题资源
                if (_currentTheme != null)
                {
                    Resources.MergedDictionaries.Remove(_currentTheme);
                }

                // 加载新主题资源
                string themePath;
                switch (config.Theme)
                {
                    case "Dark":
                        themePath = "Themes/DarkTheme.xaml";
                        break;
                    case "Light":
                    default:
                        themePath = "Themes/LightTheme.xaml";
                        break;
                }

                _currentTheme = new ResourceDictionary();
                _currentTheme.Source = new Uri(themePath, UriKind.Relative);
                Resources.MergedDictionaries.Add(_currentTheme);

                // 应用窗口背景颜色
                this.Background = Resources["WindowBackgroundBrush"] as WinBrush;

                // 应用工具栏背景颜色
                if (BottomToolbar != null)
                {
                    BottomToolbar.Background = Resources["ToolbarBackgroundBrush"] as WinBrush;
                }

                // 更新样式引用
                UpdateDynamicStyles();

                Logger.Info("MainWindow", $"主题已应用: {config.Theme}");
            }
            catch (Exception ex)
            {
                Logger.Error("MainWindow", $"加载主题时出错: {ex.Message}", ex);
                MessageBox.Show($"加载主题时出错: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 更新动态样式
        /// </summary>
        private void UpdateDynamicStyles()
        {
            try
            {
                // 更新按钮样式引用
                var buttonStyle = Resources["ButtonStyle"] as Style;
                var toggleButtonStyle = Resources["ToggleButtonStyle"] as Style;
                var toolToggleButtonStyle = Resources["ToolToggleButtonStyle"] as Style;
                var photoButtonStyle = Resources["PhotoButtonStyle"] as Style;
                var moreButtonStyle = Resources["MoreButtonStyle"] as Style;

                // 这里可以添加样式更新的具体逻辑
                // 例如，为特定控件重新应用样式
                if (MoveBtn != null && toolToggleButtonStyle != null)
                {
                    MoveBtn.Style = toolToggleButtonStyle;
                }
                if (PenBtn != null && toolToggleButtonStyle != null)
                {
                    PenBtn.Style = toolToggleButtonStyle;
                }
                if (EraserBtn != null && toolToggleButtonStyle != null)
                {
                    EraserBtn.Style = toolToggleButtonStyle;
                }

                Logger.Debug("MainWindow", "动态样式已更新");
            }
            catch (Exception ex)
            {
                Logger.Error("MainWindow", $"更新动态样式失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 刷新主题（可以从设置窗口回调）
        /// </summary>
        public void RefreshTheme()
        {
            LoadConfig();
            ApplyTheme();
        }

        #endregion

        #region 关闭和清理

        /// <summary>
        /// 退出应用
        /// </summary>
        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Logger.Info("MainWindow", "用户请求退出...");

                // 先处理校正模式
                if (_isPerspectiveCorrectionMode)
                {
                    ExitPerspectiveCorrectionMode(false);
                }

                // 直接关闭窗口
                Close();
            }
            catch (Exception ex)
            {
                Logger.Error("MainWindow", $"退出时发生错误: {ex.Message}", ex);
                // 作为最后手段
                try
                {
                    Environment.Exit(0);
                }
                catch { }
            }
        }

        /// <summary>
        /// 关闭所有弹出窗口
        /// </summary>
        private void CloseAllPopups()
        {
            try
            {
                PenSettingsPopup.IsOpen = false;
                MoreMenuPopup.IsOpen = false;
                PhotoPopup.IsOpen = false;
                TouchInfoPopup.IsOpen = false;
                PhotoTipPopup.IsOpen = false;

                Logger.Debug("MainWindow", "所有弹出窗口已关闭");
            }
            catch (Exception ex)
            {
                Logger.Error("MainWindow", $"关闭弹出窗口失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 清理静态和托管资源
        /// </summary>
        private void ClearStaticResources()
        {
            try
            {
                Logger.Info("MainWindow", "开始清理静态资源");

                // 清理画笔资源
                if (_drawingManager != null)
                {
                    _drawingManager.Dispose();
                    _drawingManager = null;
                }

                // 清理内存资源
                if (_memoryManager != null)
                {
                    _memoryManager.CleanupAllResources();
                    _memoryManager = null;
                }

                // 清理摄像头资源
                if (_cameraManager != null)
                {
                    _cameraManager.ReleaseCameraResources();
                    _cameraManager = null;
                }

                // 清理触控资源
                if (_touchManager != null)
                {
                    _touchManager.StopTracking();
                    _touchManager = null;
                }

                // 清理照片悬浮窗资源
                if (_photoPopupManager != null)
                {
                    _photoPopupManager.Dispose();
                    _photoPopupManager = null;
                }

                // 清理数据集合
                _photos.Clear();
                _liveStrokes = null;

                // 清理图片资源
                if (VideoImage.Source != null)
                {
                    var source = VideoImage.Source as BitmapSource;
                    if (source != null)
                    {
                        VideoImage.Source = null;
                        source = null;
                    }
                }

                // 清理画布
                Ink.Strokes.Clear();

                // 清理校正相关资源
                if (_originalCorrectionFrame != null)
                {
                    _originalCorrectionFrame.Dispose();
                    _originalCorrectionFrame = null;
                }

                // 清理配置
                config = null;

                Logger.Info("MainWindow", "静态资源清理完成");
            }
            catch (Exception ex)
            {
                Logger.Error("MainWindow", $"清理静态资源失败: {ex.Message}", ex);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                Logger.Info("MainWindow", "开始应用程序关闭流程...");
                _isClosing = true;

                // 强制退出梯形校正模式（如果正在校正）
                if (_isPerspectiveCorrectionMode)
                {
                    Logger.Warning("MainWindow", "检测到校正模式未正确退出，强制退出");
                    ForceExitCorrectionMode();
                }

                // 关闭所有弹出窗口
                CloseAllPopups();

                // 取消所有事件订阅
                UnsubscribeAllEvents();

                // 清理照片悬浮窗管理器
                _photoPopupManager?.Dispose();

                // 记录系统状态摘要
                _logManager?.LogSystemStatus();

                // 保存配置（必须在清理前调用）
                SaveConfig();

                // 清理所有管理器
                _touchManager?.StopTracking();
                _cameraManager?.ReleaseCameraResources();
                _memoryManager?.CleanupAllResources();
                _drawingManager?.Dispose();

                // 清理静态资源
                ClearStaticResources();

                Logger.Info("MainWindow", "应用程序关闭流程完成");
            }
            catch (Exception ex)
            {
                Logger.Error("MainWindow", $"关闭过程中发生错误: {ex.Message}", ex);
            }
            finally
            {
                // 关闭日志系统
                Logger.Shutdown();
                base.OnClosed(e);
            }
        }

        #endregion

        #region 模式切换和UI事件

        private void SetMode(DrawingManager.ToolMode mode, bool initial = false)
        {
            try
            {
                _drawingManager.SetMode(mode, initial);
                MoveBtn.IsChecked = mode == DrawingManager.ToolMode.Move;
                PenBtn.IsChecked = mode == DrawingManager.ToolMode.Pen;
                EraserBtn.IsChecked = mode == DrawingManager.ToolMode.Eraser;

                if (mode == DrawingManager.ToolMode.Pen)
                {
                    _panZoomManager.ApplyStrokeScaleCompensation();
                }

                Logger.Debug("MainWindow", $"切换到模式: {mode}");
            }
            catch (Exception ex)
            {
                Logger.Error("MainWindow", $"设置模式失败: {ex.Message}", ex);
            }
        }

        private void MoveBtn_Click(object sender, RoutedEventArgs e)
        {
            // 如果当前不是移动模式，切换到移动模式
            if (_drawingManager.CurrentMode != DrawingManager.ToolMode.Move)
            {
                SetMode(DrawingManager.ToolMode.Move);

                // 关闭画笔设置悬浮窗
                PenSettingsPopup.IsOpen = false;
            }
            else
            {
                // 如果已经是移动模式，保持选中状态（不取消）
                MoveBtn.IsChecked = true;
            }
        }

        private void EraserBtn_Click(object sender, RoutedEventArgs e)
        {
            // 如果当前不是橡皮擦模式，切换到橡皮擦模式
            if (_drawingManager.CurrentMode != DrawingManager.ToolMode.Eraser)
            {
                SetMode(DrawingManager.ToolMode.Eraser);

                // 关闭画笔设置悬浮窗
                PenSettingsPopup.IsOpen = false;
            }
            else
            {
                // 如果已经是橡皮擦模式，保持选中状态（不取消）
                EraserBtn.IsChecked = true;

                // 如果需要清屏功能，可以在这里添加确认对话框
                if (MessageBox.Show("确定要清除所有笔迹吗？", "清屏确认", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    ClearInk_Click(sender, e);
                }
            }
        }

        private void PenWidthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (PenWidthValue != null)
            {
                PenWidthValue.Text = e.NewValue.ToString("0");
                _panZoomManager.SetOriginalPenWidth(e.NewValue);
                Logger.Debug("MainWindow", $"笔迹宽度设置为: {e.NewValue:F1}");
            }
        }

        private void ColorButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string colorName)
            {
                try
                {
                    // 1. 更新UI选中状态
                    SelectColorButton(colorName);

                    // 2. 设置画笔颜色
                    var color = GetColorFromName(colorName);
                    _drawingManager.SetPenColor(color);

                    // 3. 应用笔迹缩放补偿
                    _panZoomManager.ApplyStrokeScaleCompensation();

                    // 4. 记录日志
                    Logger.Debug("MainWindow", $"画笔颜色设置为: {colorName}");
                }
                catch (Exception ex)
                {
                    Logger.Error("MainWindow", $"设置画笔颜色失败: {ex.Message}", ex);
                }
            }
        }

        private void ClosePenSettings_Click(object sender, RoutedEventArgs e)
        {
            PenSettingsPopup.IsOpen = false;
            Logger.Debug("MainWindow", "关闭画笔设置悬浮窗");
        }

        private void ClearInk_Click(object sender, RoutedEventArgs e)
        {
            if (_isClosing) return;
            _drawingManager.ClearStrokes();
            Logger.Info("MainWindow", "清除所有笔迹");
        }

        private void UndoInk_Click(object sender, RoutedEventArgs e)
        {
            if (_isClosing) return;
            _drawingManager.Undo();
            Logger.Debug("MainWindow", "撤销操作");
        }

        private void RedoInk_Click(object sender, RoutedEventArgs e)
        {
            if (_isClosing) return;
            _drawingManager.Redo();
            Logger.Debug("MainWindow", "重做操作");
        }

        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void MoreButton_Click(object sender, RoutedEventArgs e)
        {
            MoreMenuPopup.IsOpen = !MoreMenuPopup.IsOpen;
            Logger.Debug("MainWindow", "切换更多菜单显示状态");
        }

        private void TogglePhotoPanel_Click(object sender, RoutedEventArgs e)
        {
            _photoPopupManager.TogglePhotoPopup();
            Logger.Debug("MainWindow", "切换照片面板显示状态");
        }

        private void BackToLive_Click(object sender, RoutedEventArgs e)
        {
            _photoPopupManager.BackToLive();
        }

        #endregion

        #region 公开属性（用于数据绑定）

        /// <summary>
        /// 当前选中的照片（用于数据绑定）
        /// </summary>
        public PhotoWithStrokes CurrentPhoto
        {
            get { return _photoPopupManager?.CurrentPhoto; }
        }

        #endregion
    }
}