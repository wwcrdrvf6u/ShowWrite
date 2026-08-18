using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.PanAndZoom;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenCvSharp;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Path = System.IO.Path;
using Point = Avalonia.Point;
using Rect = Avalonia.Rect;
using PathShape = Avalonia.Controls.Shapes.Path; // 解决 Path 类名冲突

namespace ShowWrite
{
    public partial class MainWindow : Avalonia.Controls.Window, INotifyPropertyChanged
    {
        private readonly CameraService _cameraService;
        private List<IPluginWindow> _pluginWindows = new();
        private GlobalHotKey? _globalHotKey;

        private Control[] _loadingElements;
        private CancellationTokenSource? _loadingAnimationCts;
        private bool _isLoadingAnimationRunning;

        private int _videoWidth;
        private int _videoHeight;
        private WriteableBitmap? _attachedVideoBitmap;

        public static readonly double MinZoom = 0.1;
        public static readonly double MaxZoom = 5.0;
        public static readonly double ZoomStep = 0.1;

        private double _zoom => ZoomBorder?.ZoomX ?? 1.0;
        private Point _panOffset => new Point(ZoomBorder?.OffsetX ?? 0, ZoomBorder?.OffsetY ?? 0);
        private readonly Dictionary<int, Point> _activeTouchPoints = new();
        private bool _isNativePinchZoomActive;
        private double _nativePinchInitialDistance;
        private double _nativePinchInitialZoom = 1.0;
        private Point _nativePinchContentAnchor;

        public ObservableCollection<PhotoItem> Photos { get; } = new();

        private static readonly SKColor[] PenColors = new SKColor[]
        {
            SKColors.Red, SKColors.Orange, SKColors.Yellow, SKColors.Green, SKColors.Cyan,
            SKColors.Blue, SKColors.Purple, SKColors.Magenta, SKColors.Brown, SKColors.Gray,
            SKColors.Black, SKColors.White,
            new SKColor(255, 182, 193), new SKColor(144, 238, 144), new SKColor(173, 216, 230),
            new SKColor(255, 218, 185), new SKColor(221, 160, 221), new SKColor(240, 230, 140),
            new SKColor(255, 99, 71), new SKColor(50, 205, 50), new SKColor(30, 144, 255),
            new SKColor(255, 20, 147), new SKColor(255, 165, 0), new SKColor(0, 206, 209),
            new SKColor(138, 43, 226), new SKColor(255, 105, 180), new SKColor(0, 128, 0),
            new SKColor(0, 0, 139), new SKColor(139, 69, 19), new SKColor(105, 105, 105)
        };

        private static readonly int[] PenSizes = new int[] { 1, 2, 4, 8 };

        private int _currentPenSizeIndex = 1;
        private int _currentPenColorIndex = 0;

        private Popup? _clearSlidePopup;
        private PhotoItem? _selectedPhoto;
        private bool _isPhotoAnnotationMode = false;
        private bool _isSelectMode;
        private bool _includeInkAnnotations = true;

        private bool _isPhotoItemDragging = false;
        private Point _photoItemDragStartPoint;
        private const double PhotoItemDragThreshold = 5.0;
        private bool _isPhotoItemDragInProgress = false;

        public bool IsSelectMode
        {
            get => _isSelectMode;
            set
            {
                if (_isSelectMode != value)
                {
                    _isSelectMode = value;
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsSelectMode)));
                }
            }
        }

        public bool IncludeInkAnnotations
        {
            get => _includeInkAnnotations;
            set
            {
                if (_includeInkAnnotations != value)
                {
                    _includeInkAnnotations = value;
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IncludeInkAnnotations)));
                }
            }
        }

        public new event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        private StackPanel? _normalButtons;
        private StackPanel? _selectModeButtons;
        private Border? _leftButtonGroup;
        private Border? _keystoneButtonGroup;
        private Border? _centerButtonGroup;
        private Border? _rightButtonGroup;
        private Canvas? _keystoneOverlayCanvas;
        private Polygon? _keystonePolygon;
        private Border? _keystonePointTL;
        private Border? _keystonePointTR;
        private Border? _keystonePointBR;
        private Border? _keystonePointBL;

        private Border? _toolSliderBackground;

        private bool _isKeystoneCorrectionMode = false;
        private bool _isDraggingKeystonePoint = false;
        private Border? _draggingKeystonePoint = null;
        private Point _keystonePointOffset;

        private OpenCvSharp.Point2f[] _keystonePoints = new OpenCvSharp.Point2f[4];
        private OpenCvSharp.Point2f[] _originalKeystonePoints = new OpenCvSharp.Point2f[4];

        private ComboBox? _aspectRatioDropDown;
        private string _selectedAspectRatio = "自由";

        private bool _wasMinimized = false;

        private WhiteboardManager? _whiteboardManager;

        public ObservableCollection<WhiteboardPageThumbnail> WhiteboardPageThumbnails => _whiteboardManager?.WhiteboardPageThumbnails ?? new ObservableCollection<WhiteboardPageThumbnail>();

        // 橡皮光标相关
        private Canvas? _cursorCanvas;
        private Border? _eraserCursor;

        public MainWindow()
            : this(new CameraService())
        {
        }

        public MainWindow(CameraService cameraService)
        {
            InitializeComponent();
            this.WindowState = WindowState.FullScreen;
            this.KeyDown += MainWindow_KeyDown;

            _clearSlidePopup = this.FindControl<Popup>("ClearSlidePopup");
            _normalButtons = this.FindControl<StackPanel>("NormalButtons");
            _selectModeButtons = this.FindControl<StackPanel>("SelectModeButtons");
            _leftButtonGroup = this.FindControl<Border>("LeftButtonGroup");
            _keystoneButtonGroup = this.FindControl<Border>("KeystoneButtonGroup");
            _centerButtonGroup = this.FindControl<Border>("CenterButtonGroup");
            _rightButtonGroup = this.FindControl<Border>("RightButtonGroup");
            _keystoneOverlayCanvas = this.FindControl<Canvas>("KeystoneOverlayCanvas");
            _keystonePolygon = this.FindControl<Polygon>("KeystonePolygon");
            _keystonePointTL = this.FindControl<Border>("KeystonePointTL");
            _keystonePointTR = this.FindControl<Border>("KeystonePointTR");
            _keystonePointBR = this.FindControl<Border>("KeystonePointBR");
            _keystonePointBL = this.FindControl<Border>("KeystonePointBL");
            _toolSliderBackground = this.FindControl<Border>("ToolSliderBackground");
            _aspectRatioDropDown = this.FindControl<ComboBox>("AspectRatioDropDown");

            if (_aspectRatioDropDown != null)
            {
                _aspectRatioDropDown.SelectionChanged += AspectRatioDropDown_SelectionChanged;
            }

            var whiteboardBackground = this.FindControl<Border>("WhiteboardBackground");
            var whiteboardModeButtons = this.FindControl<StackPanel>("WhiteboardModeButtons");
            var whiteboardPageButtons = this.FindControl<StackPanel>("WhiteboardPageButtons");
            var pagePanel = this.FindControl<Border>("PagePanel");
            var pageInfoText = this.FindControl<TextBlock>("PageInfoText");
            var nextPagePath = this.FindControl<SvgIcon>("NextPagePath");
            var nextPageText = this.FindControl<TextBlock>("NextPageText");
            var pageImportingOverlay = this.FindControl<Border>("PageImportingOverlay");

            _whiteboardManager = new WhiteboardManager(InkCanvasOverlay, VideoImage, _cameraService);
            _whiteboardManager.Initialize(
                whiteboardBackground,
                whiteboardModeButtons,
                whiteboardPageButtons,
                pagePanel,
                pageInfoText,
                nextPagePath,
                nextPageText,
                pageImportingOverlay,
                ImageOverlayCanvas);
            _whiteboardManager.ImportPptRequested += OnImportPptRequested;
            _whiteboardManager.PptSlideControlReady += OnPptSlideControlReady;

            var moreButton = this.FindControl<Button>("MoreButton");
            if (moreButton != null && MoreMenuPopup != null)
            {
                MoreMenuPopup.PlacementTarget = moreButton;
            }

            DataContext = this;

            InitializeKeystonePoints();

            var includeInkCheckBox = this.FindControl<CheckBox>("IncludeInkCheckBox");
            if (includeInkCheckBox != null)
            {
                var isCheckedBinding = new Avalonia.Data.Binding(nameof(IncludeInkAnnotations))
                {
                    Source = this,
                    Mode = Avalonia.Data.BindingMode.TwoWay
                };
                var isVisibleBinding = new Avalonia.Data.Binding(nameof(IsSelectMode))
                {
                    Source = this,
                    Mode = Avalonia.Data.BindingMode.OneWay
                };
                includeInkCheckBox.Bind(CheckBox.IsCheckedProperty, isCheckedBinding);
                includeInkCheckBox.Bind(CheckBox.IsVisibleProperty, isVisibleBinding);
            }

            this.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(IsSelectMode))
                {
                    UpdateButtonVisibility();
                }
            };

            ThemeManager.ThemeChanged += OnThemeChanged;
            UpdateButtonGroupStyles();

            ((Avalonia.AvaloniaObject)this).PropertyChanged += (s, e) =>
            {
                if (e.Property == Avalonia.Controls.Window.WindowStateProperty)
                {
                    if (WindowState == WindowState.Minimized)
                    {
                        _wasMinimized = true;
                    }
                    else if (_wasMinimized)
                    {
                        _wasMinimized = false;
                        WindowState = WindowState.FullScreen;
                    }
                }
            };

            InkCanvasOverlay.SetVideoImage(VideoImage);

            _cameraService = cameraService;
            _cameraService.ErrorOccurred += OnCameraError;
            _cameraService.FrameReady += OnFrameReady;
            _cameraService.CameraStarted += OnCameraStarted;
            _cameraService.ScanComplete += OnScanComplete;
            _cameraService.UsingCachedCameras += OnUsingCachedCameras;

            ZoomBorder.ZoomChanged += OnZoomChanged;
            ZoomBorder.AddHandler(PointerPressedEvent, ZoomBorder_PointerPressed, RoutingStrategies.Tunnel, true);
            ZoomBorder.AddHandler(PointerMovedEvent, ZoomBorder_PointerMoved, RoutingStrategies.Tunnel, true);
            ZoomBorder.AddHandler(PointerReleasedEvent, ZoomBorder_PointerReleased, RoutingStrategies.Tunnel, true);

            PenBtn.AddHandler(PointerPressedEvent, PenBtn_PointerPressed, RoutingStrategies.Tunnel);
            EraserBtn.AddHandler(PointerPressedEvent, EraserBtn_PointerPressed, RoutingStrategies.Tunnel);

            InitializePenSettings();
            InitializeClearSlider();

            PluginManager.Instance.LoadPlugins();
            PluginDebugger.PrintPluginStatus();
            InitializePluginButtons();

            // 监听窗口大小变化
            this.SizeChanged += MainWindow_SizeChanged;

            // 创建橡皮光标覆盖层
            CreateEraserCursorOverlay();
            // 订阅橡皮光标事件
            InkCanvasOverlay.EraserCursorUpdate += UpdateEraserCursor;
        }

        private void MainWindow_SizeChanged(object? sender, SizeChangedEventArgs e)
        {
            // 当窗口大小变化时，确保PPT控件能正确缩放
            if (_whiteboardManager?.IsPptXmlMode == true)
            {
                var slideControl = _whiteboardManager.RenderPptXmlCurrentSlide();
                if (slideControl != null)
                {
                    OnPptSlideControlReady(slideControl);
                }
            }
        }

        private void OnZoomChanged(object? sender, ZoomChangedEventArgs e)
        {
            // 计算 ZoomBorder 相对于 InkCanvasOverlay 的位置偏移
            var zoomBorderPos = ZoomBorder.TranslatePoint(new Point(0, 0), InkCanvasOverlay);
            var zoomBorderOffset = zoomBorderPos ?? new Point(0, 0);

            InkCanvasOverlay.SetTransform(e.ZoomX, new Point(e.OffsetX, e.OffsetY), zoomBorderOffset);
            InkCanvasOverlay.InvalidateVisual();

            if (_isKeystoneCorrectionMode)
            {
                UpdateKeystonePointsDisplay();
                UpdateKeystonePolygon();
            }
        }

        private void ZoomBorder_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.Pointer.Type != PointerType.Touch)
            {
                return;
            }

            _activeTouchPoints[e.Pointer.Id] = e.GetPosition(ZoomBorder);

            if (_activeTouchPoints.Count == 2)
            {
                BeginNativePinchZoom();
                e.Handled = true;
            }
            else if (_isNativePinchZoomActive)
            {
                UpdateNativePinchZoom();
                e.Handled = true;
            }
        }

        private void ZoomBorder_PointerMoved(object? sender, PointerEventArgs e)
        {
            if (e.Pointer.Type != PointerType.Touch)
            {
                return;
            }

            if (!_activeTouchPoints.ContainsKey(e.Pointer.Id))
            {
                return;
            }

            _activeTouchPoints[e.Pointer.Id] = e.GetPosition(ZoomBorder);

            if (!_isNativePinchZoomActive && _activeTouchPoints.Count == 2)
            {
                BeginNativePinchZoom();
            }

            if (_isNativePinchZoomActive)
            {
                UpdateNativePinchZoom();
                e.Handled = true;
            }
        }

        private void ZoomBorder_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (e.Pointer.Type != PointerType.Touch)
            {
                return;
            }

            _activeTouchPoints.Remove(e.Pointer.Id);

            if (!_isNativePinchZoomActive)
            {
                return;
            }

            if (_activeTouchPoints.Count >= 2)
            {
                BeginNativePinchZoom();
            }
            else
            {
                EndNativePinchZoom();
            }

            e.Handled = true;
        }

        private void BeginNativePinchZoom()
        {
            if (!TryGetPinchPoints(out var firstPoint, out var secondPoint))
            {
                EndNativePinchZoom();
                return;
            }

            var distance = GetDistance(firstPoint, secondPoint);
            if (distance <= 0.001)
            {
                EndNativePinchZoom();
                return;
            }

            var currentZoom = ZoomBorder.ZoomX > 0 ? ZoomBorder.ZoomX : 1.0;
            var center = GetMidpoint(firstPoint, secondPoint);

            _isNativePinchZoomActive = true;
            _nativePinchInitialDistance = distance;
            _nativePinchInitialZoom = currentZoom;
            _nativePinchContentAnchor = new Point(
                (center.X - ZoomBorder.OffsetX) / currentZoom,
                (center.Y - ZoomBorder.OffsetY) / currentZoom);
        }

        private void UpdateNativePinchZoom()
        {
            if (!_isNativePinchZoomActive || !TryGetPinchPoints(out var firstPoint, out var secondPoint))
            {
                return;
            }

            var currentDistance = GetDistance(firstPoint, secondPoint);
            if (currentDistance <= 0.001 || _nativePinchInitialDistance <= 0.001)
            {
                return;
            }

            var minZoom = Math.Max(ZoomBorder.MinZoomX, ZoomBorder.MinZoomY);
            var maxZoom = Math.Min(ZoomBorder.MaxZoomX, ZoomBorder.MaxZoomY);
            if (maxZoom < minZoom)
            {
                maxZoom = minZoom;
            }

            var targetZoom = Math.Clamp(
                _nativePinchInitialZoom * (currentDistance / _nativePinchInitialDistance),
                minZoom,
                maxZoom);

            var center = GetMidpoint(firstPoint, secondPoint);
            var offsetX = center.X - _nativePinchContentAnchor.X * targetZoom;
            var offsetY = center.Y - _nativePinchContentAnchor.Y * targetZoom;
            var matrix = MatrixHelper.ScaleAndTranslate(targetZoom, targetZoom, offsetX, offsetY);
            ZoomBorder.SetMatrix(matrix, true);
        }

        private void EndNativePinchZoom()
        {
            _isNativePinchZoomActive = false;
            _nativePinchInitialDistance = 0;
        }

        private bool TryGetPinchPoints(out Point firstPoint, out Point secondPoint)
        {
            if (_activeTouchPoints.Count < 2)
            {
                firstPoint = default;
                secondPoint = default;
                return false;
            }

            using var enumerator = _activeTouchPoints.Values.GetEnumerator();
            enumerator.MoveNext();
            firstPoint = enumerator.Current;
            enumerator.MoveNext();
            secondPoint = enumerator.Current;
            return true;
        }

        private static Point GetMidpoint(Point firstPoint, Point secondPoint)
        {
            return new Point(
                (firstPoint.X + secondPoint.X) / 2.0,
                (firstPoint.Y + secondPoint.Y) / 2.0);
        }

        private static double GetDistance(Point firstPoint, Point secondPoint)
        {
            var dx = secondPoint.X - firstPoint.X;
            var dy = secondPoint.Y - firstPoint.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private void CreateEraserCursorOverlay()
        {
            _cursorCanvas = new Canvas
            {
                IsHitTestVisible = false,
                Background = Brushes.Transparent,
            };

            // 将覆盖层添加到 MainGrid 中（ZoomBorder 外部）
            var root = this.Content as Panel;
            if (root != null)
            {
                root.Children.Add(_cursorCanvas);
                _cursorCanvas.SetValue(Canvas.ZIndexProperty, 300);
            }

            VideoAreaContainer.LayoutUpdated += (s, e) => UpdateCursorCanvasLayout();
            UpdateCursorCanvasLayout();

            _eraserCursor = new Border
            {
                Width = 400,  // 临时宽度，实际大小会在更新时调整
                Height = 500, // 临时高度，实际大小会在更新时调整
                Background = new SolidColorBrush(Color.FromArgb(200, 240, 240, 240)), // 浅白色半透
                BorderThickness = new Thickness(0), // 无边框
                CornerRadius = new CornerRadius(4),
                IsHitTestVisible = false,
                Child = new Viewbox
                {
                    Child = new PathShape
                    {
                        Data = Geometry.Parse("M10,2 L22,2 L22,6 L10,6 Z M8,4 L4,8 L4,12 L8,16 L12,16 L16,12 L16,8 L12,4 Z M10,6 L12,6 L12,10 L10,10 Z M14,6 L16,6 L16,8 L14,8 Z"),
                        Fill = new SolidColorBrush(Colors.White),
                        Stretch = Stretch.Uniform,
                        Margin = new Thickness(4),
                    }
                }
            };
            _cursorCanvas.Children.Add(_eraserCursor);
            _eraserCursor.IsVisible = false;
        }

        private void UpdateCursorCanvasLayout()
        {
            if (_cursorCanvas == null || VideoAreaContainer == null) return;

            // 获取 VideoAreaContainer 相对于覆盖层父容器的位置
            var parent = _cursorCanvas.Parent as Visual;
            if (parent == null) return;

            var containerBounds = VideoAreaContainer.Bounds;
            var containerPos = VideoAreaContainer.TranslatePoint(new Point(0, 0), parent);
            if (containerPos == null) return;

            // 设置覆盖层大小和位置
            _cursorCanvas.Width = containerBounds.Width;
            _cursorCanvas.Height = containerBounds.Height;
            // 使用 SetValue 设置附加属性，避免静态方法调用冲突
            _cursorCanvas.SetValue(Canvas.LeftProperty, containerPos.Value.X);
            _cursorCanvas.SetValue(Canvas.TopProperty, containerPos.Value.Y);
        }

        private void UpdateEraserCursor(Point position, float size, bool visible)
        {
            if (_eraserCursor == null || _cursorCanvas == null) return;

            _eraserCursor.IsVisible = visible;
            if (!visible) return;

            // InkCanvas 回传的是自身坐标，需要转换到光标覆盖层坐标系
            var mappedPosition = InkCanvasOverlay.TranslatePoint(position, _cursorCanvas);
            if (mappedPosition == null) return;
            var cursorPos = mappedPosition.Value;

            // 竖长方形：宽度为 size * 0.6，高度为 size * 1.0
            double width = size * 1.6;
            double height = size * 2.0;
            _eraserCursor.Width = width;
            _eraserCursor.Height = height;

            double left = cursorPos.X - width / 2;
            double top = cursorPos.Y - height / 2;

            _eraserCursor.SetValue(Canvas.LeftProperty, left);
            _eraserCursor.SetValue(Canvas.TopProperty, top);
        }

        protected override void OnSizeChanged(SizeChangedEventArgs e)
        {
            base.OnSizeChanged(e);
            UpdateCursorCanvasLayout();

            // 窗口大小变化时更新 InkCanvas 的坐标偏移
            var zoomBorderPos = ZoomBorder.TranslatePoint(new Point(0, 0), InkCanvasOverlay);
            var zoomBorderOffset = zoomBorderPos ?? new Point(0, 0);
            InkCanvasOverlay.SetTransform(ZoomBorder.ZoomX, new Point(ZoomBorder.OffsetX, ZoomBorder.OffsetY), zoomBorderOffset);

            if (_isPhotoAnnotationMode)
            {
                QueueFitPhotoToViewportHeight();
            }
        }

        private void UpdateButtonVisibility()
        {
            var normalButtons = this.FindControl<StackPanel>("NormalButtons");
            var selectModeButtons = this.FindControl<StackPanel>("SelectModeButtons");

            if (normalButtons != null)
                normalButtons.IsVisible = !IsSelectMode;
            if (selectModeButtons != null)
                selectModeButtons.IsVisible = IsSelectMode;
        }

        private void PenBtn_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            _penWasCheckedBeforeClick = PenBtn.IsChecked ?? false;
        }

        private void EraserBtn_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            _eraserWasCheckedBeforeClick = EraserBtn.IsChecked ?? false;
        }

        private void InitializePenSettings()
        {
            for (int i = 0; i < PenColors.Length && i < 30; i++)
            {
                var color = PenColors[i];
                var row = i / 5;
                var col = i % 5;

                var colorBtn = new Border
                {
                    Width = 32,
                    Height = 32,
                    CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush(Color.FromArgb(color.Alpha, color.Red, color.Green, color.Blue)),
                    Margin = new Thickness(2),
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Tag = i
                };

                colorBtn.PointerPressed += ColorBtn_PointerPressed;

                Grid.SetRow(colorBtn, row);
                Grid.SetColumn(colorBtn, col);
                ColorGrid.Children.Add(colorBtn);
            }

            for (int i = 0; i < PenSizes.Length; i++)
            {
                var size = PenSizes[i];
                var sizeBtn = new Border
                {
                    Width = 40,
                    Height = 40,
                    CornerRadius = new CornerRadius(6),
                    Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
                    Margin = new Thickness(2),
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Tag = i
                };

                var circle = new Ellipse
                {
                    Width = Math.Min(size + 6, 28),
                    Height = Math.Min(size + 6, 28),
                    Fill = Brushes.White,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                };

                sizeBtn.Child = circle;
                sizeBtn.PointerPressed += SizeBtn_PointerPressed;

                SizePanel.Children.Add(sizeBtn);
            }

            UpdatePenSettingsSelection();

            LoadPenSettingsToInkCanvas();
        }

        private void LoadPenSettingsToInkCanvas()
        {
            var config = Config.Load();
            var settings = config.PenSettings ?? new PenSettings();

            InkCanvasOverlay.PenSettings = settings;
            InkCanvasOverlay.PalmEraserThreshold = settings.PalmEraserThreshold;
            InkCanvasOverlay.EnablePalmEraser = settings.EnablePalmEraser;
        }

        private void ColorBtn_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is Border border && border.Tag is int index)
            {
                _currentPenColorIndex = index;
                var color = PenColors[index];
                InkCanvasOverlay.SetPenColor(color);
                UpdatePenSettingsSelection();
            }
        }

        private void SizeBtn_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is Border border && border.Tag is int index)
            {
                _currentPenSizeIndex = index;
                var size = PenSizes[index];
                InkCanvasOverlay.SetPenSize(size);
                UpdatePenSettingsSelection();
            }
        }

        private void UpdatePenSettingsSelection()
        {
            for (int i = 0; i < ColorGrid.Children.Count; i++)
            {
                if (ColorGrid.Children[i] is Border border)
                {
                    border.BorderThickness = i == _currentPenColorIndex ? new Thickness(3) : new Thickness(0);
                    border.BorderBrush = i == _currentPenColorIndex ? Brushes.White : null;
                }
            }

            for (int i = 0; i < SizePanel.Children.Count; i++)
            {
                if (SizePanel.Children[i] is Border border)
                {
                    border.BorderThickness = i == _currentPenSizeIndex ? new Thickness(2) : new Thickness(0);
                    border.BorderBrush = i == _currentPenSizeIndex ? Brushes.White : null;
                }
            }
        }

        private bool _isSliderDragging = false;
        private double _sliderStartX = 0;
        private double _sliderStartOffset = 0;
        private Border? _slideTrack;
        private Border? _slideThumb;

        private void InitializeClearSlider()
        {
            _slideTrack = this.FindControl<Border>("SlideTrack");
            _slideThumb = this.FindControl<Border>("SlideThumb");

            if (_slideThumb != null)
            {
                _slideThumb.PointerPressed += SlideThumb_PointerPressed;
                _slideThumb.PointerMoved += SlideThumb_PointerMoved;
                _slideThumb.PointerReleased += SlideThumb_PointerReleased;
            }
        }

        private void InitializePluginButtons()
        {
            var normalBottomButtons = this.FindControl<StackPanel>("NormalBottomButtons");
            if (normalBottomButtons == null)
            {

                return;
            }

            

            var pluginOverlay = this.FindControl<Border>("PluginOverlay");

            foreach (var plugin in PluginManager.Instance.Plugins.Where(p => p.IsEnabled && p.IsLoaded))
            {
                if (plugin.PluginInstance is IBottomToolbarPlugin bottomToolbarPlugin)
                {
                    try
                    {
                        bottomToolbarPlugin.SetRefreshToolbarCallback(RefreshPluginButtons);
                    }
                    catch (Exception ex)
                    {

                    }
                }

                if (plugin.PluginInstance is IPluginWindow pluginWindow)
                {
                    try
                    {
                        if (pluginOverlay != null)
                        {
                            pluginWindow.SetPluginOverlay(pluginOverlay);
                        }
                        pluginWindow.SetToolbarVisibilityCallback(SetToolbarVisibility);
                        pluginWindow.SetShowResultWindowCallback(ShowPluginResultWindow);
                        pluginWindow.SetCancelCallback(() =>
                        {
                            pluginWindow.OnPluginDeactivated();
                        });
                        pluginWindow.SetShowNotificationCallback(ShowNotification);
                        _pluginWindows.Add(pluginWindow);
                    }
                    catch (Exception ex)
                    {

                    }
                }
            }

            var pluginButtons = PluginManager.Instance.GetBottomToolbarButtons();


            foreach (var pluginButton in pluginButtons)
            {
                try
                {


                    var button = new Button
                    {
                        Width = 56,
                        Height = 56,
                        IsEnabled = pluginButton.IsEnabled,
                        Tag = "plugin"
                    };
                    button.Classes.Add("tool-btn-square");

                    var stackPanel = new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Vertical,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
                    };

                    var viewbox = new Viewbox
                    {
                        Width = 28,
                        Height = 28
                    };

                    var path = new Avalonia.Controls.Shapes.Path
                    {
                        Fill = new SolidColorBrush(Colors.White),
                        Data = Geometry.Parse(pluginButton.IconPath)
                    };

                    viewbox.Child = path;

                    var textBlock = new TextBlock
                    {
                        Text = pluginButton.Label,
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Color.FromRgb(204, 204, 204)),
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        Margin = new Thickness(0, 4, 0, 0)
                    };

                    stackPanel.Children.Add(viewbox);
                    stackPanel.Children.Add(textBlock);

                    button.Content = stackPanel;

                    if (pluginButton.OnClick != null)
                    {
                        button.Click += (s, e) => pluginButton.OnClick();
                    }

                    normalBottomButtons.Children.Add(button);

                }
                catch (Exception ex)
                {
                    
                    
                }
            }


        }

        public void SetToolbarVisibility(bool visible)
        {
            var bottomToolbar = this.FindControl<Border>("BottomToolbar");
            var cancelButton = this.FindControl<Button>("PluginCancelButton");

            if (bottomToolbar != null)
                bottomToolbar.IsVisible = visible;
            if (cancelButton != null)
                cancelButton.IsVisible = !visible;
        }

        public void ShowPluginResultWindow(string result, bool isUrl, List<(string Text, Action Callback)> buttons)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                var window = new PluginResultWindow();
                window.SetResult(result, isUrl);
                window.ClearButtons();

                foreach (var (text, callback) in buttons)
                {
                    window.AddButton(text, callback);
                }

                window.ShowDialog(this);
            });
        }

        private Action? _pluginCancelCallback = null;

        public void SetPluginCancelCallback(Action callback)
        {
            _pluginCancelCallback = callback;
        }

        private void PluginCancelButton_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            _pluginCancelCallback?.Invoke();
            SetToolbarVisibility(true);
        }

        public void ShowNotification(string message, int durationMs = 3000)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                var notificationWindow = new NotificationWindow();
                notificationWindow.ShowNotification(message, durationMs);
            });
        }

        public void RefreshPluginButtons()
        {
            var normalBottomButtons = this.FindControl<StackPanel>("NormalBottomButtons");
            if (normalBottomButtons == null)
            {

                return;
            }



            var existingPluginButtons = normalBottomButtons.Children
                .OfType<Button>()
                .Where(b => b.Tag as string == "plugin")
                .ToList();

            foreach (var btn in existingPluginButtons)
            {
                normalBottomButtons.Children.Remove(btn);
            }

            var pluginButtons = PluginManager.Instance.GetBottomToolbarButtons();


            foreach (var pluginButton in pluginButtons)
            {
                try
                {


                    var button = new Button
                    {
                        Width = 56,
                        Height = 56,
                        IsEnabled = pluginButton.IsEnabled,
                        Tag = "plugin"
                    };
                    button.Classes.Add("tool-btn-square");

                    var stackPanel = new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Vertical,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
                    };

                    var viewbox = new Viewbox
                    {
                        Width = 28,
                        Height = 28
                    };

                    var path = new Avalonia.Controls.Shapes.Path
                    {
                        Fill = new SolidColorBrush(Colors.White),
                        Data = Geometry.Parse(pluginButton.IconPath)
                    };

                    viewbox.Child = path;

                    var textBlock = new TextBlock
                    {
                        Text = pluginButton.Label,
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Color.FromRgb(204, 204, 204)),
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        Margin = new Thickness(0, 4, 0, 0)
                    };

                    stackPanel.Children.Add(viewbox);
                    stackPanel.Children.Add(textBlock);

                    button.Content = stackPanel;

                    if (pluginButton.OnClick != null)
                    {
                        button.Click += (s, e) => pluginButton.OnClick();
                    }

                    normalBottomButtons.Children.Add(button);

                }
                catch (Exception ex)
                {
                    
                    
                }
            }


        }

        private void InitializeLoadingElements()
        {
            _loadingElements = new Control[]
            {
                this.FindControl<Canvas>("ElemPyro"),
                this.FindControl<Canvas>("ElemHydro"),
                this.FindControl<Canvas>("ElemAnemo"),
                this.FindControl<Canvas>("ElemElectro"),
                this.FindControl<Canvas>("ElemDendro"),
                this.FindControl<Canvas>("ElemCryo"),
                this.FindControl<Canvas>("ElemGeo")
            };

            foreach (var elem in _loadingElements)
            {
                if (elem != null)
                {
                    elem.Opacity = 1.0;
                    elem.Clip = new RectangleGeometry(new Rect(0, 0, 0, 50));
                }
            }
        }

        private void StartLoadingAnimation()
        {
            if (_isLoadingAnimationRunning) return;

            _isLoadingAnimationRunning = true;
            _loadingAnimationCts = new CancellationTokenSource();

            Task.Run(() => RunLoadingAnimationAsync(_loadingAnimationCts.Token));
        }

        private void StopLoadingAnimation()
        {
            _isLoadingAnimationRunning = false;
            _loadingAnimationCts?.Cancel();
            _loadingAnimationCts?.Dispose();
            _loadingAnimationCts = null;
        }

        private void CompleteLoadingAnimation()
        {
            if (!_isLoadingAnimationRunning || _loadingElements == null) return;

            Dispatcher.UIThread.InvokeAsync(() =>
            {
                double iconWidth = 50;
                foreach (var elem in _loadingElements)
                {
                    if (elem != null)
                    {
                        elem.Clip = new RectangleGeometry(new Rect(0, 0, iconWidth, 50));
                    }
                }
            });

            Task.Delay(300).ContinueWith(_ =>
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var loadingContainer = this.FindControl<StackPanel>("LoadingContainer");
                    if (loadingContainer != null)
                    {
                        loadingContainer.IsVisible = false;
                    }
                });
            });

            StopLoadingAnimation();
        }

        private async Task RunLoadingAnimationAsync(CancellationToken token)
        {
            if (_loadingElements == null || _loadingElements.Length == 0) return;

            double iconWidth = 50;
            double spacing = 20;
            double totalWidth = iconWidth * _loadingElements.Length + spacing * (_loadingElements.Length - 1);
            double targetWidth = totalWidth - iconWidth * 0.7;
            double animationDuration = 2000;

            var startTime = DateTime.UtcNow;
            var duration = TimeSpan.FromMilliseconds(animationDuration);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var elem in _loadingElements)
                {
                    if (elem != null)
                    {
                        elem.Opacity = 1.0;
                        elem.Clip = new RectangleGeometry(new Rect(0, 0, 0, 50));
                    }
                }
            });

            while (true)
            {
                if (token.IsCancellationRequested) break;

                var elapsed = DateTime.UtcNow - startTime;
                var rawProgress = Math.Min(elapsed.TotalMilliseconds / duration.TotalMilliseconds, 1.0);
                var progress = EaseOutCubic(rawProgress);
                var currentWidth = targetWidth * progress;

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    double currentX = 0;

                    for (int i = 0; i < _loadingElements.Length; i++)
                    {
                        var elem = _loadingElements[i];
                        if (elem == null) continue;

                        double elemStartX = currentX;
                        double elemEndX = currentX + iconWidth;

                        if (currentWidth >= elemEndX)
                        {
                            elem.Clip = new RectangleGeometry(new Rect(0, 0, iconWidth, 50));
                        }
                        else if (currentWidth > elemStartX)
                        {
                            double fillWidth = currentWidth - elemStartX;
                            elem.Clip = new RectangleGeometry(new Rect(0, 0, fillWidth, 50));
                        }
                        else
                        {
                            elem.Clip = new RectangleGeometry(new Rect(0, 0, 0, 50));
                        }

                        currentX += iconWidth + spacing;
                    }
                });

                if (rawProgress >= 1.0) break;

                await Task.Delay(16, token);
            }
        }

        private double EaseOutCubic(double t)
        {
            return 1 - Math.Pow(1 - t, 3);
        }

        private void SlideThumb_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (_slideTrack == null || _slideThumb == null) return;

            _isSliderDragging = true;
            _sliderStartX = e.GetPosition(_slideTrack).X;
            _sliderStartOffset = _slideThumb.Margin.Left;
            _slideThumb.Background = new SolidColorBrush(Color.FromRgb(255, 100, 100));
            e.Pointer.Capture(_slideThumb);
            e.Handled = true;
        }

        private void SlideThumb_PointerMoved(object? sender, PointerEventArgs e)
        {
            if (!_isSliderDragging || _slideTrack == null || _slideThumb == null) return;

            var currentX = e.GetPosition(_slideTrack).X;
            var trackWidth = _slideTrack.Bounds.Width;
            var thumbWidth = _slideThumb.Width;

            var maxOffset = trackWidth - thumbWidth;
            var deltaX = currentX - _sliderStartX;
            var newOffset = Math.Clamp(_sliderStartOffset + deltaX, 0, maxOffset);

            _slideThumb.Margin = new Thickness(newOffset, 0, 0, 0);

            var progress = newOffset / maxOffset;
            if (progress > 0.8)
            {
                _slideThumb.Background = new SolidColorBrush(Color.FromRgb(76, 175, 80));
            }
            else
            {
                _slideThumb.Background = new SolidColorBrush(Color.FromRgb(255, 100, 100));
            }
        }

        private void SlideThumb_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (!_isSliderDragging || _slideTrack == null || _slideThumb == null) return;

            _isSliderDragging = false;
            e.Pointer.Capture(null);

            var currentOffset = _slideThumb.Margin.Left;
            var trackWidth = _slideTrack.Bounds.Width;
            var thumbWidth = _slideThumb.Width;
            var maxOffset = trackWidth - thumbWidth;
            var progress = currentOffset / maxOffset;

            _slideThumb.Margin = new Thickness(0, 0, 0, 0);
            _slideThumb.Background = new SolidColorBrush(Color.FromRgb(255, 107, 107));

            if (progress > 0.8)
            {
                InkCanvasOverlay.ClearAll();
                if (_clearSlidePopup != null)
                {
                    _clearSlidePopup.IsOpen = false;
                }
            }
        }

        #region Window 生命周期

        private void CloseLoadingWindow()
        {
            if (_isLoadingAnimationRunning)
            {
                CompleteLoadingAnimation();
            }
            else
            {
                StopLoadingAnimation();
                var loadingContainer = this.FindControl<StackPanel>("LoadingContainer");
                if (loadingContainer != null)
                {
                    loadingContainer.IsVisible = false;
                }
            }
        }

        private void Window_Opened(object? sender, EventArgs e)
        {
            this.WindowState = WindowState.FullScreen;

            InitializeLoadingElements();

            _ = InitializeLicenseAsync();

            // 启动图阶段已连接摄像头时直接触发已启动逻辑；否则正常发起连接
            if (_cameraService.IsConnected)
            {
                OnCameraStarted();
            }
            else
            {
                _cameraService.DetectAndConnectCamera();
            }

            InitializeGlobalHotKey();
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
                    if (settings.Enabled && !string.IsNullOrEmpty(settings.ShortcutKey))
                    {
                        if (_globalHotKey.Register(settings.ShortcutKey))
                        {
                            _globalHotKey.HotKeyPressed += OnGlobalHotKeyPressed;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainWindow] 全局快捷键初始化失败: {ex.Message}");
            }
        }

        private void OnGlobalHotKeyPressed()
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                var settings = Config.Load().RandomNote;
                RandomNoteSettingsWindow.TryTriggerShortcut(settings.ShortcutKey, _cameraService, ShowNotificationAction);
            });
        }

        private IntPtr TryGetWindowHandle()
        {
            var platformHandle = this.TryGetPlatformHandle();
            return platformHandle?.Handle ?? IntPtr.Zero;
        }

        private async Task InitializeLicenseAsync()
        {
            try
            {
                var uuid = await LicenseManager.Instance.GetOrCreateLicenseAsync();
                System.Diagnostics.Debug.WriteLine($"[MainWindow] 许可证初始化成功，UUID: {uuid}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainWindow] 许可证初始化失败: {ex.Message}");
                StatusText.Inlines?.Clear();
                StatusText.Inlines?.Add(new Run($"许可证验证失败: {ex.Message}"));
                StatusText.IsVisible = true;
            }
        }

        private void Window_Closing(object? sender, WindowClosingEventArgs e)
        {
            StopLoadingAnimation();
            _cameraService.StopCapture();
            _cameraService.Dispose();

            // 清理PowerPoint资源
            _whiteboardManager?.ClosePowerPointPresentation();
            _whiteboardManager?.Dispose();

            InkCanvasOverlay.Dispose();

            // 取消事件订阅
            InkCanvasOverlay.EraserCursorUpdate -= UpdateEraserCursor;

            // 清理全局快捷键
            _globalHotKey?.Dispose();

            // 如果不是独立闪记模式，关闭时同时关闭闪记
            if (!Program.RandomNoteMode)
            {
                if (App.RandomNoteTrayIcon != null)
                {
                    App.RandomNoteTrayIcon.IsVisible = false;
                }
                RandomNoteSettingsWindow.StopRecording();
            }
        }

        #endregion

        #region 摄像头事件处理

        private void OnCameraError(string message)
        {
            StatusText.Inlines?.Clear();
            StatusText.Inlines?.Add(new Run(message));
            StatusText.IsVisible = true;
        }

        private void OnUsingCachedCameras()
        {
            if (_cameraService.IsConnected) return;
            StatusText.Inlines?.Clear();
            StatusText.Inlines?.Add(new Run("正在连接摄像头..."));
            StatusText.IsVisible = true;
            var loadingPanel = this.FindControl<StackPanel>("LoadingElementsPanel");
            if (loadingPanel != null)
            {
                loadingPanel.IsVisible = true;
            }
            StartLoadingAnimation();
        }

        private void OnScanComplete()
        {
            StatusText.IsVisible = false;
        }

        private void OnFrameReady()
        {
            PresentCameraFrame(centerOnSizeChange: false);

            foreach (var pluginWindow in _pluginWindows)
            {
                try
                {
                    pluginWindow.OnCameraFrame(_cameraService.FrameBitmap);
                }
                catch (Exception ex)
                {

                }
            }
        }

        private void OnCameraStarted()
        {
            if (_isPhotoAnnotationMode) return;

            CloseLoadingWindow();

            // 确保视频画面可见
            VideoImage.IsVisible = true;

            PresentCameraFrame(centerOnSizeChange: true);
            LoadKeystoneSettings();

            // 更新光标覆盖层布局
            UpdateCursorCanvasLayout();
        }

        private void PresentCameraFrame(bool centerOnSizeChange)
        {
            var frameBitmap = _cameraService.FrameBitmap;
            if (frameBitmap == null)
            {

                return;
            }

            if (!ReferenceEquals(_attachedVideoBitmap, frameBitmap))
            {
                _attachedVideoBitmap = frameBitmap;
                VideoImage.Source = frameBitmap;
            }

            var frame = _cameraService.LatestFrame;
            bool sizeChanged = false;

            if (frame != null && !frame.Empty())
            {
                var contentSizeMismatch =
                    Math.Abs(VideoAreaContainer.Width - frame.Width) > 0.5 ||
                    Math.Abs(VideoAreaContainer.Height - frame.Height) > 0.5;

                if (_videoWidth != frame.Width || _videoHeight != frame.Height)
                {
                    _videoWidth = frame.Width;
                    _videoHeight = frame.Height;
                    sizeChanged = true;
                }

                if (sizeChanged || contentSizeMismatch)
                {
                    // 从照片模式切回直播时，分辨率可能没变，但容器仍停留在照片尺寸。
                    SetVideoContentSize(frame.Width, frame.Height);
                }

                InkCanvasOverlay.SetVideoFrame(frame);
            }

            if (centerOnSizeChange || sizeChanged)
            {
                QueueCenterVideo();
            }

            VideoImage.InvalidateVisual();
        }

        private void QueueCenterVideo()
        {
            Dispatcher.UIThread.Post(CenterVideo, DispatcherPriority.Loaded);
        }

        private void CenterVideo()
        {
            if (_videoWidth <= 0 || _videoHeight <= 0)
            {
                var frameBitmap = _cameraService.FrameBitmap;
                if (frameBitmap != null)
                {
                    _videoWidth = (int)frameBitmap.Size.Width;
                    _videoHeight = (int)frameBitmap.Size.Height;
                    SetVideoContentSize(_videoWidth, _videoHeight);
                }
                else
                {
                    return;
                }
            }

            var viewportWidth = ZoomBorder.Bounds.Width;
            var viewportHeight = ZoomBorder.Bounds.Height;
            if (viewportWidth <= 0 || viewportHeight <= 0)
            {
                // 布局尚未稳定时重试，避免高分辨率首帧无法正确居中
                QueueCenterVideo();
                return;
            }

            var contentWidth = VideoAreaContainer.Width;
            var contentHeight = VideoAreaContainer.Height;
            if (contentWidth <= 0 || contentHeight <= 0)
            {
                contentWidth = _videoWidth;
                contentHeight = _videoHeight;
            }

            double fitZoom;
            double offsetX;
            double offsetY;

            // 0/180 度：按高度贴合上下边缘；90/270 度：按宽度贴合左右边缘。
            if (_currentVideoRotation == 90 || _currentVideoRotation == 270)
            {
                fitZoom = viewportWidth / contentWidth;
                offsetX = 0;
                offsetY = (viewportHeight - contentHeight * fitZoom) / 2.0;
            }
            else
            {
                fitZoom = viewportHeight / contentHeight;
                offsetX = (viewportWidth - contentWidth * fitZoom) / 2.0;
                offsetY = 0;
            }

            // 允许稍微缩小于归位比例，便于用户后续手势微调
            var minZoom = Math.Max(0.001, fitZoom * 0.8);
            if (ZoomBorder.MinZoomX > minZoom) ZoomBorder.MinZoomX = minZoom;
            if (ZoomBorder.MinZoomY > minZoom) ZoomBorder.MinZoomY = minZoom;

            var matrix = MatrixHelper.ScaleAndTranslate(fitZoom, fitZoom, offsetX, offsetY);
            ZoomBorder.SetMatrix(matrix, true);
        }

        private void SetVideoContentSize(double width, double height)
        {
            if (width <= 0 || height <= 0) return;

            VideoAreaContainer.Width = width;
            VideoAreaContainer.Height = height;
            VideoImage.Width = width;
            VideoImage.Height = height;
        }

        private void QueueFitPhotoToViewportHeight()
        {
            Dispatcher.UIThread.Post(FitPhotoToViewportHeight, DispatcherPriority.Loaded);
        }

        private void FitPhotoToViewportHeight()
        {
            if (!_isPhotoAnnotationMode) return;

            var contentWidth = VideoAreaContainer.Width;
            var contentHeight = VideoAreaContainer.Height;
            var viewportWidth = ZoomBorder.Bounds.Width;
            var viewportHeight = ZoomBorder.Bounds.Height;

            if (contentWidth <= 0 || contentHeight <= 0 || viewportWidth <= 0 || viewportHeight <= 0)
            {
                return;
            }

            var zoom = viewportHeight / contentHeight;
            var offsetX = (viewportWidth - contentWidth * zoom) / 2.0;

            var matrix = MatrixHelper.ScaleAndTranslate(zoom, zoom, offsetX, 0);
            ZoomBorder.SetMatrix(matrix, true);
        }

        #endregion

        #region 所有按钮事件

        private void MoreButton_Click(object? sender, RoutedEventArgs e)
        {
            MoreMenuPopup.IsOpen = !MoreMenuPopup.IsOpen;
        }

        private void Minimize_Click(object? sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private enum ToolMode { Move, Pen, Eraser }

        private async Task ApplyToolMode(ToolMode mode)
        {
            PenSettingsPopup.IsOpen = false;
            if (_clearSlidePopup != null) _clearSlidePopup.IsOpen = false;

            switch (mode)
            {
                case ToolMode.Move:
                    MoveBtn.IsChecked = true;
                    PenBtn.IsChecked = false;
                    EraserBtn.IsChecked = false;
                    InkCanvasOverlay.SetMoveMode();
                    _whiteboardManager?.SetImageOverlayHitTest(true);
                    VideoAreaContainer.Cursor = Cursor.Default;
                    ZoomBorder.PanButton = ButtonName.Left;
                    if (_toolSliderBackground != null)
                        await UIAnimations.SlideToolBackground(_toolSliderBackground, 0);
                    break;
                case ToolMode.Pen:
                    PenBtn.IsChecked = true;
                    MoveBtn.IsChecked = false;
                    EraserBtn.IsChecked = false;
                    InkCanvasOverlay.SetPenMode();
                    _whiteboardManager?.SetImageOverlayHitTest(false);
                    ZoomBorder.PanButton = ButtonName.Right;
                    if (_toolSliderBackground != null)
                        await UIAnimations.SlideToolBackground(_toolSliderBackground, ThemeManager.CurrentColors.SliderIndicatorSize);
                    break;
                case ToolMode.Eraser:
                    EraserBtn.IsChecked = true;
                    MoveBtn.IsChecked = false;
                    PenBtn.IsChecked = false;
                    InkCanvasOverlay.SetEraserMode();
                    _whiteboardManager?.SetImageOverlayHitTest(false);
                    ZoomBorder.PanButton = ButtonName.Right;
                    if (_toolSliderBackground != null)
                        await UIAnimations.SlideToolBackground(_toolSliderBackground, ThemeManager.CurrentColors.SliderIndicatorSize * 2);
                    break;
            }
        }

        private async void MoveBtn_Click(object? sender, RoutedEventArgs e)
        {
            await ApplyToolMode(ToolMode.Move);
        }

        private bool _penWasCheckedBeforeClick = false;

        private async void PenBtn_Click(object? sender, RoutedEventArgs e)
        {
            if (_penWasCheckedBeforeClick)
            {
                PenBtn.IsChecked = true;
                PenSettingsPopup.IsOpen = !PenSettingsPopup.IsOpen;
            }
            else
            {
                await ApplyToolMode(ToolMode.Pen);
            }
        }

        private bool _eraserWasCheckedBeforeClick = false;

        private async void EraserBtn_Click(object? sender, RoutedEventArgs e)
        {
            if (_eraserWasCheckedBeforeClick)
            {
                EraserBtn.IsChecked = true;
                if (_clearSlidePopup != null) _clearSlidePopup.IsOpen = !_clearSlidePopup.IsOpen;
            }
            else
            {
                await ApplyToolMode(ToolMode.Eraser);
            }
        }

        private void UndoInk_Click(object? sender, RoutedEventArgs e)
        {
            InkCanvasOverlay.Undo();
        }

        private void ClearInk_Click(object? sender, RoutedEventArgs e)
        {
            InkCanvasOverlay.ClearAll();
        }

        private void Capture_Click(object? sender, RoutedEventArgs e)
        {
            var frame = _cameraService.GetProcessedFrame();
            if (frame == null || frame.Empty())
                return;

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var fileName = $"photo_{timestamp}.jpg";

            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                "ShowWrite");

            Directory.CreateDirectory(dir);

            var path = Path.Combine(dir, fileName);
            Cv2.ImWrite(path, frame);

            var thumbnail = CreateThumbnail(path);

            Photos.Add(new PhotoItem
            {
                Index = Photos.Count + 1,
                FilePath = path,
                Timestamp = DateTime.Now,
                Thumbnail = thumbnail
            });

            ShowNotification("照片已保存");
        }

        private void ScanDocument_Click(object? sender, RoutedEventArgs e) { }

        private void VideoAdjustBtn_Click(object? sender, RoutedEventArgs e)
        {
            VideoAdjustPopup.IsOpen = !VideoAdjustPopup.IsOpen;
        }

        private void RotateLeft_Click(object? sender, RoutedEventArgs e)
        {
            VideoAdjustPopup.IsOpen = false;
            ApplyVideoRotation(-90);
        }

        private void RotateRight_Click(object? sender, RoutedEventArgs e)
        {
            VideoAdjustPopup.IsOpen = false;
            ApplyVideoRotation(90);
        }

        private double _currentVideoRotation = 0;

        private void ApplyVideoRotation(double angle)
        {
            var oldContentWidth = VideoAreaContainer.Width > 0 ? VideoAreaContainer.Width : (VideoImage.Source?.Size.Width ?? 1);
            var oldContentHeight = VideoAreaContainer.Height > 0 ? VideoAreaContainer.Height : (VideoImage.Source?.Size.Height ?? 1);
            var viewportCenterX = ZoomBorder.Bounds.Width / 2.0;
            var viewportCenterY = ZoomBorder.Bounds.Height / 2.0;
            var currentZoom = ZoomBorder.ZoomX > 0 ? ZoomBorder.ZoomX : 1.0;
            var currentOffsetX = ZoomBorder.OffsetX;
            var currentOffsetY = ZoomBorder.OffsetY;

            var centerContentX = (viewportCenterX - currentOffsetX) / currentZoom;
            var centerContentY = (viewportCenterY - currentOffsetY) / currentZoom;

            _currentVideoRotation = (_currentVideoRotation + angle) % 360;
            if (_currentVideoRotation < 0) _currentVideoRotation += 360;

            var sourceWidth = VideoImage.Source?.Size.Width ?? 1;
            var sourceHeight = VideoImage.Source?.Size.Height ?? 1;

            // 以左上角为旋转原点，并为 90/180/270 度补偿平移，避免旋转后内容偏移出容器。
            VideoImage.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative);
            var transformGroup = new TransformGroup();
            transformGroup.Children.Add(new RotateTransform(_currentVideoRotation));

            if (_currentVideoRotation == 90)
            {
                transformGroup.Children.Add(new TranslateTransform(sourceHeight, 0));
            }
            else if (_currentVideoRotation == 180)
            {
                transformGroup.Children.Add(new TranslateTransform(sourceWidth, sourceHeight));
            }
            else if (_currentVideoRotation == 270)
            {
                transformGroup.Children.Add(new TranslateTransform(0, sourceWidth));
            }

            VideoImage.RenderTransform = transformGroup;

            if (_currentVideoRotation == 90 || _currentVideoRotation == 270)
            {
                SetVideoContentSize(sourceHeight, sourceWidth);
            }
            else
            {
                SetVideoContentSize(sourceWidth, sourceHeight);
            }

            var rotatedCenter = RotateContentPointByAngle(
                centerContentX,
                centerContentY,
                oldContentWidth,
                oldContentHeight,
                angle);

            var offsetX = viewportCenterX - rotatedCenter.X * currentZoom;
            var offsetY = viewportCenterY - rotatedCenter.Y * currentZoom;
            var matrix = MatrixHelper.ScaleAndTranslate(currentZoom, currentZoom, offsetX, offsetY);
            ZoomBorder.SetMatrix(matrix, true);
        }

        private static Point RotateContentPointByAngle(
            double x,
            double y,
            double width,
            double height,
            double angle)
        {
            var normalizedAngle = ((int)Math.Round(angle) % 360 + 360) % 360;
            return normalizedAngle switch
            {
                90 => new Point(height - y, x),
                180 => new Point(width - x, height - y),
                270 => new Point(y, width - x),
                _ => new Point(x, y),
            };
        }

        private void PictureInPicture_Click(object? sender, RoutedEventArgs e)
        {
            if (_whiteboardManager == null) return;

            _cameraService?.CancelConnecting();
            _cameraService?.StopCapture();
            CloseLoadingWindow();

            var normalBottomButtons = this.FindControl<StackPanel>("NormalBottomButtons");
            var captureBtn = this.FindControl<Button>("CaptureBtn");
            var scanBtn = this.FindControl<Button>("ScanBtn");
            var connectDeviceBtn = this.FindControl<Button>("ConnectDeviceBtn");
            var pipBtn = this.FindControl<Button>("PictureInPictureBtn");
            var normalRightButtons = this.FindControl<StackPanel>("NormalRightButtons");

            _whiteboardManager.EnterWhiteboardMode(
                normalBottomButtons,
                captureBtn,
                scanBtn,
                connectDeviceBtn,
                pipBtn,
                normalRightButtons);
        }

        private void ExitWhiteboard_Click(object? sender, RoutedEventArgs e)
        {
            if (_whiteboardManager == null) return;

            // 关闭PowerPoint放映
            if (_whiteboardManager.IsPowerPointMode)
            {
                _whiteboardManager.ClosePowerPointPresentation();
            }

            var normalBottomButtons = this.FindControl<StackPanel>("NormalBottomButtons");
            var captureBtn = this.FindControl<Button>("CaptureBtn");
            var scanBtn = this.FindControl<Button>("ScanBtn");
            var connectDeviceBtn = this.FindControl<Button>("ConnectDeviceBtn");
            var pipBtn = this.FindControl<Button>("PictureInPictureBtn");
            var normalRightButtons = this.FindControl<StackPanel>("NormalRightButtons");
            var loadingContainer = this.FindControl<StackPanel>("LoadingContainer");
            var loadingPanel = this.FindControl<StackPanel>("LoadingElementsPanel");

            _whiteboardManager.ExitWhiteboardMode(
                normalBottomButtons,
                captureBtn,
                scanBtn,
                connectDeviceBtn,
                pipBtn,
                normalRightButtons,
                MoveBtn,
                PenBtn,
                EraserBtn,
                _toolSliderBackground,
                loadingContainer,
                loadingPanel,
                StartLoadingAnimation);
        }

        private void PrevPage_Click(object? sender, RoutedEventArgs e)
        {
            // PowerPoint模式
            if (_whiteboardManager?.IsPowerPointMode == true)
            {
                _whiteboardManager.PowerPointPreviousSlide();
                return;
            }

            // PptXml模式
            if (_whiteboardManager?.IsPptXmlMode == true)
            {
                _whiteboardManager.PrevPage_Click(sender, e);
                return;
            }

            // Aspose模式
            if (_whiteboardManager?.IsAsposePresentationOpen == true)
            {
                _whiteboardManager.AsposePreviousSlide();
                var slideBitmap = _whiteboardManager.RenderAsposeCurrentSlide();
                if (slideBitmap is not null)
                {
                    VideoImage.Source = slideBitmap;
                    SetVideoContentSize(slideBitmap.Size.Width, slideBitmap.Size.Height);
                    InkCanvasOverlay.SetPhotoMode(slideBitmap.Size.Width, slideBitmap.Size.Height);
                    InkCanvasOverlay.ClearStrokes();
                    CenterVideo();
                }
                return;
            }

            // 白板模式
            _whiteboardManager?.PrevPage_Click(sender, e);
        }

        private void NextPage_Click(object? sender, RoutedEventArgs e)
        {
            // PowerPoint模式
            if (_whiteboardManager?.IsPowerPointMode == true)
            {
                _whiteboardManager.PowerPointNextSlide();
                return;
            }

            // PptXml模式
            if (_whiteboardManager?.IsPptXmlMode == true)
            {
                _whiteboardManager.NextPage_Click(sender, e);
                return;
            }

            // Aspose模式
            if (_whiteboardManager?.IsAsposePresentationOpen == true)
            {
                _whiteboardManager.AsposeNextSlide();
                var slideBitmap = _whiteboardManager.RenderAsposeCurrentSlide();
                if (slideBitmap is not null)
                {
                    VideoImage.Source = slideBitmap;
                    SetVideoContentSize(slideBitmap.Size.Width, slideBitmap.Size.Height);
                    InkCanvasOverlay.SetPhotoMode(slideBitmap.Size.Width, slideBitmap.Size.Height);
                    InkCanvasOverlay.ClearStrokes();
                    CenterVideo();
                }
                return;
            }

            // 白板模式
            _whiteboardManager?.NextPage_Click(sender, e);
        }

        private void PageInfoBorder_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            _whiteboardManager?.PageInfoBorder_PointerPressed(sender, e);
        }

        private void PageItem_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            _whiteboardManager?.PageItem_PointerPressed(sender, e);
        }

        private void AddPageBtn_Click(object? sender, RoutedEventArgs e)
        {
            _whiteboardManager?.AddPageBtn_Click(sender, e);
        }

        private async void ImportPptBtn_Click(object? sender, RoutedEventArgs e)
        {
            await OpenFilePickerAsync();
        }

        private async void OnImportPptRequested()
        {
            await OpenFilePickerAsync();
        }

        private void OnPptSlideControlReady(PptSlideControl slideControl)
        {
            // 找到WhiteboardBackground
            var whiteboardBackground = this.FindControl<Border>("WhiteboardBackground");
            if (whiteboardBackground != null)
            {
                // 将背景设置为透明，确保不覆盖PPT背景
                whiteboardBackground.Background = Avalonia.Media.Brushes.Transparent;
                // 清空WhiteboardBackground的子元素
                whiteboardBackground.Child = null;
                // 将幻灯片控件添加到WhiteboardBackground
                whiteboardBackground.Child = slideControl;
                // 设置幻灯片控件的拉伸模式
                slideControl.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
                slideControl.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
                slideControl.Stretch = Avalonia.Media.Stretch.Uniform;
            }

            // 确保VideoImage不可见
            VideoImage.Source = null;
            VideoAreaContainer.Children.Clear();
        }

        private async Task OpenFilePickerAsync()
        {
            var storageProvider = StorageProvider;
            if (storageProvider == null) return;

            var files = await storageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "选择文件",
                AllowMultiple = true,
                FileTypeFilter = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("所有文件") { Patterns = new[] { "*.*" } }
                }
            });

            if (files.Count > 0)
            {
                var filePaths = files.Select(f => f.Path.LocalPath).ToList();
                // 区分 PPT 与普通图片/文件
                var pptFiles = filePaths.Where(p => p.EndsWith(".pptx", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".ppt", StringComparison.OrdinalIgnoreCase)).ToList();
                var otherFiles = filePaths.Except(pptFiles).ToList();

                // 处理 PPT 文件（使用 PPT XML 渲染）
                foreach (var ppt in pptFiles)
                {
                    // 直接打开 PPT 并渲染为元素
                    if (_whiteboardManager != null)
                    {
                        // 如果尚未进入板中板模式，先进入
                        if (!_whiteboardManager.IsWhiteboardMode)
                        {
                            // 进入白板模式（调用已有的 EnterWhiteboardMode）
                            var normalBottomButtons = this.FindControl<StackPanel>("NormalBottomButtons");
                            var captureBtn = this.FindControl<Button>("CaptureBtn");
                            var scanBtn = this.FindControl<Button>("ScanBtn");
                            var connectDeviceBtn = this.FindControl<Button>("ConnectDeviceBtn");
                            var pipBtn = this.FindControl<Button>("PipBtn");
                            var normalRightButtons = this.FindControl<StackPanel>("NormalRightButtons");
                            _whiteboardManager.EnterWhiteboardMode(normalBottomButtons!, captureBtn!, scanBtn!, connectDeviceBtn!, pipBtn!, normalRightButtons!);
                        }
                        // 打开 PPT XML
                        _whiteboardManager.OpenPptXmlPresentation(ppt);
                        var slideControl = _whiteboardManager.RenderPptXmlCurrentSlide();
                        if (slideControl != null)
                        {
                            // 若当前 PPT 幻灯片定义了背景图片，先设置为白板背景
                            var currentSlide = _whiteboardManager.CurrentPptSlide;
                            if (currentSlide?.BackgroundImage != null)
                            {
                                _whiteboardManager.SetWhiteboardBackgroundFromPptImage(currentSlide.BackgroundImage);
                            }
                            _whiteboardManager.SetWhiteboardBackgroundFromPptControl(slideControl);
                        }
                    }
                }

                // 处理其他文件（图片、PDF 等）
                if (otherFiles.Count > 0)
                {
                    await ImportFilesAsync(otherFiles);
                }
            }
        }

        private async Task LoadAndPlayPowerPoint(string filePath)
        {
            if (_whiteboardManager == null) return;

            try
            {
                // 进入白板模式
                if (!_whiteboardManager.IsWhiteboardMode)
                {
                    var normalBottomButtons = this.FindControl<StackPanel>("NormalBottomButtons");
                    var captureBtn = this.FindControl<Button>("CaptureBtn");
                    var scanBtn = this.FindControl<Button>("ScanBtn");
                    var connectDeviceBtn = this.FindControl<Button>("ConnectDeviceBtn");
                    var pipBtn = this.FindControl<Button>("PipBtn");
                    var normalRightButtons = this.FindControl<StackPanel>("NormalRightButtons");

                    _whiteboardManager.EnterWhiteboardMode(
                        normalBottomButtons!,
                        captureBtn!,
                        scanBtn!,
                        connectDeviceBtn!,
                        pipBtn!,
                        normalRightButtons!);
                }

                // 尝试使用PPT XML模式
                if (filePath.EndsWith(".pptx", StringComparison.OrdinalIgnoreCase))
                {
                    await LoadAndPlayPptXml(filePath);
                    return;
                }

                // 打开PPT文件
                if (_whiteboardManager.OpenPowerPointPresentation(filePath))
                {
                    // 获取白板背景控件作为宿主窗口
                    var whiteboardBackground = this.FindControl<Border>("WhiteboardBackground");
                    if (whiteboardBackground != null)
                    {
                        // 获取窗口句柄
                        var windowHandle = GetWindowHandle();

                        // 获取白板区域大小
                        var bounds = whiteboardBackground.Bounds;
                        int width = (int)bounds.Width;
                        int height = (int)bounds.Height;

                        // 开始放映
                        if (_whiteboardManager.StartPowerPointSlideShow(windowHandle, width, height))
                        {
                            StatusText.Inlines?.Clear();
                            StatusText.Inlines?.Add(new Run($"已加载PPT: {System.IO.Path.GetFileName(filePath)}"));
                            StatusText.IsVisible = true;
                        }
                        else
                        {
                            StatusText.Inlines?.Clear();
                            StatusText.Inlines?.Add(new Run("PPT放映启动失败"));
                            StatusText.IsVisible = true;
                        }
                    }
                }
                else
                {
                    StatusText.Inlines?.Clear();
                    StatusText.Inlines?.Add(new Run("无法打开PPT文件"));
                    StatusText.IsVisible = true;
                }
            }
            catch (Exception ex)
            {
                StatusText.Inlines?.Clear();
                StatusText.Inlines?.Add(new Run($"加载PPT失败: {ex.Message}"));
                StatusText.IsVisible = true;
            }
        }

        private async Task LoadAndPlayPptXml(string filePath)
        {
            if (_whiteboardManager == null) return;

            try
            {
                // 打开PPT XML文件
                _whiteboardManager.OpenPptXmlPresentation(filePath);

                // 进入板中板模式
                var normalBottomButtons = this.FindControl<StackPanel>("NormalBottomButtons");
                var captureBtn = this.FindControl<Button>("CaptureBtn");
                var scanBtn = this.FindControl<Button>("ScanBtn");
                var connectDeviceBtn = this.FindControl<Button>("ConnectDeviceBtn");
                var pipBtn = this.FindControl<Button>("PictureInPictureBtn");
                var normalRightButtons = this.FindControl<StackPanel>("NormalRightButtons");

                _whiteboardManager.EnterWhiteboardMode(
                    normalBottomButtons,
                    captureBtn,
                    scanBtn,
                    connectDeviceBtn,
                    pipBtn,
                    normalRightButtons);

                // 渲染第一页
                var slideControl = _whiteboardManager.RenderPptXmlCurrentSlide();
                if (slideControl != null)
                {
                    // 设置幻灯片为白板背景
                    _whiteboardManager.SetWhiteboardBackgroundFromPptControl(slideControl);

                    // 更新状态栏显示
                    int slideCount = _whiteboardManager.PptXmlSlideCount;
                    StatusText.Inlines?.Clear();
                    StatusText.Inlines?.Add(new Run($"已加载PPT (XML模式): {System.IO.Path.GetFileName(filePath)} - 共 {slideCount} 页"));
                    StatusText.IsVisible = true;
                }
                else
                {
                    StatusText.Inlines?.Clear();
                    StatusText.Inlines?.Add(new Run("无法渲染PPT内容"));
                    StatusText.IsVisible = true;
                }
            }
            catch (Exception ex)
            {
                StatusText.Inlines?.Clear();
                StatusText.Inlines?.Add(new Run($"加载PPT XML失败: {ex.Message}"));
                StatusText.IsVisible = true;
            }
        }

        private IntPtr GetWindowHandle()
        {
            // 获取Avalonia窗口的本地句柄
            var platformHandle = this.TryGetPlatformHandle();
            return platformHandle?.Handle ?? IntPtr.Zero;
        }

        private bool _photoPanelOpen = false;

        private async void TogglePhotoPanel_Click(object? sender, RoutedEventArgs e)
        {
            var photoPanel = this.FindControl<Border>("PhotoPanel");
            if (photoPanel == null) return;

            var toggleBtn = this.FindControl<Button>("TogglePhotoBtn");
            var icon = toggleBtn?.FindControl<SvgIcon>("TogglePhotoIcon");
            var label = toggleBtn?.FindControl<TextBlock>("TogglePhotoLabel");

            if (!_photoPanelOpen)
            {
                _photoPanelOpen = true;
                await UIAnimations.SlideInFromRight(photoPanel, 280);
                // Change to collapse state
                if (label != null) label.Text = "收回";
                if (icon != null)
                {
                    icon.IconName = "chevron-right";
                }
            }
            else
            {
                _photoPanelOpen = false;
                await UIAnimations.SlideOutToRight(photoPanel, 280);
                photoPanel.IsVisible = false;
                // Revert to original photo state
                if (label != null) label.Text = "照片";
                if (icon != null)
                {
                    icon.IconName = "photo";
                }
            }
        }


        private void ConnectDeviceBtn_Click(object? sender, RoutedEventArgs e)
        {
            if (_isPhotoAnnotationMode)
            {
                ExitPhotoAnnotationMode();
            }

            var dialog = new CameraSelectWindow(_cameraService);
            dialog.CameraSelected += (cameraIndex) =>
            {
                var loadingContainer = this.FindControl<StackPanel>("LoadingContainer");
                if (loadingContainer != null)
                {
                    loadingContainer.IsVisible = true;
                }
                var loadingPanel = this.FindControl<StackPanel>("LoadingElementsPanel");
                if (loadingPanel != null)
                {
                    loadingPanel.IsVisible = true;
                }
                StartLoadingAnimation();

                _cameraService?.StartCapture(cameraIndex);
            };
            dialog.ShowDialog(this);
        }

        private void SavePhotoBtn_Click(object? sender, RoutedEventArgs e)
        {
            IsSelectMode = true;
        }

        private async void ConfirmSavePhotos_Click(object? sender, RoutedEventArgs e)
        {
            var checkedPhotos = Photos.Where(p => p.IsChecked).ToList();
            if (checkedPhotos.Count == 0)
            {
                IsSelectMode = false;
                return;
            }

            var storageProvider = this.StorageProvider;
            if (storageProvider == null) return;

            var folder = await storageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
            {
                Title = "选择保存位置"
            });

            if (folder.Count == 0)
            {
                return;
            }

            var destDir = folder[0].Path.LocalPath;
            Directory.CreateDirectory(destDir);

            foreach (var photo in checkedPhotos)
            {
                if (string.IsNullOrEmpty(photo.FilePath) || !File.Exists(photo.FilePath))
                    continue;

                var destPath = Path.Combine(destDir, Path.GetFileName(photo.FilePath));

                if (IncludeInkAnnotations && photo.Strokes.Count > 0)
                {
                    SavePhotoWithAnnotations(photo, destPath);
                }
                else
                {
                    File.Copy(photo.FilePath, destPath, true);
                }
            }

            IsSelectMode = false;
            foreach (var p in Photos) p.IsChecked = false;
        }

        private void CancelSelectMode_Click(object? sender, RoutedEventArgs e)
        {
            IsSelectMode = false;
            foreach (var p in Photos) p.IsChecked = false;
        }

        private void InvertSelection_Click(object? sender, RoutedEventArgs e)
        {
            foreach (var p in Photos)
            {
                p.IsChecked = !p.IsChecked;
            }
        }

        private void SavePhotoWithAnnotations(PhotoItem photo, string destPath)
        {
            try
            {
                using var originalBitmap = SKBitmap.Decode(photo.FilePath);
                if (originalBitmap == null)
                {
                    File.Copy(photo.FilePath, destPath, true);
                    return;
                }

                using var surface = SKSurface.Create(new SKImageInfo(originalBitmap.Width, originalBitmap.Height));
                var canvas = surface.Canvas;

                canvas.DrawBitmap(originalBitmap, 0, 0);

                foreach (var stroke in photo.Strokes)
                {
                    if (stroke.VideoPoints == null || stroke.PointWidths == null) continue;

                    using var paint = new SKPaint
                    {
                        Style = SKPaintStyle.Fill,
                        IsAntialias = true,
                        Color = stroke.Color
                    };

                    for (int i = 0; i < stroke.VideoPoints.Count; i++)
                    {
                        var point = stroke.VideoPoints[i];
                        var width = stroke.PointWidths[i];
                        canvas.DrawCircle(point.X, point.Y, width / 2, paint);
                    }
                }

                using var image = surface.Snapshot();
                using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
                using var fileStream = File.OpenWrite(destPath);
                data.SaveTo(fileStream);
            }
            catch
            {
                File.Copy(photo.FilePath, destPath, true);
            }
        }

        private async void ImportPhotoBtn_Click(object? sender, RoutedEventArgs e)
        {
            var storageProvider = this.StorageProvider;
            if (storageProvider == null) return;

            var files = await storageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "选择照片或PDF文件",
                AllowMultiple = true,
                FileTypeFilter = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("图片文件")
                    {
                        Patterns = new[] { "*.jpg", "*.jpeg", "*.png", "*.bmp", "*.gif" }
                    },
                    new Avalonia.Platform.Storage.FilePickerFileType("PDF文件")
                    {
                        Patterns = new[] { "*.pdf" }
                    },
                    new Avalonia.Platform.Storage.FilePickerFileType("所有支持的文件")
                    {
                        Patterns = new[] { "*.jpg", "*.jpeg", "*.png", "*.bmp", "*.gif", "*.pdf" }
                    }
                }
            });

            if (files.Count == 0) return;

            ImportingOverlay.IsVisible = true;

            try
            {
                await System.Threading.Tasks.Task.Run(async () =>
                {
                    foreach (var file in files)
                    {
                        var path = file.Path.LocalPath;

                        if (path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                        {
                            var imagePaths = PdfService.ConvertPdfToImages(path);

                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                foreach (var imagePath in imagePaths)
                                {
                                    var thumbnail = CreateThumbnail(imagePath);

                                    Photos.Add(new PhotoItem
                                    {
                                        Index = Photos.Count + 1,
                                        FilePath = imagePath,
                                        Timestamp = DateTime.Now,
                                        Thumbnail = thumbnail
                                    });
                                }
                            });
                        }
                        else
                        {
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                var thumbnail = CreateThumbnail(path);

                                Photos.Add(new PhotoItem
                                {
                                    Index = Photos.Count + 1,
                                    FilePath = path,
                                    Timestamp = DateTime.Now,
                                    Thumbnail = thumbnail
                                });
                            });
                        }
                    }
                });
            }
            finally
            {
                ImportingOverlay.IsVisible = false;
            }
        }

        public async void OpenFiles(List<string> filePaths)
        {
            if (filePaths == null || filePaths.Count == 0) return;

            var imageFiles = new List<string>();
            string? pptFile = null;

            foreach (var path in filePaths)
            {
                var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".pptx" || ext == ".ppt")
                {
                    pptFile = path;
                }
                else
                {
                    imageFiles.Add(path);
                }
            }

            if (pptFile != null)
            {
                await LoadAndPlayPowerPoint(pptFile);
            }
            else if (imageFiles.Count > 0)
            {
                await ImportFilesAsync(imageFiles);
            }
        }

        private async Task ImportFilesAsync(List<string> filePaths)
        {
            ImportingOverlay.IsVisible = true;

            try
            {
                await System.Threading.Tasks.Task.Run(async () =>
                {
                    foreach (var path in filePaths)
                    {
                        if (path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                        {
                            var imagePaths = PdfService.ConvertPdfToImages(path);

                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                foreach (var imagePath in imagePaths)
                                {
                                    // 白板模式下，PDF图片作为图层导入
                                    if (_whiteboardManager?.IsWhiteboardMode == true)
                                    {
                                        _whiteboardManager.AddImageOverlay(imagePath);
                                    }
                                    else
                                    {
                                        var thumbnail = CreateThumbnail(imagePath);

                                        Photos.Add(new PhotoItem
                                        {
                                            Index = Photos.Count + 1,
                                            FilePath = imagePath,
                                            Timestamp = DateTime.Now,
                                            Thumbnail = thumbnail
                                        });
                                    }
                                }
                            });
                        }
                        else
                        {
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                // 白板模式下，图片作为图层导入
                                if (_whiteboardManager?.IsWhiteboardMode == true)
                                {
                                    _whiteboardManager.AddImageOverlay(path);
                                }
                                else
                                {
                                    var thumbnail = CreateThumbnail(path);

                                    Photos.Add(new PhotoItem
                                    {
                                        Index = Photos.Count + 1,
                                        FilePath = path,
                                        Timestamp = DateTime.Now,
                                        Thumbnail = thumbnail
                                    });
                                }
                            });
                        }
                    }
                });

                // 白板模式下不打开相册面板
                if (_whiteboardManager?.IsWhiteboardMode != true)
                {
                    await OpenPhotoPanelAsync();
                }
            }
            finally
            {
                ImportingOverlay.IsVisible = false;
            }
        }

        private async Task OpenPhotoPanelAsync()
        {
            var photoPanel = this.FindControl<Border>("PhotoPanel");
            if (photoPanel == null || _photoPanelOpen) return;

            _photoPanelOpen = true;
            await UIAnimations.SlideInFromRight(photoPanel, 280);
        }

        private void PhotoItem_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is Border border && border.Tag is PhotoItem photo)
            {
                _isPhotoItemDragging = true;
                _photoItemDragStartPoint = e.GetPosition(border);
                _isPhotoItemDragInProgress = false;
                e.Pointer.Capture(border);
            }
        }

        private void PhotoItem_PointerMoved(object? sender, PointerEventArgs e)
        {
            if (_isPhotoItemDragging && sender is Border border)
            {
                var currentPosition = e.GetPosition(border);
                var delta = currentPosition - _photoItemDragStartPoint;
                var distance = Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y);

                if (distance > PhotoItemDragThreshold)
                {
                    _isPhotoItemDragInProgress = true;
                }
            }
        }

        private void PhotoItem_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (sender is Border border && border.Tag is PhotoItem photo)
            {
                if (!_isPhotoItemDragInProgress)
                {
                    if (photo.IsSelected && _isPhotoAnnotationMode)
                    {
                        ExitPhotoAnnotationMode();
                        return;
                    }

                    if (_selectedPhoto != null && _isPhotoAnnotationMode)
                    {
                        _selectedPhoto.Strokes = InkCanvasOverlay.GetStrokes();
                    }

                    foreach (var p in Photos)
                    {
                        p.IsSelected = false;
                    }

                    photo.IsSelected = true;
                    _selectedPhoto = photo;

                    ShowPhotoForAnnotation(photo);
                }

                _isPhotoItemDragging = false;
                _isPhotoItemDragInProgress = false;
            }
        }

        private async void ShowPhotoForAnnotation(PhotoItem photo)
        {
            if (string.IsNullOrEmpty(photo.FilePath) || !File.Exists(photo.FilePath))
                return;

            _isPhotoAnnotationMode = true;

            _cameraService?.CancelConnecting();
            _cameraService?.StopCapture();
            CloseLoadingWindow();

            var bitmap = await LoadBitmapAsync(photo.FilePath);
            if (bitmap != null)
            {
                VideoImage.Source = bitmap;
                SetVideoContentSize(bitmap.Size.Width, bitmap.Size.Height);
                QueueFitPhotoToViewportHeight();

                InkCanvasOverlay.SetPhotoMode(bitmap.Size.Width, bitmap.Size.Height);
                InkCanvasOverlay.SetStrokes(photo.Strokes);
            }

            StatusText.IsVisible = false;

            // 更新光标覆盖层布局
            UpdateCursorCanvasLayout();
        }

        private async Task<Bitmap?> LoadBitmapAsync(string path)
        {
            return await Task.Run(() =>
            {
                if (!File.Exists(path)) return null;
                try
                {
                    using var stream = File.OpenRead(path);
                    return new Bitmap(stream);
                }
                catch
                {
                    return null;
                }
            });
        }

        public void ExitPhotoAnnotationMode()
        {
            if (!_isPhotoAnnotationMode) return;

            _isPhotoAnnotationMode = false;

            if (_selectedPhoto != null)
            {
                _selectedPhoto.Strokes = InkCanvasOverlay.GetStrokes();
                _selectedPhoto.IsSelected = false;
                _selectedPhoto = null;
            }

            // 隐藏当前显示的照片
            VideoImage.Source = null;
            VideoImage.IsVisible = false;

            InkCanvasOverlay.ExitPhotoMode();
            InkCanvasOverlay.ClearStrokes();

            if (!_cameraService.IsConnected)
            {
                var loadingContainer = this.FindControl<StackPanel>("LoadingContainer");
                if (loadingContainer != null)
                {
                    loadingContainer.IsVisible = true;
                }
                var loadingPanel = this.FindControl<StackPanel>("LoadingElementsPanel");
                if (loadingPanel != null)
                {
                    loadingPanel.IsVisible = true;
                }
                StartLoadingAnimation();
            }
            // 重新检测并连接摄像头，连接成功后会在 PresentCameraFrame 中重新显示画面
            _cameraService?.DetectAndConnectCamera();

            // 更新光标覆盖层布局
            UpdateCursorCanvasLayout();
        }

        private void DeletePhoto_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is PhotoItem photo)
            {
                photo.IsDeleting = true;
            }
            e.Handled = true;
        }

        private void ConfirmDeletePhoto_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is PhotoItem photo)
            {
                if (_selectedPhoto == photo && _isPhotoAnnotationMode)
                {
                    ExitPhotoAnnotationMode();
                }

                if (!string.IsNullOrEmpty(photo.FilePath) && File.Exists(photo.FilePath))
                {
                    try
                    {
                        File.Delete(photo.FilePath);
                    }
                    catch { }
                }

                Photos.Remove(photo);

                for (int i = 0; i < Photos.Count; i++)
                {
                    Photos[i].Index = i + 1;
                }
            }
            e.Handled = true;
        }

        private void CancelDeletePhoto_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is PhotoItem photo)
            {
                photo.IsDeleting = false;
            }
            e.Handled = true;
        }

        private Bitmap? CreateThumbnail(string imagePath)
        {
            try
            {
                using var stream = File.OpenRead(imagePath);
                var decodeTask = Task.Run(() => SkiaSharp.SKBitmap.Decode(stream));
                decodeTask.Wait();

                using var originalBitmap = decodeTask.Result;
                if (originalBitmap == null) return null;

                int thumbWidth = 260;
                int thumbHeight = (int)(originalBitmap.Height * ((double)thumbWidth / originalBitmap.Width));

                using var resizedBitmap = originalBitmap.Resize(new SkiaSharp.SKImageInfo(thumbWidth, thumbHeight), SkiaSharp.SKSamplingOptions.Default);
                if (resizedBitmap == null) return null;

                using var image = SkiaSharp.SKImage.FromBitmap(resizedBitmap);
                using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 80);

                var ms = new MemoryStream(data.ToArray());
                return new Bitmap(ms);
            }
            catch
            {
                return null;
            }
        }

        private void SwitchCamera_Click(object? sender, RoutedEventArgs e)
        {
            MoreMenuPopup.IsOpen = false;

            if (_isPhotoAnnotationMode)
            {
                ExitPhotoAnnotationMode();
            }

            var win = new CameraSelectWindow(_cameraService);
            win.CameraSelected += idx =>
            {
                var loadingContainer = this.FindControl<StackPanel>("LoadingContainer");
                if (loadingContainer != null)
                {
                    loadingContainer.IsVisible = true;
                }
                var loadingPanel = this.FindControl<StackPanel>("LoadingElementsPanel");
                if (loadingPanel != null)
                {
                    loadingPanel.IsVisible = true;
                }
                StartLoadingAnimation();

                _cameraService?.SwitchToCamera(idx);
            };
            win.ShowDialog(this);
        }

        private void OpenSettings_Click(object? sender, RoutedEventArgs e)
        {
            MoreMenuPopup.IsOpen = false;
            var settingsWindow = new SettingsWindow();
            settingsWindow.ShowDialog(this);
        }

        private void Exit_Click(object? sender, RoutedEventArgs e)
        {
            this.Close();
        }

        #endregion

        #region 梯形校正

        private void InitializeKeystonePoints()
        {
            if (_keystonePointTL != null)
            {
                _keystonePointTL.PointerPressed += KeystonePoint_PointerPressed;
                _keystonePointTL.PointerMoved += KeystonePoint_PointerMoved;
                _keystonePointTL.PointerReleased += KeystonePoint_PointerReleased;
            }
            if (_keystonePointTR != null)
            {
                _keystonePointTR.PointerPressed += KeystonePoint_PointerPressed;
                _keystonePointTR.PointerMoved += KeystonePoint_PointerMoved;
                _keystonePointTR.PointerReleased += KeystonePoint_PointerReleased;
            }
            if (_keystonePointBR != null)
            {
                _keystonePointBR.PointerPressed += KeystonePoint_PointerPressed;
                _keystonePointBR.PointerMoved += KeystonePoint_PointerMoved;
                _keystonePointBR.PointerReleased += KeystonePoint_PointerReleased;
            }
            if (_keystonePointBL != null)
            {
                _keystonePointBL.PointerPressed += KeystonePoint_PointerPressed;
                _keystonePointBL.PointerMoved += KeystonePoint_PointerMoved;
                _keystonePointBL.PointerReleased += KeystonePoint_PointerReleased;
            }
        }

        private void KeystoneCorrection_Click(object? sender, RoutedEventArgs e)
        {
            MoreMenuPopup.IsOpen = false;
            EnterKeystoneCorrectionMode();
        }

        private void EnterKeystoneCorrectionMode()
        {
            _isKeystoneCorrectionMode = true;

            // 将 KeystoneButtonGroup 移动到中间列并居中
            if (_keystoneButtonGroup != null)
            {
                Grid.SetColumn(_keystoneButtonGroup, 1);
                _keystoneButtonGroup.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
                _keystoneButtonGroup.IsVisible = true;
            }

            // 隐藏其他按钮组
            if (_leftButtonGroup != null)
                _leftButtonGroup.IsVisible = false;
            if (_centerButtonGroup != null)
                _centerButtonGroup.IsVisible = false;
            if (_rightButtonGroup != null)
                _rightButtonGroup.IsVisible = false;

            if (_keystoneOverlayCanvas != null)
                _keystoneOverlayCanvas.IsVisible = true;

            // 初始化校正点（原有代码）
            var existingPoints = _cameraService.GetSourcePoints();
            if (existingPoints != null && existingPoints.Length == 4)
            {
                for (int i = 0; i < 4; i++)
                {
                    _keystonePoints[i] = existingPoints[i];
                    _originalKeystonePoints[i] = existingPoints[i];
                }
                _cameraService.ResetPerspectiveTransform();
            }
            else
            {
                var defaultPoints = _cameraService.GetDefaultSourcePoints(_videoWidth, _videoHeight);
                for (int i = 0; i < 4; i++)
                {
                    _keystonePoints[i] = defaultPoints[i];
                    _originalKeystonePoints[i] = defaultPoints[i];
                }
            }

            UpdateKeystonePointsDisplay();
            UpdateKeystonePolygon();
        }
        private async void OpenRandomNoteWindow_Click(object? sender, RoutedEventArgs e)
        {
            MoreMenuPopup.IsOpen = false; // 关闭菜单

            var dialog = new RandomNoteSettingsWindow(_cameraService);
            await dialog.ShowDialog(this);
        }

        public void ShowNotificationAction(string message)
        {
            // 这里调用原始的 ShowNotification 方法，并传递默认的 durationMs
            ShowNotification(message, 3000);
        }
        private void MainWindow_KeyDown(object? sender, KeyEventArgs e)
        {
            string pressed = GetKeyString(e);
            var settings = Config.Load().RandomNote;
            if (pressed == settings.ShortcutKey)
            {
                RandomNoteSettingsWindow.TryTriggerShortcut(pressed, _cameraService, new Action<string>(ShowNotificationAction));
                e.Handled = true;
            }
        }

        private string GetKeyString(KeyEventArgs e)
        {
            var parts = new List<string>();
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
            if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
            if (e.Key != Key.LeftCtrl && e.Key != Key.RightCtrl && e.Key != Key.LeftAlt && e.Key != Key.RightAlt &&
                e.Key != Key.LeftShift && e.Key != Key.RightShift && e.Key != Key.LWin && e.Key != Key.RWin)
            {
                parts.Add(e.Key.ToString());
            }
            return string.Join("+", parts);
        }

        private void ExitKeystoneCorrectionMode()
        {
            _isKeystoneCorrectionMode = false;

            // 将 KeystoneButtonGroup 移回左侧列并左对齐
            if (_keystoneButtonGroup != null)
            {
                Grid.SetColumn(_keystoneButtonGroup, 0);
                _keystoneButtonGroup.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
                _keystoneButtonGroup.IsVisible = false;
            }

            // 恢复其他按钮组
            if (_leftButtonGroup != null)
                _leftButtonGroup.IsVisible = true;
            if (_centerButtonGroup != null)
                _centerButtonGroup.IsVisible = true;
            if (_rightButtonGroup != null)
                _rightButtonGroup.IsVisible = true;

            if (_keystoneOverlayCanvas != null)
                _keystoneOverlayCanvas.IsVisible = false;
        }

        private void ConfirmKeystone_Click(object? sender, RoutedEventArgs e)
        {
            var destPoints = CalculateAspectRatioDestPoints(_videoWidth, _videoHeight);
            _cameraService.SetPerspectiveTransform(_keystonePoints, destPoints);

            SaveKeystoneSettings();
            ExitKeystoneCorrectionMode();
        }

        private void CancelKeystone_Click(object? sender, RoutedEventArgs e)
        {
            ExitKeystoneCorrectionMode();
        }

        private void ResetKeystone_Click(object? sender, RoutedEventArgs e)
        {
            var defaultPoints = _cameraService.GetDefaultSourcePoints(_videoWidth, _videoHeight);
            for (int i = 0; i < 4; i++)
            {
                _keystonePoints[i] = defaultPoints[i];
            }
            UpdateKeystonePointsDisplay();
            UpdateKeystonePolygon();
        }

        private void ClearKeystone_Click(object? sender, RoutedEventArgs e)
        {
            MoreMenuPopup.IsOpen = false;
            _cameraService.ResetPerspectiveTransform();

            var config = Config.Load();
            var cameraIndex = _cameraService.CurrentCameraIndex;
            if (config.CameraKeystoneSettings.ContainsKey(cameraIndex))
            {
                config.CameraKeystoneSettings.Remove(cameraIndex);
                config.Save();
            }
        }

        private void SaveKeystoneSettings()
        {
            var config = Config.Load();
            var cameraIndex = _cameraService.CurrentCameraIndex;

            config.CameraKeystoneSettings[cameraIndex] = new KeystonePoints
            {
                TLX = _keystonePoints[0].X,
                TLY = _keystonePoints[0].Y,
                TRX = _keystonePoints[1].X,
                TRY = _keystonePoints[1].Y,
                BRX = _keystonePoints[2].X,
                BRY = _keystonePoints[2].Y,
                BLX = _keystonePoints[3].X,
                BLY = _keystonePoints[3].Y,
                AspectRatio = _selectedAspectRatio
            };

            config.Save();
        }

        private void LoadKeystoneSettings()
        {
            var config = Config.Load();
            var cameraIndex = _cameraService.CurrentCameraIndex;

            if (config.CameraKeystoneSettings.TryGetValue(cameraIndex, out var points))
            {
                var sourcePoints = new OpenCvSharp.Point2f[]
                {
                    new OpenCvSharp.Point2f(points.TLX, points.TLY),
                    new OpenCvSharp.Point2f(points.TRX, points.TRY),
                    new OpenCvSharp.Point2f(points.BRX, points.BRY),
                    new OpenCvSharp.Point2f(points.BLX, points.BLY)
                };

                _selectedAspectRatio = points.AspectRatio ?? "自由";
                if (_aspectRatioDropDown != null)
                {
                    for (int i = 0; i < _aspectRatioDropDown.ItemCount; i++)
                    {
                        if (_aspectRatioDropDown.Items[i] is ComboBoxItem item && 
                            item.Content?.ToString() == _selectedAspectRatio)
                        {
                            _aspectRatioDropDown.SelectedIndex = i;
                            break;
                        }
                    }
                }

                var destPoints = CalculateAspectRatioDestPoints(_videoWidth, _videoHeight);
                _cameraService.SetPerspectiveTransform(sourcePoints, destPoints);
            }
        }

        private void KeystonePoint_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is Border border && border.Tag != null)
            {
                _isDraggingKeystonePoint = true;
                _draggingKeystonePoint = border;
                var position = e.GetPosition(_keystoneOverlayCanvas);
                var pointIndex = Convert.ToInt32(border.Tag);
                var pointX = _keystonePoints[pointIndex].X;
                var pointY = _keystonePoints[pointIndex].Y;
                _keystonePointOffset = new Point(position.X - pointX, position.Y - pointY);
                e.Pointer.Capture(border);
                e.Handled = true;
            }
        }

        private void KeystonePoint_PointerMoved(object? sender, PointerEventArgs e)
        {
            if (!_isDraggingKeystonePoint || _draggingKeystonePoint == null || _keystoneOverlayCanvas == null)
                return;

            var position = e.GetPosition(_keystoneOverlayCanvas);
            var pointIndex = Convert.ToInt32(_draggingKeystonePoint.Tag);

            var videoX = position.X - _keystonePointOffset.X;
            var videoY = position.Y - _keystonePointOffset.Y;

            videoX = Math.Clamp(videoX, 0, _videoWidth);
            videoY = Math.Clamp(videoY, 0, _videoHeight);

            _keystonePoints[pointIndex] = new OpenCvSharp.Point2f((float)videoX, (float)videoY);

            UpdateKeystonePointsDisplay();
            UpdateKeystonePolygon();
            e.Handled = true;
        }

        private void KeystonePoint_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (_isDraggingKeystonePoint)
            {
                _isDraggingKeystonePoint = false;
                if (_draggingKeystonePoint != null)
                {
                    e.Pointer.Capture(null);
                }
                _draggingKeystonePoint = null;
                e.Handled = true;
            }
        }

        private void UpdateKeystonePointsDisplay()
        {
            if (_keystonePointTL == null || _keystonePointTR == null ||
                _keystonePointBR == null || _keystonePointBL == null ||
                _keystoneOverlayCanvas == null)
                return;

            var offsetX = -12;
            var offsetY = -12;

            // 使用完全限定名避免与 SkiaSharp.SKCanvas 冲突
            Avalonia.Controls.Canvas.SetLeft(_keystonePointTL, _keystonePoints[0].X + offsetX);
            Avalonia.Controls.Canvas.SetTop(_keystonePointTL, _keystonePoints[0].Y + offsetY);

            Avalonia.Controls.Canvas.SetLeft(_keystonePointTR, _keystonePoints[1].X + offsetX);
            Avalonia.Controls.Canvas.SetTop(_keystonePointTR, _keystonePoints[1].Y + offsetY);

            Avalonia.Controls.Canvas.SetLeft(_keystonePointBR, _keystonePoints[2].X + offsetX);
            Avalonia.Controls.Canvas.SetTop(_keystonePointBR, _keystonePoints[2].Y + offsetY);

            Avalonia.Controls.Canvas.SetLeft(_keystonePointBL, _keystonePoints[3].X + offsetX);
            Avalonia.Controls.Canvas.SetTop(_keystonePointBL, _keystonePoints[3].Y + offsetY);
        }

        private void UpdateKeystonePolygon()
        {
            if (_keystonePolygon == null)
                return;

            var points = new List<Point>
            {
                new Point(_keystonePoints[0].X, _keystonePoints[0].Y),
                new Point(_keystonePoints[1].X, _keystonePoints[1].Y),
                new Point(_keystonePoints[2].X, _keystonePoints[2].Y),
                new Point(_keystonePoints[3].X, _keystonePoints[3].Y)
            };

            _keystonePolygon.Points = new Points(points);
        }

        private void AspectRatioDropDown_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_aspectRatioDropDown?.SelectedItem is ComboBoxItem item)
            {
                _selectedAspectRatio = item.Content?.ToString() ?? "自由";
            }
        }

        private OpenCvSharp.Point2f[] CalculateAspectRatioDestPoints(int width, int height)
        {
            float targetRatio = 0f;
            
            switch (_selectedAspectRatio)
            {
                case "A4 (1:1.414)":
                    targetRatio = 1.414f;
                    break;
                case "B4":
                    targetRatio = 353f / 250f;
                    break;
                case "自由":
                default:
                    return new OpenCvSharp.Point2f[]
                    {
                        new OpenCvSharp.Point2f(0, 0),
                        new OpenCvSharp.Point2f(width, 0),
                        new OpenCvSharp.Point2f(width, height),
                        new OpenCvSharp.Point2f(0, height)
                    };
            }

            float currentRatio = (float)height / width;
            float newWidth, newHeight, offsetX, offsetY;

            if (currentRatio > targetRatio)
            {
                newHeight = height;
                newWidth = height / targetRatio;
                offsetX = (newWidth - width) / 2;
                offsetY = 0;
            }
            else
            {
                newWidth = width;
                newHeight = width * targetRatio;
                offsetX = 0;
                offsetY = (newHeight - height) / 2;
            }

            return new OpenCvSharp.Point2f[]
            {
                new OpenCvSharp.Point2f(-offsetX, -offsetY),
                new OpenCvSharp.Point2f(width + offsetX, -offsetY),
                new OpenCvSharp.Point2f(width + offsetX, height + offsetY),
                new OpenCvSharp.Point2f(-offsetX, height + offsetY)
            };
        }

        #endregion

        private void OnThemeChanged(ThemeType theme)
        {
            UpdateButtonGroupStyles();
        }

        private void UpdateButtonGroupStyles()
        {
            var colors = ThemeManager.CurrentColors;

            UpdateButtonGroupShadow(_leftButtonGroup, colors.ShowButtonShadow);
            UpdateButtonGroupShadow(_keystoneButtonGroup, colors.ShowButtonShadow);
            UpdateButtonGroupShadow(_centerButtonGroup, colors.ShowButtonShadow);
            UpdateButtonGroupShadow(_rightButtonGroup, colors.ShowButtonShadow);

            UpdateButtonGroupBackground(_leftButtonGroup, colors.ShowButtonGroupBackground);
            UpdateButtonGroupBackground(_keystoneButtonGroup, colors.ShowButtonGroupBackground);
            UpdateButtonGroupBackground(_centerButtonGroup, colors.ShowButtonGroupBackground);
            UpdateButtonGroupBackground(_rightButtonGroup, colors.ShowButtonGroupBackground);

            UpdateButtonTextVisibility(_leftButtonGroup, colors.ShowButtonText);
            UpdateButtonTextVisibility(_keystoneButtonGroup, colors.ShowButtonText);
            UpdateButtonTextVisibility(_centerButtonGroup, colors.ShowButtonText);
            UpdateButtonTextVisibility(_rightButtonGroup, colors.ShowButtonText);

            UpdateButtonIconSizes(_leftButtonGroup, colors.IconSize, colors.ButtonSize);
            UpdateButtonIconSizes(_keystoneButtonGroup, colors.IconSize, colors.ButtonSize);
            UpdateButtonIconSizes(_centerButtonGroup, colors.IconSize, colors.ButtonSize);
            UpdateButtonIconSizes(_rightButtonGroup, colors.IconSize, colors.ButtonSize);

            if (_toolSliderBackground != null)
            {
                _toolSliderBackground.Width = colors.SliderIndicatorSize;
                _toolSliderBackground.Height = colors.SliderIndicatorSize;
            }

            UpdateButtonGroupPadding(_leftButtonGroup, colors.ButtonGroupPadding);
            UpdateButtonGroupPadding(_keystoneButtonGroup, colors.ButtonGroupPadding);
            UpdateButtonGroupPadding(_centerButtonGroup, colors.ButtonGroupPadding);
            UpdateButtonGroupPadding(_rightButtonGroup, colors.ButtonGroupPadding);
        }

        private void UpdateButtonGroupShadow(Border? border, bool showShadow)
        {
            if (border == null) return;

            if (showShadow)
            {
                var shadowColor = ThemeManager.CurrentTheme == ThemeType.NoBackground
                    ? Color.Parse("#66808080")
                    : Color.Parse("#33000000");

                var shadowBlur = ThemeManager.CurrentTheme == ThemeType.NoBackground ? 16 : 12;
                var shadowOffsetY = ThemeManager.CurrentTheme == ThemeType.NoBackground ? 6 : 4;

                border.BoxShadow = new BoxShadows(new BoxShadow
                {
                    Color = shadowColor,
                    OffsetX = 0,
                    OffsetY = shadowOffsetY,
                    Blur = shadowBlur,
                    Spread = 0
                });
            }
            else
            {
                border.BoxShadow = default;
            }
        }

        private void UpdateButtonGroupBackground(Border? border, bool showBackground)
        {
            if (border == null) return;

            if (showBackground)
            {
                border.Classes.Remove("no-background");

                var toggleButtons = border.GetLogicalDescendants().OfType<ToggleButton>();
                foreach (var btn in toggleButtons)
                {
                    btn.Classes.Remove("no-background");
                }
            }
            else
            {
                border.Classes.Add("no-background");

                var toggleButtons = border.GetLogicalDescendants().OfType<ToggleButton>();
                foreach (var btn in toggleButtons)
                {
                    btn.Classes.Add("no-background");
                }
            }
        }

        private void UpdateButtonTextVisibility(Border? border, bool showText)
        {
            if (border == null) return;
            var textBlocks = border.GetLogicalDescendants().OfType<TextBlock>()
                .Where(tb => tb.Classes.Contains("button-text-label") || tb.FontSize == 10 || tb.FontSize == 9);
            foreach (var tb in textBlocks)
            {
                tb.IsVisible = showText;
            }
        }

        private void UpdateIconAlignment(Border? border, bool showText)
        {
            if (border == null) return;

            var buttons = border.GetLogicalDescendants().OfType<Button>();
            var toggleButtons = border.GetLogicalDescendants().OfType<ToggleButton>();

            foreach (var btn in buttons.Concat(toggleButtons))
            {
                var grid = btn.GetLogicalDescendants().OfType<Grid>().FirstOrDefault();
                if (grid == null) continue;

                var viewbox = grid.GetLogicalDescendants().OfType<Viewbox>().FirstOrDefault();
                if (viewbox == null) continue;

                if (showText)
                {
                    viewbox.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
                    viewbox.Margin = new Thickness(0, 4, 0, 0);
                }
                else
                {
                    viewbox.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
                    viewbox.Margin = new Thickness(0);
                }
            }
        }

        private void UpdateButtonIconSizes(Border? border, double iconSize, double buttonSize)
        {
            if (border == null) return;

            var buttons = border.GetLogicalDescendants().OfType<Button>();
            var toggleButtons = border.GetLogicalDescendants().OfType<ToggleButton>();

            foreach (var btn in buttons)
            {
                btn.Width = buttonSize;
                btn.Height = buttonSize;

                var viewbox = btn.GetLogicalDescendants().OfType<Viewbox>().FirstOrDefault();
                if (viewbox != null)
                {
                    viewbox.Width = iconSize;
                    viewbox.Height = iconSize;
                }
            }

            foreach (var btn in toggleButtons)
            {
                btn.Width = buttonSize;
                btn.Height = buttonSize;

                var viewbox = btn.GetLogicalDescendants().OfType<Viewbox>().FirstOrDefault();
                if (viewbox != null)
                {
                    viewbox.Width = iconSize;
                    viewbox.Height = iconSize;
                }
            }
        }

        private void UpdateButtonGroupPadding(Border? border, double padding)
        {
            if (border == null) return;

            border.Padding = new Thickness(padding, 4, padding, 4);
        }
    }

    public class PhotoItem : System.ComponentModel.INotifyPropertyChanged
    {
        private bool _isSelected;
        private bool _isDeleting;
        private bool _isChecked;

        public int Index { get; set; }
        public string? FilePath { get; set; }
        public DateTime Timestamp { get; set; }
        public Bitmap? Thumbnail { get; set; }
        public List<InkStroke> Strokes { get; set; } = new();

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsSelected)));
                }
            }
        }

        public bool IsDeleting
        {
            get => _isDeleting;
            set
            {
                if (_isDeleting != value)
                {
                    _isDeleting = value;
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsDeleting)));
                }
            }
        }

        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked != value)
                {
                    _isChecked = value;
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsChecked)));
                }
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }

    public class SelectedBackgroundConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool isSelected && isSelected)
                return new SolidColorBrush(Color.FromRgb(0, 120, 212));
            return new SolidColorBrush(Color.FromRgb(61, 61, 61));
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class SelectedForegroundConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool isSelected && isSelected)
                return Brushes.White;
            return new SolidColorBrush(Color.FromRgb(170, 170, 170));
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class WhiteboardPageThumbnail : INotifyPropertyChanged
    {
        private int _pageNumber;
        private Bitmap? _thumbnail;
        private bool _isSelected;

        public int PageNumber
        {
            get => _pageNumber;
            set
            {
                if (_pageNumber != value)
                {
                    _pageNumber = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PageNumber)));
                }
            }
        }

        public Bitmap? Thumbnail
        {
            get => _thumbnail;
            set
            {
                if (_thumbnail != value)
                {
                    _thumbnail = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumbnail)));
                }
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
