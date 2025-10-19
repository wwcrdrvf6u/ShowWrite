using AForge;
using AForge.Imaging.Filters;
using Newtonsoft.Json;
using ShowWrite.Models;
using ShowWrite.Services;
using ShowWrite.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;
using Cursors = System.Windows.Input.Cursors;
using D = System.Drawing;
using MessageBox = System.Windows.MessageBox;
using WinForms = System.Windows.Forms;

namespace ShowWrite
{
    public partial class MainWindow : Window
    {
        private readonly VideoService _videoService = new();
        private readonly ObservableCollection<PhotoWithStrokes> _photos = new();
        private PhotoWithStrokes? _currentPhoto;
        private bool _isLiveMode = true;
        private int currentCameraIndex = 0;
        // 透视校正过滤器
        private QuadrilateralTransformation? _perspectiveCorrectionFilter;
        // 配置对象
        private AppConfig config = new AppConfig();
        // 画面调节参数
        private double _brightness = 0.0;
        private double _contrast = 0.0;
        private int _rotation = 0;
        private bool _mirrorHorizontal = false;
        private bool _mirrorVertical = false;
        // 配置文件路径
        private readonly string configPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
        // 绘制管理器
        private readonly DrawingManager _drawingManager;
        // 双击检测
        private DateTime _lastClickTime = DateTime.MinValue;
        private const int DoubleClickDelay = 300; // 毫秒
        // 触控跟踪
        private readonly Dictionary<int, System.Windows.Point> _currentTouches = new Dictionary<int, System.Windows.Point>();
        private DispatcherTimer _touchUpdateTimer;
        // 新增：内存优化相关字段
        private readonly object _frameLock = new object();
        private D.Bitmap _lastProcessedFrame;
        private bool _cameraStoppedInPhotoMode = false;
        // 新增：实时模式的笔迹集合
        private StrokeCollection _liveStrokes = new StrokeCollection();
        // 新增：缩放相关字段
        private ScaleTransform _scaleTransform = new ScaleTransform();
        private TranslateTransform _translateTransform = new TranslateTransform();
        private TransformGroup _transformGroup = new TransformGroup();
        private System.Windows.Point _lastTouchPoint1;
        private System.Windows.Point _lastTouchPoint2;
        private double _lastTouchDistance;
        private bool _isZooming = false;
        // 新增：鼠标拖拽平移状态（修复版本）
        private System.Windows.Point _dragStartPoint;
        private System.Windows.Point _startTranslate;
        private bool _isDragging = false;
        // 新增：TouchSDK 面积
        private double _sdkTouchArea = 0;
        // 新增：应用程序关闭标志
        private bool _isClosing = false;

        // 新增：笔迹缩放补偿相关字段
        private double _originalPenWidth = 2.0; // 原始笔迹宽度
        private double _currentScaleFactor = 1.0; // 当前缩放因子

        // 新增：摄像头状态相关字段
        private bool _cameraAvailable = true;
        private SolidColorBrush _noCameraBackground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 40, 40));

        // 新增：支持笔迹的照片包装类
        public class PhotoWithStrokes
        {
            public CapturedImage CapturedImage { get; set; }
            public StrokeCollection Strokes { get; set; }
            public BitmapSource Image => CapturedImage.Image;
            // 新增：缩略图属性
            public BitmapSource Thumbnail { get; set; }
            // 新增：时间戳
            public string Timestamp { get; set; }

            public PhotoWithStrokes(CapturedImage capturedImage)
            {
                CapturedImage = capturedImage;
                Strokes = new StrokeCollection();
                Timestamp = DateTime.Now.ToString("MM-dd HH:mm:ss");
                // 创建缩略图
                Thumbnail = CreateThumbnail(capturedImage.Image, 120, 90);
            }

            /// <summary>
            /// 创建缩略图
            /// </summary>
            private BitmapSource CreateThumbnail(BitmapSource source, int width, int height)
            {
                try
                {
                    var scaleX = (double)width / source.PixelWidth;
                    var scaleY = (double)height / source.PixelHeight;
                    var scale = Math.Min(scaleX, scaleY);
                    var scaledWidth = (int)(source.PixelWidth * scale);
                    var scaledHeight = (int)(source.PixelHeight * scale);
                    var thumbnail = new TransformedBitmap(source,
                        new ScaleTransform(scale, scale));
                    var result = new CroppedBitmap(thumbnail,
                        new Int32Rect((thumbnail.PixelWidth - scaledWidth) / 2,
                                     (thumbnail.PixelHeight - scaledHeight) / 2,
                                     scaledWidth, scaledHeight));
                    result.Freeze();
                    return result;
                }
                catch (Exception)
                {
                    return source;
                }
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            PhotoList.ItemsSource = _photos;
            // 初始化绘制管理器
            _drawingManager = new DrawingManager(Ink, VideoArea, this);
            // 订阅 TouchSDK 面积变化事件
            _drawingManager.OnSDKTouchAreaChanged += OnSDKTouchAreaChanged;
            // 初始化实时模式笔迹
            _drawingManager.SwitchToPhotoStrokes(_liveStrokes);
            // 初始化重做按钮状态 - 已移除对 RedoBtn 的引用
            // UpdateRedoButtonState(); // 注释掉或删除，因为 RedoBtn 不存在
            // 初始化缩放变换
            InitializeZoomTransform();
            // 确保 config 不为 null
            if (config == null)
            {
                config = new AppConfig();
            }
            // 加载配置
            LoadConfig();
            // 应用窗口设置
            WindowStyle = WindowStyle.None;
            WindowState = config.StartMaximized ? WindowState.Maximized : WindowState.Normal;
            // 应用绘制管理器配置
            _drawingManager.ApplyConfig(config);
            // 初始化画笔设置悬浮窗
            InitializePenSettingsPopup();
            // 初始化触控更新定时器
            InitializeTouchUpdateTimer();
            // 设置触控信息悬浮窗初始位置
            PositionTouchInfoPopup();
            // 显示 TouchSDK 初始化状态
            UpdateTouchSDKStatus();

            _videoService.OnNewFrameProcessed += frame =>
            {
                // 如果正在关闭，忽略新帧
                if (_isClosing) return;

                Dispatcher.Invoke(() =>
                {
                    if (_isLiveMode && !_isClosing)
                    {
                        try
                        {
                            lock (_frameLock)
                            {
                                // 释放上一帧资源
                                if (_lastProcessedFrame != null && !_lastProcessedFrame.Equals(frame))
                                {
                                    _lastProcessedFrame.Dispose();
                                    _lastProcessedFrame = null;
                                }
                                using var processed = ProcessFrame((D.Bitmap)frame.Clone(), applyAdjustments: true);
                                // 保存当前帧引用用于下次释放
                                _lastProcessedFrame = (D.Bitmap)processed.Clone();
                                VideoImage.Source = BitmapToBitmapImage(processed);
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"视频帧处理错误: {ex.Message}");
                        }
                        finally
                        {
                            // 实时释放当前帧资源
                            if (!frame.Equals(_lastProcessedFrame))
                            {
                                frame.Dispose();
                            }
                        }
                    }
                    else
                    {
                        // 非实时模式下立即释放帧
                        frame.Dispose();
                    }
                });
            };

            // 检查摄像头可用性
            CheckCameraAvailability();

            // 如果配置为自动启动摄像头，则尝试启动
            if (config.AutoStartCamera && _cameraAvailable && !_videoService.Start(currentCameraIndex))
            {
                MessageBox.Show("未找到可用摄像头。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                _cameraAvailable = false;
                ShowNoCameraBackground();
            }
        }

        // =========================
        // 新增：摄像头可用性检查
        // =========================
        private void CheckCameraAvailability()
        {
            try
            {
                var cameras = _videoService.GetAvailableCameras();
                _cameraAvailable = cameras != null && cameras.Count > 0;

                if (!_cameraAvailable)
                {
                    Console.WriteLine("未检测到可用摄像头");
                    ShowNoCameraBackground();
                }
                else
                {
                    Console.WriteLine($"检测到 {cameras.Count} 个摄像头");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"检查摄像头可用性失败: {ex.Message}");
                _cameraAvailable = false;
                ShowNoCameraBackground();
            }
        }

        // =========================
        // 新增：显示无摄像头背景
        // =========================
        private void ShowNoCameraBackground()
        {
            Dispatcher.Invoke(() =>
            {
                VideoImage.Source = null;
                VideoArea.Background = _noCameraBackground;

                // 显示提示文本
                var textBlock = new TextBlock
                {
                    Text = "未检测到摄像头\n批注功能仍可使用",
                    Foreground = Brushes.White,
                    FontSize = 16,
                    FontWeight = FontWeights.Bold,
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };

                // 确保视频区域可以接收输入事件
                VideoArea.IsHitTestVisible = true;
                VideoArea.Focusable = true;

                Console.WriteLine("已切换到无摄像头模式，批注功能可用");
            });
        }

        // =========================
        // TouchSDK 相关方法
        // =========================
        /// <summary>
        /// TouchSDK 面积变化事件处理
        /// </summary>
        private void OnSDKTouchAreaChanged(double area)
        {
            if (_isClosing) return;

            Dispatcher.Invoke(() =>
            {
                _sdkTouchArea = area;
                UpdateSDKTouchAreaDisplay();
            });
        }

        /// <summary>
        /// 更新 SDK 面积显示
        /// </summary>
        private void UpdateSDKTouchAreaDisplay()
        {
            if (SDKTouchAreaText != null)
            {
                SDKTouchAreaText.Text = $"SDK面积: {_sdkTouchArea:F0} 像素²";
            }
        }

        /// <summary>
        /// 更新 TouchSDK 状态显示
        /// </summary>
        private void UpdateTouchSDKStatus()
        {
            if (_drawingManager.IsTouchSDKInitialized)
            {
                Console.WriteLine("TouchSDK 已成功初始化");
                // 可以在界面上显示 TouchSDK 状态
                if (TouchCountText != null)
                {
                    TouchCountText.Text = $"触控点数: 0 (TouchSDK 就绪)";
                }
            }
            else
            {
                Console.WriteLine("TouchSDK 未初始化或初始化失败");
                if (TouchCountText != null)
                {
                    TouchCountText.Text = $"触控点数: 0 (TouchSDK 未就绪)";
                }
            }
        }

        // =========================
        // 修复：缩放功能初始化
        // =========================
        private void InitializeZoomTransform()
        {
            try
            {
                // 清除可能存在的旧变换
                if (VideoArea.RenderTransform != null)
                {
                    VideoArea.RenderTransform = null;
                }
                // 重新创建变换组
                _transformGroup = new TransformGroup();
                _scaleTransform = new ScaleTransform();
                _translateTransform = new TranslateTransform();
                _transformGroup.Children.Add(_scaleTransform);
                _transformGroup.Children.Add(_translateTransform);
                // 确保 VideoArea 是变换的根容器
                VideoArea.RenderTransform = _transformGroup;
                VideoArea.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
                Console.WriteLine("变换系统初始化完成");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"变换初始化错误: {ex.Message}");
            }
        }

        // =========================
        // 缩放控制方法（添加笔迹缩放补偿）
        // =========================
        private void ResetZoom()
        {
            _scaleTransform.ScaleX = 1.0;
            _scaleTransform.ScaleY = 1.0;
            _translateTransform.X = 0;
            _translateTransform.Y = 0;

            // 重置缩放因子
            _currentScaleFactor = 1.0;

            // 重置笔迹粗细
            ApplyStrokeScaleCompensation();
        }

        private void ZoomAtPoint(double scaleFactor, System.Windows.Point center)
        {
            // 计算相对于当前变换的缩放中心
            var relativeX = (center.X - _translateTransform.X) / _scaleTransform.ScaleX;
            var relativeY = (center.Y - _translateTransform.Y) / _scaleTransform.ScaleY;
            // 应用缩放
            _scaleTransform.ScaleX *= scaleFactor;
            _scaleTransform.ScaleY *= scaleFactor;
            // 限制缩放范围
            _scaleTransform.ScaleX = Math.Max(0.1, Math.Min(10, _scaleTransform.ScaleX));
            _scaleTransform.ScaleY = Math.Max(0.1, Math.Min(10, _scaleTransform.ScaleY));
            // 调整位置以保持缩放中心点不变
            _translateTransform.X = center.X - relativeX * _scaleTransform.ScaleX;
            _translateTransform.Y = center.Y - relativeY * _scaleTransform.ScaleY;

            // 更新当前缩放因子
            _currentScaleFactor = _scaleTransform.ScaleX;

            // 应用笔迹缩放补偿
            ApplyStrokeScaleCompensation();
        }

        // =========================
        // 新增：笔迹缩放补偿方法
        // =========================
        private void ApplyStrokeScaleCompensation()
        {
            if (_drawingManager == null) return;

            try
            {
                // 计算补偿后的笔迹宽度
                double compensatedWidth = _originalPenWidth / _currentScaleFactor;

                // 限制最小和最大笔迹宽度
                compensatedWidth = Math.Max(1.0, Math.Min(50.0, compensatedWidth));

                // 更新绘制管理器的笔迹宽度
                _drawingManager.UserPenWidth = compensatedWidth;
                _drawingManager.UpdatePenAttributes();

                Console.WriteLine($"缩放补偿: 缩放因子={_currentScaleFactor:F2}, 笔迹宽度={compensatedWidth:F2}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"应用笔迹缩放补偿失败: {ex.Message}");
            }
        }

        // =========================
        // 修复：鼠标拖拽平移功能
        // =========================
        private void VideoArea_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_drawingManager.CurrentMode == DrawingManager.ToolMode.Move &&
                e.ChangedButton == MouseButton.Left)
            {
                try
                {
                    _isDragging = true;
                    _dragStartPoint = e.GetPosition(this); // 使用窗口坐标
                    _startTranslate = new System.Windows.Point(_translateTransform.X, _translateTransform.Y);
                    VideoArea.CaptureMouse();
                    e.Handled = true;
                    // 设置光标样式
                    this.Cursor = Cursors.SizeAll;
                    Console.WriteLine($"开始拖拽: 起始点=({_dragStartPoint.X:F1}, {_dragStartPoint.Y:F1}), " +
                                    $"起始平移=({_startTranslate.X:F1}, {_startTranslate.Y:F1})");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"鼠标按下错误: {ex.Message}");
                    _isDragging = false;
                }
            }
            else
            {
                _drawingManager.HandleMouseDown(e);
            }
        }

        private void VideoArea_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_isDragging && _drawingManager.CurrentMode == DrawingManager.ToolMode.Move)
            {
                try
                {
                    // 使用窗口坐标计算位移，避免变换影响
                    var currentScreenPosition = e.GetPosition(this);
                    var deltaScreen = new System.Windows.Vector(
                        currentScreenPosition.X - _dragStartPoint.X,
                        currentScreenPosition.Y - _dragStartPoint.Y);
                    // 考虑缩放因子，使移动更自然
                    var scaledDeltaX = deltaScreen.X / _scaleTransform.ScaleX;
                    var scaledDeltaY = deltaScreen.Y / _scaleTransform.ScaleY;
                    // 应用位移到变换
                    _translateTransform.X = _startTranslate.X + scaledDeltaX;
                    _translateTransform.Y = _startTranslate.Y + scaledDeltaY;
                    // 添加边界限制
                    ApplyBoundaryLimits();
                    e.Handled = true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"鼠标移动错误: {ex.Message}");
                }
            }
            else
            {
                _drawingManager.HandleMouseMove(e);
            }
        }

        private void VideoArea_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging && e.ChangedButton == MouseButton.Left)
            {
                try
                {
                    _isDragging = false;
                    VideoArea.ReleaseMouseCapture();
                    // 恢复默认光标
                    this.Cursor = Cursors.Arrow;
                    e.Handled = true;
                    Console.WriteLine($"结束拖拽: 最终平移=({_translateTransform.X:F1}, {_translateTransform.Y:F1})");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"鼠标释放错误: {ex.Message}");
                }
            }
            else
            {
                _drawingManager.HandleMouseUp(e);
            }
        }

        // =========================
        // 新增：边界限制方法
        // =========================
        private void ApplyBoundaryLimits()
        {
            try
            {
                // 根据缩放级别动态计算边界限制
                var maxTranslation = 2000 * _scaleTransform.ScaleX; // 缩放越大，允许的平移范围越大
                var minTranslation = -2000 * _scaleTransform.ScaleX;
                // 应用边界限制
                _translateTransform.X = Math.Max(minTranslation, Math.Min(maxTranslation, _translateTransform.X));
                _translateTransform.Y = Math.Max(minTranslation, Math.Min(maxTranslation, _translateTransform.Y));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"应用边界限制错误: {ex.Message}");
            }
        }

        // =========================
        // 修复：鼠标滚轮缩放功能（添加笔迹补偿）
        // =========================
        private void Window_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            // 只在移动模式下启用缩放
            if (_drawingManager.CurrentMode == DrawingManager.ToolMode.Move)
            {
                var delta = e.Delta / 120.0;
                var scaleFactor = Math.Pow(1.2, delta);
                var mousePos = e.GetPosition(VideoArea);
                ZoomAtPoint(scaleFactor, mousePos);
                e.Handled = true;
                // 应用边界限制
                ApplyBoundaryLimits();
            }
            else
            {
                _drawingManager.HandleMouseWheel(e);
            }
        }

        // =========================
        // 触控信息悬浮窗控制
        // =========================
        private void PositionTouchInfoPopup()
        {
            // 设置悬浮窗初始位置在右上角
            TouchInfoPopup.HorizontalOffset = SystemParameters.PrimaryScreenWidth - 200;
            TouchInfoPopup.VerticalOffset = 50;
        }

        private void InitializeTouchUpdateTimer()
        {
            _touchUpdateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100) // 每100ms更新一次
            };
            _touchUpdateTimer.Tick += (s, e) => UpdateTouchInfo();
            _touchUpdateTimer.Start();
        }

        private void UpdateTouchInfo()
        {
            if (_isClosing) return;

            int touchCount = _currentTouches.Count;
            if (touchCount > 0)
            {
                // 显示悬浮窗
                TouchInfoPopup.IsOpen = true;
                // 更新触控点数显示
                if (_drawingManager.IsTouchSDKInitialized)
                {
                    TouchCountText.Text = $"触控点数: {touchCount} (TouchSDK 就绪)";
                }
                else
                {
                    TouchCountText.Text = $"触控点数: {touchCount} (TouchSDK 未就绪)";
                }
                // 计算触控中心点
                System.Windows.Point center = CalculateTouchCenter();
                TouchCenterText.Text = $"中心: ({center.X:F0}, {center.Y:F0})";
                if (touchCount >= 3)
                {
                    double area = CalculatePolygonArea(_currentTouches.Values.ToList());
                    TouchAreaText.Text = $"面积: {area:F0} 像素²";
                }
                else
                {
                    TouchAreaText.Text = "面积: 需要3个以上点";
                }
                // 更新 SDK 面积显示
                UpdateSDKTouchAreaDisplay();
            }
            else
            {
                // 如果没有触控点，但用户没有手动关闭，则保持显示
                // 如果用户手动关闭了，则不再自动打开
                // 更新 TouchSDK 状态显示
                if (_drawingManager.IsTouchSDKInitialized)
                {
                    TouchCountText.Text = "触控点数: 0 (TouchSDK 就绪)";
                }
                else
                {
                    TouchCountText.Text = "触控点数: 0 (TouchSDK 未就绪)";
                }
                TouchAreaText.Text = "面积: 0 像素²";
                TouchCenterText.Text = "中心: (0, 0)";
                UpdateSDKTouchAreaDisplay();
            }
        }

        private void CloseTouchInfo_Click(object sender, RoutedEventArgs e)
        {
            TouchInfoPopup.IsOpen = false;
        }

        private System.Windows.Point CalculateTouchCenter()
        {
            if (_currentTouches.Count == 0)
                return new System.Windows.Point(0, 0);
            double centerX = 0, centerY = 0;
            foreach (var point in _currentTouches.Values)
            {
                centerX += point.X;
                centerY += point.Y;
            }
            centerX /= _currentTouches.Count;
            centerY /= _currentTouches.Count;
            return new System.Windows.Point(centerX, centerY);
        }

        private double CalculatePolygonArea(List<System.Windows.Point> points)
        {
            if (points.Count < 3)
                return 0;
            // 使用鞋带公式计算多边形面积
            double area = 0;
            int n = points.Count;
            for (int i = 0; i < n; i++)
            {
                System.Windows.Point current = points[i];
                System.Windows.Point next = points[(i + 1) % n];
                area += (current.X * next.Y - next.X * current.Y);
            }
            return Math.Abs(area / 2.0);
        }

        // =========================
        // 触摸事件处理（增强双指缩放，添加笔迹补偿）
        // =========================
        protected override void OnTouchDown(TouchEventArgs e)
        {
            if (_isClosing) return;

            base.OnTouchDown(e);
            // 记录触摸点
            var touchPoint = e.GetTouchPoint(VideoArea);
            _currentTouches[e.TouchDevice.Id] = touchPoint.Position;
            // 双指缩放检测
            if (_currentTouches.Count == 2 && _drawingManager.CurrentMode == DrawingManager.ToolMode.Move)
            {
                var points = _currentTouches.Values.ToArray();
                _lastTouchPoint1 = points[0];
                _lastTouchPoint2 = points[1];
                _lastTouchDistance = GetDistance(_lastTouchPoint1, _lastTouchPoint2);
                _isZooming = true;
            }
            _drawingManager.HandleTouchDown(e);
        }

        protected override void OnTouchMove(TouchEventArgs e)
        {
            if (_isClosing) return;

            base.OnTouchMove(e);
            // 更新触摸点位置
            if (_currentTouches.ContainsKey(e.TouchDevice.Id))
            {
                var touchPoint = e.GetTouchPoint(VideoArea);
                _currentTouches[e.TouchDevice.Id] = touchPoint.Position;
            }
            // 双指缩放处理
            if (_isZooming && _currentTouches.Count == 2 && _drawingManager.CurrentMode == DrawingManager.ToolMode.Move)
            {
                var points = _currentTouches.Values.ToArray();
                var currentDistance = GetDistance(points[0], points[1]);
                if (Math.Abs(currentDistance - _lastTouchDistance) > 10) // 避免微小移动
                {
                    var scaleFactor = currentDistance / _lastTouchDistance;
                    // 计算缩放中心点
                    var center = new System.Windows.Point(
                        (points[0].X + points[1].X) / 2,
                        (points[0].Y + points[1].Y) / 2
                    );
                    // 应用缩放
                    ZoomAtPoint(scaleFactor, center);
                    _lastTouchDistance = currentDistance;
                    _lastTouchPoint1 = points[0];
                    _lastTouchPoint2 = points[1];
                    // 应用边界限制
                    ApplyBoundaryLimits();
                }
            }
            _drawingManager.HandleTouchMove(e);
        }

        protected override void OnTouchUp(TouchEventArgs e)
        {
            if (_isClosing) return;

            base.OnTouchUp(e);
            // 移除触摸点
            if (_currentTouches.ContainsKey(e.TouchDevice.Id))
            {
                _currentTouches.Remove(e.TouchDevice.Id);
            }
            // 重置缩放状态
            if (_currentTouches.Count < 2)
            {
                _isZooming = false;
            }
            _drawingManager.HandleTouchUp(e);
        }

        // =========================
        // 新增：双指缩放辅助方法
        // =========================
        private double GetDistance(System.Windows.Point p1, System.Windows.Point p2)
        {
            return Math.Sqrt(Math.Pow(p2.X - p1.X, 2) + Math.Pow(p2.Y - p1.Y, 2));
        }

        // =========================
        // 初始化画笔设置悬浮窗（保存原始宽度）
        // =========================
        private void InitializePenSettingsPopup()
        {
            // 设置初始笔宽并保存原始宽度
            _originalPenWidth = _drawingManager.UserPenWidth;
            PenWidthSlider.Value = _originalPenWidth;
            PenWidthValue.Text = _originalPenWidth.ToString("0");
        }

        // =========================
        // 模式切换委托给绘制管理器（添加笔迹补偿）
        // =========================
        private void SetMode(DrawingManager.ToolMode mode, bool initial = false)
        {
            _drawingManager.SetMode(mode, initial);
            MoveBtn.IsChecked = mode == DrawingManager.ToolMode.Move;
            PenBtn.IsChecked = mode == DrawingManager.ToolMode.Pen;
            EraserBtn.IsChecked = mode == DrawingManager.ToolMode.Eraser;

            // 确保笔迹宽度保持缩放补偿
            if (mode == DrawingManager.ToolMode.Pen)
            {
                ApplyStrokeScaleCompensation();
            }
        }

        private void MoveBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_drawingManager.CurrentMode != DrawingManager.ToolMode.Move)
                SetMode(DrawingManager.ToolMode.Move);
            else
                MoveBtn.IsChecked = true;
        }

        private void PenBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_drawingManager.CurrentMode == DrawingManager.ToolMode.Pen)
            {
                // 切换画笔设置悬浮窗的显示状态
                PenSettingsPopup.IsOpen = !PenSettingsPopup.IsOpen;
                PenBtn.IsChecked = true;
            }
            else
            {
                SetMode(DrawingManager.ToolMode.Pen);
            }
        }

        private void EraserBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_drawingManager.CurrentMode == DrawingManager.ToolMode.Eraser)
            {
                if (MessageBox.Show("确定要清除所有笔迹吗？", "清屏确认", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    ClearInk_Click(sender, e);
                }
                EraserBtn.IsChecked = true;
            }
            else
            {
                SetMode(DrawingManager.ToolMode.Eraser);
            }
        }

        // =========================
        // 画笔设置悬浮窗事件处理（更新原始宽度）
        // =========================
        private void PenWidthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (PenWidthValue != null)
            {
                PenWidthValue.Text = e.NewValue.ToString("0");

                // 更新原始笔迹宽度
                _originalPenWidth = e.NewValue;

                // 应用缩放补偿
                ApplyStrokeScaleCompensation();
            }
        }

        private void ColorButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button button && button.Tag is string colorName)
            {
                var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorName);
                _drawingManager.SetPenColor(color);

                // 确保笔迹宽度保持缩放补偿
                ApplyStrokeScaleCompensation();
            }
        }

        private void ClosePenSettings_Click(object sender, RoutedEventArgs e)
        {
            PenSettingsPopup.IsOpen = false;
        }

        // =========================
        // 更多按钮功能 - 修复悬浮窗显示
        // =========================
        private void MoreButton_Click(object sender, RoutedEventArgs e)
        {
            MoreMenuPopup.IsOpen = !MoreMenuPopup.IsOpen;
        }

        private void MoreMenuPopup_Closed(object sender, EventArgs e)
        {
            // 确保更多按钮状态正确
        }

        // =========================
        // 照片面板（增强缩略图显示，重置笔迹补偿）
        // =========================
        private void TogglePhotoPanel_Click(object sender, RoutedEventArgs e)
        {
            PhotoPopup.IsOpen = !PhotoPopup.IsOpen;
        }

        private void PhotoList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isClosing) return;

            if (PhotoList.SelectedItem is PhotoWithStrokes photoWithStrokes)
            {
                // 保存当前实时模式的笔迹
                _liveStrokes = new StrokeCollection(_drawingManager.GetStrokes());
                _isLiveMode = false;
                _currentPhoto = photoWithStrokes;
                VideoImage.Source = photoWithStrokes.Image;
                // 切换绘制管理器的StrokeCollection到照片的笔迹
                _drawingManager.SwitchToPhotoStrokes(photoWithStrokes.Strokes);
                // UpdateRedoButtonState(); // 已移除对 RedoBtn 的引用
                // 重置缩放状态
                ResetZoom();
                // 释放摄像头资源
                ReleaseCameraResources();
                // 触发GC释放旧资源
                TriggerMemoryCleanup();
            }
        }

        private void BackToLive_Click(object sender, RoutedEventArgs e)
        {
            if (_isClosing) return;

            // 如果当前在照片模式，保存照片的笔迹
            if (_currentPhoto != null)
            {
                _currentPhoto.Strokes = new StrokeCollection(_drawingManager.GetStrokes());
            }
            _isLiveMode = true;
            _currentPhoto = null;
            // 取消照片列表的选中状态
            PhotoList.SelectedItem = null;
            // 切换回实时模式的笔迹
            _drawingManager.SwitchToPhotoStrokes(_liveStrokes);
            // UpdateRedoButtonState(); // 已移除对 RedoBtn 的引用
            // 重置缩放状态
            ResetZoom();
            // 重新启动摄像头
            RestartCamera();
            // 触发GC释放照片资源
            TriggerMemoryCleanup();
        }

        private async void ShowPhotoTip()
        {
            if (_isClosing) return;

            PhotoTipPopup.IsOpen = true;
            await Task.Delay(3000);
            PhotoTipPopup.IsOpen = false;
        }

        // =========================
        // 新增：摄像头资源管理方法（增强无摄像头支持）
        // =========================
        /// <summary>
        /// 释放摄像头资源
        /// </summary>
        private void ReleaseCameraResources()
        {
            try
            {
                if (!_cameraStoppedInPhotoMode && _cameraAvailable)
                {
                    Console.WriteLine("开始释放摄像头资源...");
                    _videoService.Stop();
                    _cameraStoppedInPhotoMode = true;

                    // 等待摄像头完全停止
                    Task.Delay(100).Wait();

                    // 强制垃圾回收
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();

                    Console.WriteLine("摄像头资源已完全释放");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"释放摄像头资源失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 重启摄像头
        /// </summary>
        private void RestartCamera()
        {
            try
            {
                if (_cameraStoppedInPhotoMode && _cameraAvailable)
                {
                    Console.WriteLine("重新启动摄像头...");
                    if (!_videoService.Start(currentCameraIndex))
                    {
                        MessageBox.Show("摄像头启动失败。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        _cameraAvailable = false;
                        ShowNoCameraBackground();
                    }
                    _cameraStoppedInPhotoMode = false;
                    Console.WriteLine("摄像头已重新启动");
                }
                else if (!_cameraAvailable)
                {
                    // 无摄像头时显示背景
                    ShowNoCameraBackground();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"摄像头启动失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                _cameraAvailable = false;
                ShowNoCameraBackground();
            }
        }

        // =========================
        // 增强的内存优化方法
        // =========================
        private void TriggerMemoryCleanup()
        {
            if (_isClosing) return;

            // 在后台线程执行内存清理，避免阻塞UI
            Task.Run(() =>
            {
                try
                {
                    // 清理未使用的位图资源
                    if (_lastProcessedFrame != null)
                    {
                        lock (_frameLock)
                        {
                            _lastProcessedFrame?.Dispose();
                            _lastProcessedFrame = null;
                        }
                    }
                    // 强制垃圾回收
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    // 记录内存状态（调试用）
                    Dispatcher.Invoke(() =>
                    {
                        var memory = GC.GetTotalMemory(false) / 1024 / 1024;
                        Console.WriteLine($"内存清理完成，当前内存使用: {memory} MB");
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"内存清理错误: {ex.Message}");
                }
            });
        }

        // =========================
        // 按钮功能（增强无摄像头支持）
        // =========================
        private void Capture_Click(object sender, RoutedEventArgs e)
        {
            if (_isClosing) return;

            // 检查摄像头可用性
            if (!_cameraAvailable)
            {
                MessageBox.Show("没有可用的摄像头，无法拍照。", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var bmp = _videoService.GetFrameCopy();
            if (bmp != null)
            {
                D.Bitmap? processedBmp = null;
                try
                {
                    processedBmp = ProcessFrame(bmp, applyAdjustments: true);
                    var img = BitmapToBitmapImage(processedBmp);
                    // 创建照片对象，并保存当前笔迹
                    var capturedImage = new CapturedImage(img);
                    var photo = new PhotoWithStrokes(capturedImage);
                    photo.Strokes = new StrokeCollection(_drawingManager.GetStrokes());
                    _photos.Insert(0, photo);
                    _currentPhoto = photo;
                    // 显示提示
                    ShowPhotoTip();
                    // 触发内存清理
                    TriggerMemoryCleanup();
                }
                finally
                {
                    bmp.Dispose();
                    processedBmp?.Dispose();
                }
            }
            else
            {
                MessageBox.Show("无法获取摄像头画面。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveImage_Click(object sender, RoutedEventArgs e)
        {
            if (_isClosing) return;

            if (_currentPhoto == null)
            {
                MessageBox.Show("请先拍照或选择一张图片。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var dlg = new WinForms.SaveFileDialog
            {
                Filter = "PNG 图片|*.png|JPEG 图片|*.jpg",
                FileName = $"Capture_{DateTime.Now:yyyyMMdd_HHmmss}.png"
            };
            if (dlg.ShowDialog() == WinForms.DialogResult.OK)
            {
                try
                {
                    // 保存包含批注的图片
                    SaveImageWithInk(_currentPhoto.Image, _currentPhoto.Strokes, dlg.FileName);
                    MessageBox.Show("保存成功！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
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
                SaveBitmapSourceToFile(originalImage, filePath);
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
            BitmapEncoder encoder = filePath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                ? new JpegBitmapEncoder()
                : new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(renderBitmap));
            using var stream = new FileStream(filePath, FileMode.Create);
            encoder.Save(stream);
        }

        private void ClearInk_Click(object sender, RoutedEventArgs e)
        {
            if (_isClosing) return;

            _drawingManager.ClearStrokes();
            // UpdateRedoButtonState(); // 已移除对 RedoBtn 的引用
        }

        private void UndoInk_Click(object sender, RoutedEventArgs e)
        {
            if (_isClosing) return;

            _drawingManager.Undo();
            // UpdateRedoButtonState(); // 已移除对 RedoBtn 的引用
        }

        private void RedoInk_Click(object sender, RoutedEventArgs e)
        {
            if (_isClosing) return;

            _drawingManager.Redo();
            // UpdateRedoButtonState(); // 已移除对 RedoBtn 的引用
        }

        // =========================
        // 已移除对 RedoBtn 的引用
        // =========================
        private void UpdateRedoButtonState()
        {
            // RedoBtn.IsEnabled = _drawingManager.CanRedo; // 注释掉或删除
            // 保留此方法的定义，以防未来需要
        }

        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        // =========================
        // 增强的退出功能
        // =========================
        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Console.WriteLine("开始退出流程...");

                // 设置关闭标志，阻止新操作
                _isClosing = true;

                // 先进行资源清理
                ReleaseCameraResources();

                // 停止所有定时器
                _touchUpdateTimer?.Stop();
                _touchUpdateTimer = null;

                // 关闭所有弹出窗口
                PenSettingsPopup.IsOpen = false;
                MoreMenuPopup.IsOpen = false;
                PhotoPopup.IsOpen = false;
                TouchInfoPopup.IsOpen = false;
                PhotoTipPopup.IsOpen = false;

                // 保存配置
                SaveConfig();

                // 延迟一点时间确保资源释放完成
                Task.Delay(200).ContinueWith(_ =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        Console.WriteLine("关闭主窗口...");
                        Close();
                    });
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"退出时发生错误: {ex.Message}");
                // 强制关闭
                Close();
            }
        }

        // =========================
        // ✅【关键修正】梯形校正功能 - 修复摄像头关联问题
        // =========================
        private void OpenPerspectiveCorrection_Click(object sender, RoutedEventArgs e)
        {
            if (_isClosing) return;

            // 检查摄像头可用性
            if (!_cameraAvailable)
            {
                MessageBox.Show("没有可用的摄像头，无法使用透视校正功能。", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // ✅ 正确传入三个参数：VideoService、AppConfig 和摄像头索引
            var wnd = new Views.RealTimePerspectiveCorrectionWindow(_videoService, config, currentCameraIndex);
            wnd.Owner = this;
            if (wnd.ShowDialog() == true && wnd.CorrectionPoints != null)
            {
                // 获取当前摄像头实际帧，用于尺寸参考
                using var currentFrame = _videoService.GetFrameCopy();
                if (currentFrame == null)
                {
                    MessageBox.Show("无法获取当前视频帧，无法应用透视校正。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                try
                {
                    // ✅ 关键：将校正窗口的点缩放到实际帧坐标系
                    double scaleX = (double)currentFrame.Width / wnd.SourceWidth;
                    double scaleY = (double)currentFrame.Height / wnd.SourceHeight;

                    var scaledPoints = new List<AForge.IntPoint>();
                    foreach (var p in wnd.CorrectionPoints)
                    {
                        scaledPoints.Add(new AForge.IntPoint(
                            (int)Math.Round(p.X * scaleX),
                            (int)Math.Round(p.Y * scaleY)
                        ));
                    }

                    // ✅ 使用实际帧尺寸创建滤镜
                    _perspectiveCorrectionFilter = new QuadrilateralTransformation(
                        scaledPoints,
                        currentFrame.Width,
                        currentFrame.Height);

                    // ✅ 保存当前摄像头的矫正信息
                    SaveCurrentCameraCorrection(
                        scaledPoints,
                        currentFrame.Width,
                        currentFrame.Height,
                        wnd.SourceWidth,
                        wnd.SourceHeight);

                    SaveConfig();
                    MessageBox.Show("透视校正已成功应用！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"应用透视校正失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    _perspectiveCorrectionFilter = null;
                }
            }
        }

        // =========================
        // 新增：摄像头矫正信息管理方法
        // =========================
        private CameraCorrectionConfig? GetCurrentCameraCorrection()
        {
            if (config?.CameraCorrections != null && config.CameraCorrections.ContainsKey(currentCameraIndex))
            {
                return config.CameraCorrections[currentCameraIndex];
            }
            return null;
        }

        private void SaveCurrentCameraCorrection(List<IntPoint> points, int sourceWidth, int sourceHeight, int originalWidth, int originalHeight)
        {
            if (config?.CameraCorrections == null)
                config.CameraCorrections = new Dictionary<int, CameraCorrectionConfig>();

            config.CameraCorrections[currentCameraIndex] = new CameraCorrectionConfig
            {
                CorrectionPoints = points,
                SourceWidth = sourceWidth,
                SourceHeight = sourceHeight,
                OriginalCameraWidth = originalWidth,
                OriginalCameraHeight = originalHeight
            };
        }

        private void ClearCorrection_Click(object sender, RoutedEventArgs e)
        {
            if (_isClosing) return;

            _perspectiveCorrectionFilter = null;

            // ✅ 清除当前摄像头的矫正信息
            if (config?.CameraCorrections != null && config.CameraCorrections.ContainsKey(currentCameraIndex))
            {
                config.CameraCorrections.Remove(currentCameraIndex);
            }

            SaveConfig();

            // 刷新当前画面
            if (_cameraAvailable)
            {
                var frame = _videoService.GetFrameCopy();
                if (frame != null)
                {
                    using var processed = ProcessFrame(frame, applyAdjustments: true);
                    VideoImage.Source = BitmapToBitmapImage(processed);
                    frame.Dispose();
                }
            }

            MessageBox.Show("透视校正已清除。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // =========================
        // 新增：为当前摄像头加载矫正信息
        // =========================
        private void LoadCorrectionForCurrentCamera()
        {
            var correction = GetCurrentCameraCorrection();
            if (correction != null && correction.CorrectionPoints != null && correction.CorrectionPoints.Count == 4)
            {
                try
                {
                    _perspectiveCorrectionFilter = new QuadrilateralTransformation(
                        correction.CorrectionPoints,
                        correction.SourceWidth,
                        correction.SourceHeight);

                    Console.WriteLine($"已加载摄像头 {currentCameraIndex} 的透视校正配置");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"加载摄像头 {currentCameraIndex} 的透视校正失败: {ex.Message}");
                    _perspectiveCorrectionFilter = null;
                }
            }
            else
            {
                _perspectiveCorrectionFilter = null;
                Console.WriteLine($"摄像头 {currentCameraIndex} 无透视校正配置");
            }
        }

        // 双击事件处理
        private void VideoArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_isClosing) return;

            var currentTime = DateTime.Now;
            var timeSinceLastClick = (currentTime - _lastClickTime).TotalMilliseconds;
            if (timeSinceLastClick <= DoubleClickDelay)
            {
                // 双击事件
                if (_drawingManager.CurrentMode == DrawingManager.ToolMode.Move)
                {
                    try
                    {
                        if (_cameraAvailable)
                        {
                            _videoService.AutoFocus();
                            MessageBox.Show("已触发自动对焦。", "对焦");
                        }
                        else
                        {
                            MessageBox.Show("没有可用的摄像头，无法进行自动对焦。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                    }
                    catch (Exception ex)
                    {
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

        // 从 Bitmap 构建 ZXing 的 BinaryBitmap 并解码
        private ZXing.Result? DecodeBarcodeFromBitmap(D.Bitmap src)
        {
            using var bmp24 = new D.Bitmap(src.Width, src.Height, D.Imaging.PixelFormat.Format24bppRgb);
            using (var g = D.Graphics.FromImage(bmp24))
            {
                g.DrawImage(src, 0, 0, bmp24.Width, bmp24.Height);
            }
            var rect = new D.Rectangle(0, 0, bmp24.Width, bmp24.Height);
            var data = bmp24.LockBits(rect, ImageLockMode.ReadOnly, D.Imaging.PixelFormat.Format24bppRgb);
            try
            {
                int stride = Math.Abs(data.Stride);
                int length = stride * bmp24.Height;
                byte[] buffer = new byte[length];
                Marshal.Copy(data.Scan0, buffer, 0, length);
                var luminance = new RGBLuminanceSource(buffer, bmp24.Width, bmp24.Height, RGBLuminanceSource.BitmapFormat.BGR24);
                var binary = new BinaryBitmap(new HybridBinarizer(luminance));
                var reader = new MultiFormatReader();
                var hints = new Dictionary<DecodeHintType, object>
                {
                    { DecodeHintType.TRY_HARDER, true },
                    { DecodeHintType.POSSIBLE_FORMATS, new[]
                        {
                            BarcodeFormat.QR_CODE, BarcodeFormat.DATA_MATRIX, BarcodeFormat.AZTEC,
                            BarcodeFormat.PDF_417, BarcodeFormat.CODE_128, BarcodeFormat.CODE_39,
                            BarcodeFormat.EAN_13, BarcodeFormat.EAN_8, BarcodeFormat.UPC_A
                        }
                    }
                };
                return reader.decode(binary, hints);
            }
            catch (ReaderException)
            {
                return null;
            }
            finally
            {
                bmp24.UnlockBits(data);
            }
        }

        // "扫一扫"点击事件
        private void ScanQRCode_Click(object sender, RoutedEventArgs e)
        {
            if (_isClosing) return;

            // 检查摄像头可用性
            if (!_cameraAvailable)
            {
                MessageBox.Show("没有可用的摄像头，无法使用扫码功能。", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var frame = _videoService.GetFrameCopy();
            if (frame == null) return;
            D.Bitmap? corrected = null;
            try
            {
                var target = frame;
                if (_perspectiveCorrectionFilter != null)
                {
                    corrected = _perspectiveCorrectionFilter.Apply(frame);
                    target = corrected;
                }
                var result = DecodeBarcodeFromBitmap(target);
                if (result != null)
                {
                    System.Windows.Clipboard.SetText(result.Text ?? string.Empty);
                    MessageBox.Show($"识别到：{result.BarcodeFormat}\n{result.Text}\n(已复制到剪贴板)", "扫一扫");
                }
                else
                {
                    MessageBox.Show("未检测到二维码/条码。", "扫一扫");
                }
            }
            finally
            {
                corrected?.Dispose();
                frame.Dispose();
            }
        }

        private void ScanDocument_Click(object sender, RoutedEventArgs e)
        {
            if (_isClosing) return;

            // 检查摄像头可用性
            if (!_cameraAvailable)
            {
                MessageBox.Show("没有可用的摄像头，无法使用文档扫描功能。", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var bmp = _videoService.GetFrameCopy();
            if (bmp == null) return;
            D.Bitmap? processed = null;
            try
            {
                processed = ProcessFrame(bmp, applyAdjustments: true);
                var gray = AForge.Imaging.Filters.Grayscale.CommonAlgorithms.BT709.Apply(processed);
                var threshold = new AForge.Imaging.Filters.BradleyLocalThresholding
                {
                    WindowSize = 41,
                    PixelBrightnessDifferenceLimit = 0.1f
                };
                threshold.ApplyInPlace(gray);
                var img = BitmapToBitmapImage(gray);
                var capturedImage = new CapturedImage(img);
                var photo = new PhotoWithStrokes(capturedImage);
                // 保存当前笔迹到文档扫描照片
                photo.Strokes = new StrokeCollection(_drawingManager.GetStrokes());
                _photos.Insert(0, photo);
                _currentPhoto = photo;
                ShowPhotoTip();
                // 触发内存清理
                TriggerMemoryCleanup();
            }
            finally
            {
                bmp.Dispose();
                processed?.Dispose();
            }
        }

        // =========================
        // 画面调节窗口
        // =========================
        private void OpenAdjustVideo_Click(object sender, RoutedEventArgs e)
        {
            if (_isClosing) return;

            // 检查摄像头可用性
            if (!_cameraAvailable)
            {
                MessageBox.Show("没有可用的摄像头，无法使用画面调节功能。", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

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
                _brightness = wnd.Brightness;
                _contrast = wnd.Contrast;
                _rotation = wnd.Rotation;
                _mirrorHorizontal = wnd.MirrorH;
                _mirrorVertical = wnd.MirrorV;
            }
        }

        // =========================
        // 摄像头切换 - 修复矫正信息关联
        // =========================
        private void SwitchCamera_Click(object sender, RoutedEventArgs e)
        {
            if (_isClosing) return;

            var cameras = _videoService.GetAvailableCameras();
            if (cameras.Count == 0)
            {
                MessageBox.Show("未找到可用摄像头。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                _cameraAvailable = false;
                ShowNoCameraBackground();
                return;
            }

            var dlg = new WinForms.Form
            {
                Text = "选择摄像头",
                Width = 400,
                Height = 200,
                StartPosition = WinForms.FormStartPosition.CenterParent
            };

            var combo = new WinForms.ComboBox { Dock = WinForms.DockStyle.Top, DropDownStyle = WinForms.ComboBoxStyle.DropDownList };
            combo.Items.AddRange(cameras.ToArray());
            combo.SelectedIndex = currentCameraIndex;
            var okBtn = new WinForms.Button { Text = "确定", Dock = WinForms.DockStyle.Bottom, DialogResult = WinForms.DialogResult.OK };
            dlg.Controls.Add(combo);
            dlg.Controls.Add(okBtn);

            if (dlg.ShowDialog() == WinForms.DialogResult.OK)
            {
                int newCameraIndex = combo.SelectedIndex;

                // 停止当前摄像头
                _videoService.Stop();

                // 切换前清除当前矫正滤镜
                _perspectiveCorrectionFilter = null;

                // 更新摄像头索引
                currentCameraIndex = newCameraIndex;

                // ✅ 尝试加载新摄像头的矫正信息
                LoadCorrectionForCurrentCamera();

                // 启动新摄像头
                if (!_videoService.Start(currentCameraIndex))
                {
                    MessageBox.Show("切换摄像头失败。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    _cameraAvailable = false;
                    ShowNoCameraBackground();
                }
                else
                {
                    _cameraAvailable = true;
                }
            }
        }

        // =========================
        // 配置保存/加载 - 修复摄像头矫正信息存储
        // =========================
        private void LoadConfig()
        {
            try
            {
                if (!File.Exists(configPath))
                {
                    config = new AppConfig();
                    return;
                }

                var json = File.ReadAllText(configPath, Encoding.UTF8);
                var cfg = JsonConvert.DeserializeObject<AppConfig>(json);
                if (cfg == null)
                {
                    config = new AppConfig();
                    return;
                }

                currentCameraIndex = cfg.CameraIndex;
                config = cfg;

                // ✅ 为当前摄像头加载矫正信息
                LoadCorrectionForCurrentCamera();
            }
            catch (Exception ex)
            {
                Console.WriteLine("加载配置失败: " + ex.Message);
                config = new AppConfig();
            }
        }

        private void SaveConfig()
        {
            try
            {
                var cfg = new AppConfig
                {
                    CameraIndex = currentCameraIndex,
                    StartMaximized = config.StartMaximized,
                    AutoStartCamera = config.AutoStartCamera,
                    DefaultPenWidth = _drawingManager.UserPenWidth,
                    DefaultPenColor = _drawingManager.PenColor.ToString(),
                    EnableHardwareAcceleration = config.EnableHardwareAcceleration,
                    EnableFrameProcessing = config.EnableFrameProcessing,
                    FrameRateLimit = config.FrameRateLimit,
                    CameraCorrections = config.CameraCorrections ?? new Dictionary<int, CameraCorrectionConfig>()
                };

                var json = JsonConvert.SerializeObject(cfg, Formatting.Indented);
                File.WriteAllText(configPath, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Console.WriteLine("保存配置失败: " + ex.Message);
            }
        }

        // =========================
        // 设置窗口功能
        // =========================
        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            if (_isClosing) return;

            var cameras = _videoService.GetAvailableCameras();
            var settingsWindow = new SettingsWindow(config, cameras)
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            if (settingsWindow.ShowDialog() == true)
            {
                WindowState = config.StartMaximized ? WindowState.Maximized : WindowState.Normal;
                _drawingManager.ApplyConfig(config);
                SaveConfig();
                if (currentCameraIndex != config.CameraIndex)
                {
                    currentCameraIndex = config.CameraIndex;
                    _videoService.Stop();

                    // ✅ 加载新摄像头的矫正信息
                    LoadCorrectionForCurrentCamera();

                    if (config.AutoStartCamera && _cameraAvailable && !_videoService.Start(currentCameraIndex))
                    {
                        MessageBox.Show("切换摄像头失败。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        _cameraAvailable = false;
                        ShowNoCameraBackground();
                    }
                }
            }
        }

        // =========================
        // 视频帧统一处理：校正 + 调节
        // =========================
        private D.Bitmap ProcessFrame(D.Bitmap src, bool applyAdjustments)
        {
            D.Bitmap work = src;
            try
            {
                // 1) 透视校正
                if (_perspectiveCorrectionFilter != null)
                {
                    var corrected = _perspectiveCorrectionFilter.Apply(work);
                    if (!ReferenceEquals(work, src)) work.Dispose();
                    work = corrected;
                }
                if (!applyAdjustments)
                    return work;
                // 2) 亮度/对比度
                if (Math.Abs(_brightness) > 0.01)
                {
                    var bc = new BrightnessCorrection((int)Math.Max(-100, Math.Min(100, _brightness)));
                    bc.ApplyInPlace(work);
                }
                if (Math.Abs(_contrast) > 0.01)
                {
                    var cc = new ContrastCorrection((int)Math.Max(-100, Math.Min(100, _contrast)));
                    cc.ApplyInPlace(work);
                }
                // 3) 旋转
                if (_rotation == 90) work.RotateFlip(D.RotateFlipType.Rotate90FlipNone);
                else if (_rotation == 180) work.RotateFlip(D.RotateFlipType.Rotate180FlipNone);
                else if (_rotation == 270) work.RotateFlip(D.RotateFlipType.Rotate270FlipNone);
                // 4) 镜像
                if (_mirrorHorizontal) work.RotateFlip(D.RotateFlipType.RotateNoneFlipX);
                if (_mirrorVertical) work.RotateFlip(D.RotateFlipType.RotateNoneFlipY);
                return work;
            }
            catch
            {
                if (!ReferenceEquals(work, src)) work.Dispose();
                return src;
            }
        }

        // =========================
        // 工具方法
        // =========================
        private BitmapImage BitmapToBitmapImage(D.Bitmap bitmap)
        {
            using var memory = new MemoryStream();
            bitmap.Save(memory, D.Imaging.ImageFormat.Bmp);
            memory.Position = 0;
            var bmpImage = new BitmapImage();
            bmpImage.BeginInit();
            bmpImage.CacheOption = BitmapCacheOption.OnLoad;
            bmpImage.StreamSource = memory;
            bmpImage.EndInit();
            bmpImage.Freeze();
            return bmpImage;
        }

        private void SaveBitmapSourceToFile(BitmapSource bitmap, string filePath)
        {
            BitmapEncoder encoder = filePath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                ? new JpegBitmapEncoder()
                : new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new FileStream(filePath, FileMode.Create);
            encoder.Save(stream);
        }

        // =========================
        // 事件处理程序
        // =========================
        // 手势操作事件处理
        private void VideoArea_ManipulationStarting(object sender, ManipulationStartingEventArgs e)
        {
            // 只在移动模式下启用手势操作
            if (_drawingManager.CurrentMode == DrawingManager.ToolMode.Move)
            {
                e.ManipulationContainer = this;
                e.Mode = ManipulationModes.Scale | ManipulationModes.Translate;
            }
            else
            {
                e.Mode = ManipulationModes.None;
            }
        }

        private void VideoArea_ManipulationDelta(object sender, ManipulationDeltaEventArgs e)
        {
            _drawingManager.HandleManipulationDelta(e);
        }

        // =========================
        // 增强的应用程序关闭逻辑
        // =========================
        protected override void OnClosed(EventArgs e)
        {
            try
            {
                Console.WriteLine("开始应用程序关闭流程...");

                // 设置关闭标志
                _isClosing = true;

                // 1. 停止触控更新定时器
                _touchUpdateTimer?.Stop();
                _touchUpdateTimer = null;

                // 2. 取消 TouchSDK 事件订阅
                if (_drawingManager != null)
                {
                    _drawingManager.OnSDKTouchAreaChanged -= OnSDKTouchAreaChanged;
                }

                // 3. 停止并释放摄像头资源
                ReleaseCameraResources();

                // 4. 强制停止视频服务
                _videoService?.Stop();

                // 5. 释放最后处理的帧
                if (_lastProcessedFrame != null)
                {
                    _lastProcessedFrame.Dispose();
                    _lastProcessedFrame = null;
                }

                // 6. 保存配置
                SaveConfig();

                // 7. 释放绘制管理器
                _drawingManager?.Dispose();

                // 8. 清除照片集合
                _photos.Clear();

                // 9. 强制垃圾回收
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                Console.WriteLine("应用程序关闭流程完成");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"关闭过程中发生错误: {ex.Message}");
            }
            finally
            {
                base.OnClosed(e);

                // 10. 强制终止应用程序（如果仍有线程运行）
                Console.WriteLine("强制终止应用程序进程");
                Environment.Exit(0);
            }
        }
    }
}