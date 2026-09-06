using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using SkiaSharp;
using System;
using System.Collections.Generic;

namespace ShowWrite
{
    public class InkCanvas : Control, IDisposable
    {
        private int _videoWidth;
        private int _videoHeight;

        private bool _isPhotoMode;
        private double _photoWidth;
        private double _photoHeight;

        private bool _isWhiteboardMode;
        private string? _whiteboardBackgroundPath;
        private SKBitmap? _whiteboardBackgroundBitmap;

        private readonly List<InkStroke> _strokes = new();
        private List<SKPoint>? _currentVideoPoints;
        private List<SKPoint>? _currentScreenPoints;
        private List<float>? _currentPointWidths;
        private List<float>? _currentZoomFactors;
        private List<long>? _currentTimestamps;
        private bool _currentIsEraser;
        private float _currentSize;
        private SKColor _currentColor;
        private float _currentRatio = 0.5f;
        private const float BasePenWidthScale = 0.8f;

        private List<InkStroke>? _tempStrokes;
        private SKPoint _lastEraserPoint;
        private bool _hasLastEraserPoint;

        private WriteableBitmap? _displayBitmap;

        // 已提交笔迹的屏幕空间缓存：缩放/平移/书写时只搬运缓存位图，无需每帧重绘全部笔迹
        private SKBitmap? _inkCache;
        private bool _inkCacheDirty = true;
        private double _cacheZoom = 1.0;          // 构建缓存时使用的缩放
        private Point _cacheOffset = new Point(0, 0); // 构建缓存时使用的平移（pan + zoomBorderOffset）
        private double _prevRenderZoom = double.NaN;
        private double _prevRenderOffsetX = double.NaN;
        private double _prevRenderOffsetY = double.NaN;
        private DateTime _lastTransformChangeUtc = DateTime.MinValue;
        private DateTime _lastEraseRebuildUtc = DateTime.MinValue;
        private double _displayZoom = double.NaN;   // _displayBitmap 内容渲染时使用的变换
        private Point _displayOffset = new Point(0, 0);
        private bool _forceContentRender = true;    // 强制下一帧重写 _displayBitmap（内容变化时）
        private DispatcherTimer? _cacheRebuildTimer;
        private const double CacheRebuildDelayMs = 120;      // 变换稳定后重建缓存的延迟
        private const double CacheRebuildScaleDrift = 0.25;  // 连续缩放超过 25% 强制重建，避免过度模糊
        private const double EraseRebuildIntervalMs = 50;    // 擦除拖动期间的缓存重建节流间隔

        private bool _isDrawing;
        private bool _invalidateScheduled = false;

        private Image? _videoImage;

        private double _currentZoom = 1.0;
        private Point _currentPan = new Point(0, 0);
        private Point _zoomBorderOffset = new Point(0, 0);

        // 渲染时实际使用的变换（可能处于动画中间状态，与摄像头画面的过渡动画保持同步）
        private double _renderZoom = 1.0;
        private Point _renderPan = new Point(0, 0);
        private Visual? _transformSource;
        private EventHandler<AvaloniaPropertyChangedEventArgs>? _transformSourceHandler;

        private bool _isPalmEraserActive = false;
        private bool _lastModeBeforePalmEraser = false;
        private double _palmEraserThreshold = 5000.0;
        private double _currentTouchArea = 0.0;
        private bool _enablePalmEraser = true;
        private int _palmActivationHitCount = 0;
        private int _palmReleaseHitCount = 0;
        private DateTime _lastPalmHitTimeUtc = DateTime.MinValue;
        private const int PalmReleaseDebounceMs = 90;

        // 橡皮光标事件
        public event Action<Point, float, bool>? EraserCursorUpdate;

        public int PenSize { get; set; } = 4;
        public SKColor PenColor { get; set; } = SKColors.Red;
        public int EraserSize { get; set; } = 45;
        public PenSettings PenSettings { get; set; } = new PenSettings();

        public double PalmEraserThreshold
        {
            get => _palmEraserThreshold;
            set => _palmEraserThreshold = Math.Max(1000, value);
        }

        public bool EnablePalmEraser
        {
            get => _enablePalmEraser;
            set => _enablePalmEraser = value;
        }

        public bool IsPalmEraserActive => _isPalmEraserActive;

        public bool IsPenMode { get; private set; }
        public bool IsEraserMode { get; private set; }

        public InkCanvas()
        {
            ClipToBounds = false;
            // 高 DPI 适配（已使用布局取整）
            UseLayoutRounding = true;

            PointerPressed += OnPointerPressed;
            PointerMoved += OnPointerMoved;
            PointerReleased += OnPointerReleased;
            PointerCaptureLost += OnPointerCaptureLost;

            // 变换（缩放/拖拽）结束后延迟触发一次重绘，用于重建笔迹缓存
            _cacheRebuildTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(CacheRebuildDelayMs)
            };
            _cacheRebuildTimer.Tick += (s, e) =>
            {
                _cacheRebuildTimer!.Stop();
                InvalidateVisual();
            };
        }

        public void SetVideoImage(Image videoImage)
        {
            _videoImage = videoImage;
        }

        public void SetVideoFrame(OpenCvSharp.Mat? frame)
        {
            if (frame != null && !frame.Empty())
            {
                SetVideoSize(frame.Width, frame.Height);
            }
        }

        public void SetTransform(double zoom, Point pan, Point zoomBorderOffset)
        {
            if (Math.Abs(_currentZoom - zoom) > 0.001 || _currentPan != pan || _zoomBorderOffset != zoomBorderOffset)
            {
                _currentZoom = zoom;
                _currentPan = pan;
                _zoomBorderOffset = zoomBorderOffset;
                ScheduleInvalidate();
            }
        }

        /// <summary>
        /// 设置变换动画源（ZoomBorder 的子元素）。
        /// 摄像头画面通过该元素 RenderTransform 上的过渡动画平滑缩放/平移，
        /// 笔迹层在渲染时直接采样该属性的当前动画值，保证两个图层逐帧同步。
        /// </summary>
        public void SetTransformSource(Visual? source)
        {
            if (_transformSource == source) return;

            if (_transformSource != null && _transformSourceHandler != null)
            {
                _transformSource.PropertyChanged -= _transformSourceHandler;
            }
            _transformSource = source;

            if (source != null)
            {
                // 过渡动画每帧都会更新 RenderTransform，借此触发笔迹层重绘
                _transformSourceHandler = (s, e) =>
                {
                    if (e.Property == Visual.RenderTransformProperty)
                    {
                        InvalidateVisual();
                    }
                };
                source.PropertyChanged += _transformSourceHandler;
            }

            ScheduleInvalidate();
        }

        /// <summary>
        /// 采样变换源的当前（可能是动画中间态）矩阵；无有效源时回退到目标值。
        /// </summary>
        private (double zoom, Point pan) GetEffectiveRenderTransform()
        {
            if (_transformSource?.RenderTransform is { } transform)
            {
                var m = transform.Value;
                // 仅接受无旋转/倾斜且等比的缩放平移矩阵
                if (Math.Abs(m.M12) < 1e-3 && Math.Abs(m.M21) < 1e-3 &&
                    Math.Abs(m.M11 - m.M22) < 1e-3 && Math.Abs(m.M11) > 1e-3)
                {
                    return (m.M11, new Point(m.M31, m.M32));
                }
            }
            return (_currentZoom, _currentPan);
        }

        public void SetVideoSize(int width, int height)
        {
            if (width <= 0 || height <= 0) return;
            if (_videoWidth == width && _videoHeight == height) return;

            _videoWidth = width;
            _videoHeight = height;
        }

        public void SetPenMode()
        {
            IsPenMode = true;
            IsEraserMode = false;
            if (_videoImage != null)
            {
                _videoImage.Cursor = Cursor.Default;
            }
            IsHitTestVisible = true;
            Cursor = Cursor.Default;
            EraserCursorUpdate?.Invoke(default, 0, false);
        }

        public void SetEraserMode()
        {
            IsPenMode = false;
            IsEraserMode = true;
            if (_videoImage != null)
            {
                _videoImage.Cursor = Cursor.Default;
            }
            IsHitTestVisible = true;
            Cursor = Cursor.Default;
            EraserCursorUpdate?.Invoke(default, EraserSize, true);
        }

        public void SetMoveMode()
        {
            IsPenMode = false;
            IsEraserMode = false;
            if (_videoImage != null)
            {
                _videoImage.Cursor = Cursor.Default;
            }
            IsHitTestVisible = false;
            Cursor = Cursor.Default;
            EraserCursorUpdate?.Invoke(default, 0, false);
        }

        public void SetPenColor(SKColor color)
        {
            PenColor = color;
        }

        public void SetPenSize(int size)
        {
            PenSize = size;
        }

        public void SetPhotoMode(double photoWidth, double photoHeight)
        {
            _photoWidth = photoWidth;
            _photoHeight = photoHeight;
            _isPhotoMode = true;
            _inkCacheDirty = true; // 坐标映射变化，缓存需要重建
            _forceContentRender = true;
            IsHitTestVisible = IsPenMode || IsEraserMode;
        }

        public void ExitPhotoMode()
        {
            _isPhotoMode = false;
            _photoWidth = 0;
            _photoHeight = 0;
            _inkCacheDirty = true;
            _forceContentRender = true;
        }

        public void SetWhiteboardMode()
        {
            _isWhiteboardMode = true;
            _inkCacheDirty = true;
            _forceContentRender = true;
        }

        public void ExitWhiteboardMode()
        {
            _isWhiteboardMode = false;
            _whiteboardBackgroundPath = null;
            _whiteboardBackgroundBitmap?.Dispose();
            _whiteboardBackgroundBitmap = null;
            _forceContentRender = true;
            InvalidateVisual();
        }

        public void SetWhiteboardBackground(string? imagePath)
        {
            _whiteboardBackgroundPath = imagePath;
            _whiteboardBackgroundBitmap?.Dispose();
            _whiteboardBackgroundBitmap = null;

            if (!string.IsNullOrEmpty(imagePath) && System.IO.File.Exists(imagePath))
            {
                try
                {
                    using var stream = System.IO.File.OpenRead(imagePath);
                    _whiteboardBackgroundBitmap = SKBitmap.Decode(stream);
                }
                catch
                {
                    _whiteboardBackgroundBitmap = null;
                }
            }

            _forceContentRender = true; // 背景变化需要刷新显示位图
            InvalidateVisual();
        }

        public List<InkStroke> GetStrokes()
        {
            return new List<InkStroke>(_strokes);
        }

        public void SetStrokes(List<InkStroke> strokes)
        {
            _strokes.Clear();
            _strokes.AddRange(strokes);
            _inkCacheDirty = true;
            InvalidateVisual();
        }

        public void ClearStrokes()
        {
            _strokes.Clear();
            _inkCacheDirty = true;
            InvalidateVisual();
        }

        private SKPoint ScreenToVideo(Point screenPoint)
        {
            if (_isWhiteboardMode)
            {
                return new SKPoint((float)screenPoint.X, (float)screenPoint.Y);
            }

            // screenPoint 是相对于 InkCanvasOverlay 的坐标
            // 需要先减去 ZoomBorder 相对于 InkCanvasOverlay 的位置偏移，
            // 再减去 PAZ 的 Pan 偏移，最后除以 Zoom
            var videoX = (float)((screenPoint.X - _zoomBorderOffset.X - _currentPan.X) / _currentZoom);
            var videoY = (float)((screenPoint.Y - _zoomBorderOffset.Y - _currentPan.Y) / _currentZoom);
            return new SKPoint(videoX, videoY);
        }

        private static SKPoint ToSkPoint(Point point)
        {
            return new SKPoint((float)point.X, (float)point.Y);
        }

        private float GetRenderScaling()
        {
            return Math.Max(1.0f, (float)(TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0));
        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!IsPenMode && !IsEraserMode) return;

            var point = e.GetPosition(this);
            var pointerPoint = e.GetCurrentPoint(this);

            if (IsPenMode && pointerPoint.Properties.IsRightButtonPressed)
            {
                return;
            }

            if (TryHandlePalmEraser(e, point, isMoveEvent: false))
                return;

            _isDrawing = true;
            e.Pointer.Capture(this);

            var videoPoint = ScreenToVideo(point);
            var screenPoint = ToSkPoint(point);
            _currentVideoPoints = new List<SKPoint> { videoPoint };
            _currentIsEraser = IsEraserMode;
            _currentSize = IsEraserMode ? EraserSize : PenSize;
            _currentColor = PenColor;

            float zoomFactor = (_isWhiteboardMode) ? 1.0f : (float)_currentZoom;

            if (_currentIsEraser)
            {
                _lastEraserPoint = videoPoint;
                _hasLastEraserPoint = true;
                _tempStrokes = CloneStrokes(_strokes);
                float eraserWidthScreen = _currentSize * 1.6f;
                float eraserHeightScreen = _currentSize * 2.0f;
                var eraserRectVideo = GetEraserRectInVideo(videoPoint, eraserWidthScreen, eraserHeightScreen);
                ApplyEraserToPoint(videoPoint, eraserRectVideo);
            }
            else
            {
                _currentRatio = 0.5f;
                _currentScreenPoints = new List<SKPoint> { screenPoint };
                float widthScale = BasePenWidthScale / GetRenderScaling();
                _currentPointWidths = new List<float> { _currentSize * _currentRatio * widthScale };
                _currentZoomFactors = new List<float> { zoomFactor };
                _currentTimestamps = new List<long> { DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
            }
        }

        private void OnPointerMoved(object? sender, PointerEventArgs e)
        {
            if (!IsPenMode && !IsEraserMode) return;

            var currentPoint = e.GetPosition(this);
            var videoPoint = ScreenToVideo(currentPoint);

            if (IsEraserMode && !_isPalmEraserActive)
            {
                EraserCursorUpdate?.Invoke(currentPoint, EraserSize, true);
            }

            if (TryHandlePalmEraser(e, currentPoint, isMoveEvent: true))
                return;

            if (!_isDrawing || _currentVideoPoints == null) return;

            var lastVideoPoint = _currentVideoPoints[^1];
            _currentVideoPoints.Add(videoPoint);

            float zoomFactor = (_isWhiteboardMode) ? 1.0f : (float)_currentZoom;

            if (_currentIsEraser)
            {
                if (_hasLastEraserPoint)
                {
                    float eraserWidthScreen = _currentSize * 1.6f;
                    float eraserHeightScreen = _currentSize * 2.0f;
                    var eraserRectVideo = GetEraserRectInVideo(videoPoint, eraserWidthScreen, eraserHeightScreen);
                    ApplyEraserToSegment(_lastEraserPoint, videoPoint, eraserRectVideo);
                }
                _lastEraserPoint = videoPoint;
                _hasLastEraserPoint = true;
            }
            else
            {
                var currentTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                _currentTimestamps!.Add(currentTime);
                var currentScreenPoint = ToSkPoint(currentPoint);
                var lastScreenPoint = _currentScreenPoints![^1];
                _currentScreenPoints.Add(currentScreenPoint);

                float screenDistance = CalculateDistance(lastScreenPoint, currentScreenPoint);

                UpdateRatioBySpeed(screenDistance);

                float widthScale = BasePenWidthScale / GetRenderScaling();
                _currentPointWidths!.Add(_currentSize * _currentRatio * widthScale);
                _currentZoomFactors!.Add(zoomFactor);
            }

            InvalidateVisual();
        }

        private float CalculateDistance(SKPoint from, SKPoint to)
        {
            float dx = to.X - from.X;
            float dy = to.Y - from.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        private void UpdateRatioBySpeed(float screenDistance)
        {
            var settings = PenSettings;

            if (screenDistance > settings.SpeedThresholdFast)
            {
                if (_currentRatio > settings.RatioMin)
                    _currentRatio *= settings.RatioChangeCoefficient;
            }
            else if (screenDistance < settings.SpeedThresholdSlow)
            {
                if (_currentRatio < settings.RatioMax)
                    _currentRatio *= (1 + (1 - settings.RatioChangeCoefficient));
            }
            else
            {
                if (_currentRatio > 1f)
                    _currentRatio *= settings.RatioChangeCoefficient;
                else
                    _currentRatio *= (1 + (1 - settings.RatioChangeCoefficient) / 2);
            }

            _currentRatio = Math.Clamp(_currentRatio, settings.RatioMin, settings.RatioMax);
        }

        private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (_isPalmEraserActive)
            {
                if (_tempStrokes != null)
                {
                    _strokes.Clear();
                    _strokes.AddRange(_tempStrokes);
                    _inkCacheDirty = true;
                }
                DeactivatePalmEraser();
            }

            ResetPalmDetectionState();

            if (!_isDrawing) return;

            if (_currentVideoPoints != null && _currentVideoPoints.Count > 1)
            {
                if (_currentIsEraser && _tempStrokes != null)
                {
                    _strokes.Clear();
                    _strokes.AddRange(_tempStrokes);
                    _inkCacheDirty = true;
                }
                else
                {
                    CommitCurrentStroke();
                }
            }

            ResetCurrentInteraction();
            InvalidateVisual();
            e.Pointer.Capture(null);

            if (IsEraserMode && EraserCursorUpdate != null)
            {
                var pos = e.GetPosition(this);
                EraserCursorUpdate(pos, EraserSize, true);
            }
        }

        private void ApplyInkStyle(List<float> widths, float baseWidth)
        {
            if (widths.Count < 2) return;

            int n = widths.Count - 1;
            float minPressure = 0.2f;
            int taperLength = Math.Min(widths.Count / 5, 12);

            if (n >= taperLength * 2)
            {
                for (int i = 0; i < taperLength; i++)
                {
                    float factor = (float)i / taperLength;
                    float pressure = minPressure + (1.0f - minPressure) * factor;
                    float targetWidth = baseWidth * pressure;
                    widths[i] = widths[i] * factor + targetWidth * (1 - factor);
                }

                for (int i = 0; i < taperLength; i++)
                {
                    float factor = (float)i / taperLength;
                    float pressure = minPressure + (1.0f - minPressure) * factor;
                    float targetWidth = baseWidth * pressure;
                    int idx = n - i;
                    widths[idx] = widths[idx] * factor + targetWidth * (1 - factor);
                }
            }
            else
            {
                for (int i = 0; i <= n; i++)
                {
                    float startFactor = (float)i / n;
                    float endFactor = (float)(n - i) / n;
                    float positionFactor = Math.Min(startFactor, endFactor) * 2.0f;
                    float pressure = minPressure + (1.0f - minPressure) * Math.Min(positionFactor, 1.0f);
                    float targetWidth = baseWidth * pressure;
                    float blendFactor = 1.0f - Math.Min(positionFactor, 1.0f);
                    widths[i] = widths[i] * (1 - blendFactor) + targetWidth * blendFactor;
                }
            }
        }

        private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        {
            if (_isDrawing && _currentVideoPoints != null && _currentVideoPoints.Count > 1)
            {
                if (_currentIsEraser && _tempStrokes != null)
                {
                    _strokes.Clear();
                    _strokes.AddRange(_tempStrokes);
                    _inkCacheDirty = true;
                }
                else
                {
                    CommitCurrentStroke();
                }
            }

            ResetCurrentInteraction();
            InvalidateVisual();
        }

        private void CommitCurrentStroke()
        {
            if (_currentVideoPoints == null ||
                _currentPointWidths == null ||
                _currentZoomFactors == null ||
                _currentVideoPoints.Count <= 1 ||
                _currentPointWidths.Count != _currentVideoPoints.Count)
            {
                return;
            }

            ApplyInkStyle(_currentPointWidths, _currentSize);
            var videoWidths = ConvertScreenWidthsToVideo(_currentPointWidths, _currentZoomFactors);
            float lastZoomFactor = _currentZoomFactors.Count > 0 ? _currentZoomFactors[^1] : 1.0f;
            float widthScale = BasePenWidthScale / GetRenderScaling();

            var stroke = new InkStroke
            {
                VideoPoints = new List<SKPoint>(_currentVideoPoints),
                PointWidths = videoWidths,
                IsEraser = false,
                Size = (_currentSize * widthScale) / Math.Max(lastZoomFactor, 0.001f),
                Color = _currentColor
            };

            _strokes.Add(stroke);
            TryAppendStrokeToCache(stroke);
            _forceContentRender = true; // 已提交笔迹需要刷新进显示位图
        }

        private List<float> ConvertScreenWidthsToVideo(List<float> screenWidths, List<float> zoomFactors)
        {
            var videoWidths = new List<float>(screenWidths.Count);

            for (int i = 0; i < screenWidths.Count; i++)
            {
                float zoomFactor = i < zoomFactors.Count ? zoomFactors[i] : 1.0f;
                videoWidths.Add(screenWidths[i] / Math.Max(zoomFactor, 0.001f));
            }

            return videoWidths;
        }

        private void ResetCurrentInteraction()
        {
            _currentVideoPoints = null;
            _currentScreenPoints = null;
            _currentPointWidths = null;
            _currentZoomFactors = null;
            _currentTimestamps = null;
            _tempStrokes = null;
            _hasLastEraserPoint = false;
            _isDrawing = false;
        }

        private List<InkStroke> CloneStrokes(List<InkStroke> source)
        {
            var result = new List<InkStroke>();
            foreach (var stroke in source)
            {
                result.Add(new InkStroke
                {
                    VideoPoints = new List<SKPoint>(stroke.VideoPoints ?? new List<SKPoint>()),
                    PointWidths = stroke.PointWidths != null ? new List<float>(stroke.PointWidths) : null,
                    IsEraser = stroke.IsEraser,
                    Size = stroke.Size,
                    Color = stroke.Color
                });
            }
            return result;
        }

        private SKRect GetEraserRectInVideo(SKPoint videoCenter, float eraserWidthScreen, float eraserHeightScreen)
        {
            float zoom = (_isWhiteboardMode) ? 1.0f : (float)_currentZoom;
            float halfWidthVideo = (eraserWidthScreen / 2) / zoom;
            float halfHeightVideo = (eraserHeightScreen / 2) / zoom;
            return new SKRect(
                videoCenter.X - halfWidthVideo,
                videoCenter.Y - halfHeightVideo,
                videoCenter.X + halfWidthVideo,
                videoCenter.Y + halfHeightVideo);
        }

        private void ApplyEraserToPoint(SKPoint eraserCenter, SKRect eraserRectVideo)
        {
            if (_tempStrokes == null) return;

            var newStrokes = new List<InkStroke>();

            foreach (var stroke in _tempStrokes)
            {
                if (stroke.VideoPoints == null || stroke.VideoPoints.Count < 2)
                {
                    newStrokes.Add(stroke);
                    continue;
                }

                var segments = EraseStrokeWithRectangle(stroke, eraserRectVideo);
                newStrokes.AddRange(segments);
            }

            _tempStrokes = newStrokes;
        }

        private void ApplyEraserToSegment(SKPoint from, SKPoint to, SKRect eraserRectVideo)
        {
            if (_tempStrokes == null) return;

            // 沿拖动路径按橡皮尺寸的一半步进采样，保证覆盖路径的同时避免过密的重复擦除
            float step = Math.Max(1f, Math.Min(eraserRectVideo.Width, eraserRectVideo.Height) * 0.5f);

            float dx = to.X - from.X;
            float dy = to.Y - from.Y;
            float length = (float)Math.Sqrt(dx * dx + dy * dy);

            if (length < step)
            {
                ApplyEraserToPoint(to, eraserRectVideo);
                return;
            }

            int steps = (int)Math.Ceiling(length / step);
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                SKPoint point = new SKPoint(
                    from.X + dx * t,
                    from.Y + dy * t
                );
                ApplyEraserToPoint(point, eraserRectVideo);
            }
        }

        private List<InkStroke> EraseStrokeWithRectangle(InkStroke stroke, SKRect rectVideo)
        {
            var result = new List<InkStroke>();
            if (stroke.VideoPoints == null)
            {
                return result;
            }

            var videoPoints = stroke.VideoPoints;
            var currentSegment = new List<SKPoint>();
            var currentWidths = new List<float>();
            var hasWidths = stroke.PointWidths != null && stroke.PointWidths.Count == videoPoints.Count;

            float GetWidth(int i) => hasWidths ? stroke.PointWidths![i] : stroke.Size;

            // 按线段几何擦除：对每条相邻点组成的线段与橡皮矩形求交，在交点处精确切断。
            // 切口优先用未膨胀的橡皮框与笔画中心线求交，保证实际擦除范围严格等于橡皮框
            // 大小（屏幕固定，不随画布缩放变化）；仅当橡皮框未碰到中心线（只擦到笔画边缘）
            // 时，才用按笔画半径膨胀的矩形兜底，避免粗笔画擦不掉。
            for (int i = 0; i < videoPoints.Count - 1; i++)
            {
                var a = videoPoints[i];
                var b = videoPoints[i + 1];
                float widthA = GetWidth(i);
                float widthB = GetWidth(i + 1);

                bool hit = ClipSegmentToRect(a, b, rectVideo, 0f, out float t0, out float t1)
                           || ClipSegmentToRect(a, b, rectVideo, (widthA + widthB) / 4, out t0, out t1);

                if (!hit)
                {
                    // 整段保留
                    if (currentSegment.Count == 0)
                    {
                        currentSegment.Add(a);
                        currentWidths.Add(widthA);
                    }
                    currentSegment.Add(b);
                    currentWidths.Add(widthB);
                }
                else
                {
                    if (t0 > 0)
                    {
                        // 保留擦除入口之前的部分（在入口处切断）
                        if (currentSegment.Count == 0)
                        {
                            currentSegment.Add(a);
                            currentWidths.Add(widthA);
                        }
                        currentSegment.Add(LerpPoint(a, b, t0));
                        currentWidths.Add(Lerp(widthA, widthB, t0));
                        FlushSegment(result, currentSegment, currentWidths, stroke);
                    }
                    else
                    {
                        FlushSegment(result, currentSegment, currentWidths, stroke);
                    }

                    if (t1 < 1)
                    {
                        // 从擦除出口处重新开始一段
                        currentSegment.Add(LerpPoint(a, b, t1));
                        currentWidths.Add(Lerp(widthA, widthB, t1));
                        currentSegment.Add(b);
                        currentWidths.Add(widthB);
                    }
                }
            }

            FlushSegment(result, currentSegment, currentWidths, stroke);

            return result;
        }

        private void FlushSegment(List<InkStroke> result, List<SKPoint> points, List<float> widths, InkStroke source)
        {
            if (points.Count >= 2)
            {
                result.Add(CreateStrokeSegment(points, widths, source));
            }
            points.Clear();
            widths.Clear();
        }

        private static SKPoint LerpPoint(SKPoint a, SKPoint b, float t)
        {
            return new SKPoint(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
        }

        private static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }

        /// <summary>
        /// 计算线段 a->b（按笔画半径膨胀后）与矩形的相交参数区间 [t0, t1]。
        /// 未相交时返回 false。
        /// </summary>
        private static bool ClipSegmentToRect(SKPoint a, SKPoint b, SKRect rect, float radius, out float t0, out float t1)
        {
            t0 = 0f;
            t1 = 1f;

            // 将矩形按笔画半径膨胀，使线段中心线的相交近似等价于笔画实体与橡皮相交
            var grown = new SKRect(rect.Left - radius, rect.Top - radius, rect.Right + radius, rect.Bottom + radius);

            float dx = b.X - a.X;
            float dy = b.Y - a.Y;

            if (Math.Abs(dx) < 1e-6f)
            {
                if (a.X < grown.Left || a.X > grown.Right) return false;
            }
            else
            {
                float inv = 1f / dx;
                float ta = (grown.Left - a.X) * inv;
                float tb = (grown.Right - a.X) * inv;
                if (ta > tb) (ta, tb) = (tb, ta);
                t0 = Math.Max(t0, ta);
                t1 = Math.Min(t1, tb);
                if (t0 > t1) return false;
            }

            if (Math.Abs(dy) < 1e-6f)
            {
                if (a.Y < grown.Top || a.Y > grown.Bottom) return false;
            }
            else
            {
                float inv = 1f / dy;
                float ta = (grown.Top - a.Y) * inv;
                float tb = (grown.Bottom - a.Y) * inv;
                if (ta > tb) (ta, tb) = (tb, ta);
                t0 = Math.Max(t0, ta);
                t1 = Math.Min(t1, tb);
                if (t0 > t1) return false;
            }

            return true;
        }

        private InkStroke CreateStrokeSegment(List<SKPoint> points, List<float> widths, InkStroke source)
        {
            return new InkStroke
            {
                VideoPoints = new List<SKPoint>(points),
                PointWidths = new List<float>(widths),
                IsEraser = source.IsEraser,
                Size = source.Size,
                Color = source.Color
            };
        }

        private SKPaint CreatePenPaint(SKColor color, float size)
        {
            return new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round,
                IsAntialias = true,
                Color = color,
                StrokeWidth = size
            };
        }

        private SKPaint CreateEraserPaint(float size)
        {
            return new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round,
                IsAntialias = true,
                BlendMode = SKBlendMode.Clear,
                StrokeWidth = size
            };
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            // 渲染前采样变换源的当前动画值，使笔迹与摄像头画面的缩放/平移动画逐帧同步
            var effectiveTransform = GetEffectiveRenderTransform();
            _renderZoom = effectiveTransform.zoom;
            _renderPan = effectiveTransform.pan;

            var displayWidth = (int)Bounds.Width;
            var displayHeight = (int)Bounds.Height;
            var renderScaling = GetRenderScaling();
            var pixelWidth = Math.Max(1, (int)Math.Ceiling(displayWidth * renderScaling));
            var pixelHeight = Math.Max(1, (int)Math.Ceiling(displayHeight * renderScaling));

            if (displayWidth <= 0 || displayHeight <= 0) return;

            if (_displayBitmap == null || _displayBitmap.PixelSize.Width != pixelWidth || _displayBitmap.PixelSize.Height != pixelHeight)
            {
                _displayBitmap?.Dispose();
                _displayBitmap = new WriteableBitmap(
                    new PixelSize(pixelWidth, pixelHeight),
                    new Vector(96 * renderScaling, 96 * renderScaling),
                    PixelFormat.Bgra8888,
                    AlphaFormat.Premul);
            }

            // 笔迹层的有效变换（白板模式下不参与 ZoomBorder 变换，恒为恒等）
            double effZoom;
            Point effOffset;
            if (_isWhiteboardMode)
            {
                effZoom = 1.0;
                effOffset = new Point(0, 0);
            }
            else
            {
                effZoom = _renderZoom;
                effOffset = new Point(_renderPan.X + _zoomBorderOffset.X, _renderPan.Y + _zoomBorderOffset.Y);
            }

            bool isErasing = _tempStrokes != null && (_isPalmEraserActive || (_isDrawing && _currentIsEraser));

            // 是否需要重写 _displayBitmap 内容（CPU 光栅化）。
            // 纯变换帧（拖拽/缩放、无书写）跳过 CPU 光栅化，仅用 GPU 搬运已有内容，
            // 保证笔迹与视频画面在同一次合成中上屏，避免两层一快一慢。
            bool contentFrame = _forceContentRender
                || double.IsNaN(_displayZoom)
                || isErasing
                || (_isDrawing && !_currentIsEraser);

            if (!isErasing)
            {
                EnsureInkCache(pixelWidth, pixelHeight);

                bool cacheTransformMatches =
                    !_inkCacheDirty &&
                    Math.Abs(_cacheZoom - effZoom) < 0.001 &&
                    Math.Abs(_cacheOffset.X - effOffset.X) < 0.5 &&
                    Math.Abs(_cacheOffset.Y - effOffset.Y) < 0.5;

                if (!cacheTransformMatches)
                {
                    var now = DateTime.UtcNow;
                    bool changedSinceLastRender =
                        double.IsNaN(_prevRenderZoom) ||
                        Math.Abs(_prevRenderZoom - effZoom) >= 0.001 ||
                        Math.Abs(_prevRenderOffsetX - effOffset.X) >= 0.5 ||
                        Math.Abs(_prevRenderOffsetY - effOffset.Y) >= 0.5;
                    if (changedSinceLastRender)
                    {
                        _lastTransformChangeUtc = now;
                    }

                    // 变换稳定后（或缩放漂移过大时）重建缓存以恢复清晰度；进行中则等下一帧
                    bool stable = !changedSinceLastRender ||
                        (now - _lastTransformChangeUtc).TotalMilliseconds >= CacheRebuildDelayMs;
                    bool needsRebuild = _inkCacheDirty ||
                        Math.Abs(effZoom / Math.Max(_cacheZoom, 0.001) - 1.0) > CacheRebuildScaleDrift;

                    if (stable || needsRebuild)
                    {
                        RebuildInkCache(_strokes, effZoom, effOffset, pixelWidth, pixelHeight, renderScaling);
                        contentFrame = true; // 缓存已更新，刷新显示内容
                    }
                    else
                    {
                        ArmCacheRebuildTimer();
                    }
                }

                _prevRenderZoom = effZoom;
                _prevRenderOffsetX = effOffset.X;
                _prevRenderOffsetY = effOffset.Y;
            }

            if (contentFrame)
            {
                using (var fb = _displayBitmap.Lock())
                {
                    var info = new SKImageInfo(fb.Size.Width, fb.Size.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
                    using var surface = SKSurface.Create(info, fb.Address, fb.RowBytes);
                    var canvas = surface.Canvas;
                    canvas.Clear(SKColors.Transparent);
                    canvas.Save();
                    canvas.Scale(renderScaling, renderScaling);

                    if (_isWhiteboardMode && _whiteboardBackgroundBitmap != null)
                    {
                        var bounds = Bounds;

                        var imgWidth = _whiteboardBackgroundBitmap.Width;
                        var imgHeight = _whiteboardBackgroundBitmap.Height;

                        var scaleX = (float)bounds.Width / imgWidth;
                        var scaleY = (float)bounds.Height / imgHeight;
                        var scale = Math.Min(scaleX, scaleY);

                        var destWidth = imgWidth * scale;
                        var destHeight = imgHeight * scale;
                        var destX = ((float)bounds.Width - destWidth) / 2;
                        var destY = ((float)bounds.Height - destHeight) / 2;

                        var destRect = new SKRect(destX, destY, destX + destWidth, destY + destHeight);
                        canvas.DrawBitmap(_whiteboardBackgroundBitmap, destRect);
                    }

                    if (isErasing)
                    {
                        // 擦除期间按固定间隔用临时笔迹重建缓存，其余帧直接搬运缓存位图
                        if (_inkCache == null || _inkCache.Width != pixelWidth || _inkCache.Height != pixelHeight ||
                            (DateTime.UtcNow - _lastEraseRebuildUtc).TotalMilliseconds >= EraseRebuildIntervalMs)
                        {
                            RebuildInkCache(_tempStrokes!, effZoom, effOffset, pixelWidth, pixelHeight, renderScaling);
                            _lastEraseRebuildUtc = DateTime.UtcNow;
                        }
                    }

                    BlitInkCache(canvas, effZoom, effOffset);

                    if (_isDrawing && !_currentIsEraser)
                    {
                        RenderCurrentWetStroke(canvas);
                    }
                    canvas.Restore();
                }

                _displayZoom = effZoom;
                _displayOffset = effOffset;
                _forceContentRender = false;

                context.DrawImage(_displayBitmap, new Rect(_displayBitmap.Size), Bounds);
            }
            else
            {
                // 纯变换帧：按显示内容变换与当前变换的相对关系做 GPU 搬运，零 CPU 光栅化。
                // 屏幕位置 = effOffset + k * (displayPixel - displayOffset)，k = effZoom / displayZoom
                double k = effZoom / Math.Max(_displayZoom, 0.001);
                bool identity =
                    Math.Abs(k - 1.0) < 0.001 &&
                    Math.Abs(_displayOffset.X - effOffset.X) < 0.5 &&
                    Math.Abs(_displayOffset.Y - effOffset.Y) < 0.5;

                if (identity)
                {
                    context.DrawImage(_displayBitmap, new Rect(_displayBitmap.Size), Bounds);
                }
                else
                {
                    var x = effOffset.X - k * _displayOffset.X;
                    var y = effOffset.Y - k * _displayOffset.Y;
                    context.DrawImage(
                        _displayBitmap,
                        new Rect(_displayBitmap.Size),
                        new Rect(x, y, Bounds.Width * k, Bounds.Height * k));
                }
            }
        }

        /// <summary>确保笔迹缓存位图已按当前显示尺寸分配。</summary>
        private void EnsureInkCache(int pixelWidth, int pixelHeight)
        {
            if (_inkCache != null && _inkCache.Width == pixelWidth && _inkCache.Height == pixelHeight) return;

            _inkCache?.Dispose();
            _inkCache = new SKBitmap(new SKImageInfo(pixelWidth, pixelHeight, SKColorType.Bgra8888, SKAlphaType.Premul));
            _inkCacheDirty = true;
        }

        /// <summary>用指定笔迹集合和变换整体重建笔迹缓存（仅在笔迹变化或变换稳定后调用）。</summary>
        private void RebuildInkCache(List<InkStroke> strokes, double effZoom, Point effOffset, int pixelWidth, int pixelHeight, float renderScaling)
        {
            EnsureInkCache(pixelWidth, pixelHeight);

            var info = new SKImageInfo(pixelWidth, pixelHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info, _inkCache!.GetPixels(), _inkCache.RowBytes);
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);
            canvas.Scale(renderScaling, renderScaling);
            RenderStrokes(canvas, strokes, (float)effZoom, effOffset.X, effOffset.Y, renderScaling);

            _cacheZoom = effZoom;
            _cacheOffset = effOffset;
            _inkCacheDirty = false;
        }

        /// <summary>把缓存位图搬运到显示画布；变换不一致时按相对变换缩放/平移搬运。</summary>
        private void BlitInkCache(SKCanvas canvas, double effZoom, Point effOffset)
        {
            if (_inkCache == null) return;

            var destRect = new SKRect(0, 0, (float)Bounds.Width, (float)Bounds.Height);

            bool matches =
                Math.Abs(_cacheZoom - effZoom) < 0.001 &&
                Math.Abs(_cacheOffset.X - effOffset.X) < 0.5 &&
                Math.Abs(_cacheOffset.Y - effOffset.Y) < 0.5;

            if (matches)
            {
                canvas.DrawBitmap(_inkCache, destRect);
                return;
            }

            // 缩放/拖拽进行中：p' = effOffset + k * (p - cacheOffset)，k = effZoom / cacheZoom
            float scale = (float)(effZoom / Math.Max(_cacheZoom, 0.001));
            canvas.Save();
            canvas.Translate((float)effOffset.X, (float)effOffset.Y);
            canvas.Scale(scale, scale);
            canvas.Translate((float)-_cacheOffset.X, (float)-_cacheOffset.Y);
            canvas.DrawBitmap(_inkCache, destRect);
            canvas.Restore();
        }

        private void ArmCacheRebuildTimer()
        {
            if (_cacheRebuildTimer == null) return;
            _cacheRebuildTimer.Stop();
            _cacheRebuildTimer.Start();
        }

        /// <summary>
        /// 笔迹提交时尝试把它增量绘制进缓存，避免每次落笔都整体重建。
        /// 缓存失效或变换已变化时退回标脏，由下一次渲染整体重建。
        /// </summary>
        private void TryAppendStrokeToCache(InkStroke stroke)
        {
            float renderScaling = GetRenderScaling();
            int pixelWidth = Math.Max(1, (int)Math.Ceiling((int)Bounds.Width * renderScaling));
            int pixelHeight = Math.Max(1, (int)Math.Ceiling((int)Bounds.Height * renderScaling));

            if (_inkCache == null || _inkCacheDirty ||
                _inkCache.Width != pixelWidth || _inkCache.Height != pixelHeight)
            {
                _inkCacheDirty = true;
                return;
            }

            double effZoom;
            Point effOffset;
            if (_isWhiteboardMode)
            {
                effZoom = 1.0;
                effOffset = new Point(0, 0);
            }
            else
            {
                var eff = GetEffectiveRenderTransform();
                effZoom = eff.zoom;
                effOffset = new Point(eff.pan.X + _zoomBorderOffset.X, eff.pan.Y + _zoomBorderOffset.Y);
            }

            if (Math.Abs(_cacheZoom - effZoom) >= 0.001 ||
                Math.Abs(_cacheOffset.X - effOffset.X) >= 0.5 ||
                Math.Abs(_cacheOffset.Y - effOffset.Y) >= 0.5)
            {
                _inkCacheDirty = true;
                return;
            }

            var info = new SKImageInfo(pixelWidth, pixelHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info, _inkCache.GetPixels(), _inkCache.RowBytes);
            var canvas = surface.Canvas;
            canvas.Scale(renderScaling, renderScaling);
            RenderStroke(canvas, stroke, (float)effZoom, effOffset.X, effOffset.Y, renderScaling);
        }

        private void RenderStrokes(SKCanvas canvas, List<InkStroke> strokes, float zoom, double offsetX, double offsetY, float renderScaling)
        {
            foreach (var stroke in strokes)
            {
                RenderStroke(canvas, stroke, zoom, offsetX, offsetY, renderScaling);
            }
        }

        private SKPoint VideoToScreenLocal(SKPoint videoPoint, float zoom, double offsetX, double offsetY)
        {
            if (_isWhiteboardMode)
            {
                return videoPoint;
            }

            return new SKPoint(
                videoPoint.X * zoom + (float)offsetX,
                videoPoint.Y * zoom + (float)offsetY);
        }

        private void RenderStroke(SKCanvas canvas, InkStroke stroke, float zoom, double offsetX, double offsetY, float renderScaling)
        {
            if (stroke.VideoPoints == null || stroke.VideoPoints.Count < 2) return;

            if (stroke.PointWidths != null && stroke.PointWidths.Count == stroke.VideoPoints.Count)
            {
                RenderVariableWidthStroke(canvas, stroke, zoom, offsetX, offsetY, renderScaling);
            }
            else
            {
                var displaySize = stroke.Size * zoom;
                using var paint = CreatePenPaint(stroke.Color, displaySize);

                for (int i = 1; i < stroke.VideoPoints.Count; i++)
                {
                    var screenFrom = VideoToScreenLocal(stroke.VideoPoints[i - 1], zoom, offsetX, offsetY);
                    var screenTo = VideoToScreenLocal(stroke.VideoPoints[i], zoom, offsetX, offsetY);
                    canvas.DrawLine(
                        new SKPoint(screenFrom.X / renderScaling, screenFrom.Y / renderScaling),
                        new SKPoint(screenTo.X / renderScaling, screenTo.Y / renderScaling),
                        paint);
                }
            }
        }

        private void RenderVariableWidthStroke(SKCanvas canvas, InkStroke stroke, float zoom, double offsetX, double offsetY, float renderScaling)
        {
            if (stroke.VideoPoints == null || stroke.PointWidths == null) return;

            using var paint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                IsAntialias = true,
                Color = stroke.Color
            };

            for (int i = 0; i < stroke.VideoPoints.Count - 1; i++)
            {
                var p1 = VideoToScreenLocal(stroke.VideoPoints[i], zoom, offsetX, offsetY);
                var p2 = VideoToScreenLocal(stroke.VideoPoints[i + 1], zoom, offsetX, offsetY);
                var w1 = stroke.PointWidths[i] * zoom;
                var w2 = stroke.PointWidths[i + 1] * zoom;

                RenderSmoothSegment(canvas,
                    new SKPoint(p1.X / renderScaling, p1.Y / renderScaling),
                    new SKPoint(p2.X / renderScaling, p2.Y / renderScaling),
                    w1, w2, paint);
            }
        }

        private void RenderCurrentWetStroke(SKCanvas canvas)
        {
            if (_currentScreenPoints == null ||
                _currentPointWidths == null ||
                _currentScreenPoints.Count < 2 ||
                _currentPointWidths.Count != _currentScreenPoints.Count)
            {
                return;
            }

            float renderScaling = GetRenderScaling();

            using var paint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                IsAntialias = true,
                Color = _currentColor
            };

            for (int i = 0; i < _currentScreenPoints.Count - 1; i++)
            {
                var p1 = _currentScreenPoints[i];
                var p2 = _currentScreenPoints[i + 1];
                RenderSmoothSegment(
                    canvas,
                    new SKPoint(p1.X / renderScaling, p1.Y / renderScaling),
                    new SKPoint(p2.X / renderScaling, p2.Y / renderScaling),
                    _currentPointWidths[i],
                    _currentPointWidths[i + 1],
                    paint);
            }
        }

        private void RenderSmoothSegment(SKCanvas canvas, SKPoint p1, SKPoint p2, float w1, float w2, SKPaint paint)
        {
            float dx = p2.X - p1.X;
            float dy = p2.Y - p1.Y;
            float length = (float)Math.Sqrt(dx * dx + dy * dy);

            if (length < 0.5f)
            {
                canvas.DrawCircle(p1, w1 / 2, paint);
                return;
            }

            // 按笔画宽度的 1/4 步进采样即可保证平滑，大幅减少粗笔画的绘制调用数
            float step = Math.Max(1f, Math.Min(w1, w2) * 0.25f);
            int subdivisions = Math.Max(2, (int)(length / step));

            for (int i = 0; i <= subdivisions; i++)
            {
                float t = i / (float)subdivisions;
                float x = p1.X + dx * t;
                float y = p1.Y + dy * t;
                float w = w1 + (w2 - w1) * t;

                canvas.DrawCircle(new SKPoint(x, y), w / 2, paint);
            }
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            return availableSize;
        }

        public void ClearAll()
        {
            _strokes.Clear();
            ResetCurrentInteraction();
            _inkCacheDirty = true;
            InvalidateVisual();
        }

        public void Undo()
        {
            if (_strokes.Count == 0) return;

            _strokes.RemoveAt(_strokes.Count - 1);
            _inkCacheDirty = true;
            InvalidateVisual();
        }

        public int StrokeCount => _strokes.Count;

        private double CalculateTouchArea(PointerEventArgs e)
        {
            try
            {
                var pointerPoint = e.GetCurrentPoint(this);
                var bounds = pointerPoint.Properties.ContactRect;
                var width = Math.Abs(bounds.Width);
                var height = Math.Abs(bounds.Height);
                if (width <= 0 || height <= 0)
                {
                    return 0;
                }

                // 电容屏用面积，红外屏更适合用等效边长（sqrt(area)）避免误放大
                double metric = PenSettings.IsInfraredScreen
                    ? Math.Sqrt(width * height)
                    : width * height;

                var multiplier = PenSettings.PalmTouchMultiplier;
                if (multiplier > 0)
                {
                    metric *= multiplier;
                }

                return metric;
            }
            catch
            {
                return 0;
            }
        }

        private bool TryHandlePalmEraser(PointerEventArgs e, Point screenPoint, bool isMoveEvent)
        {
            if (!EnablePalmEraser || !IsPenMode)
                return false;

            // 状态锁：激活后允许短时抖动，防止在手掌擦和书写之间来回抖动。
            var touchMetric = CalculateTouchArea(e);
            _currentTouchArea = touchMetric;
            var nowUtc = DateTime.UtcNow;
            var isPalmCandidate = touchMetric > PalmEraserThreshold;

            if (isPalmCandidate)
            {
                _palmActivationHitCount++;
                _palmReleaseHitCount = 0;
                _lastPalmHitTimeUtc = nowUtc;
            }
            else
            {
                _palmActivationHitCount = 0;
                _palmReleaseHitCount++;
            }

            int activationSamples = Math.Max(1, PenSettings.PalmActivationSamples);
            int releaseSamples = Math.Max(1, PenSettings.PalmReleaseSamples);

            if (!_isPalmEraserActive)
            {
                if (_palmActivationHitCount >= activationSamples)
                {
                    ActivatePalmEraser(screenPoint);
                }
                else
                {
                    return false;
                }
            }

            var elapsedSinceLastPalmHit = (nowUtc - _lastPalmHitTimeUtc).TotalMilliseconds;
            if (!isPalmCandidate && _palmReleaseHitCount >= releaseSamples && elapsedSinceLastPalmHit > PalmReleaseDebounceMs)
            {
                DeactivatePalmEraser();
                return false;
            }

            if (_isPalmEraserActive)
            {
                var videoPoint = ScreenToVideo(screenPoint);
                float eraserSize = CalculatePalmEraserSize(touchMetric);
                ApplyEraserAtPoint(videoPoint, eraserSize);
                EraserCursorUpdate?.Invoke(screenPoint, eraserSize, true);
                if (isMoveEvent)
                {
                    InvalidateVisual();
                }
                return true;
            }

            return false;
        }

        private void ActivatePalmEraser(Point screenPoint)
        {
            if (_isPalmEraserActive) return;

            _lastModeBeforePalmEraser = IsPenMode;
            _isPalmEraserActive = true;
            _isDrawing = false;
            _currentVideoPoints = null;
            _currentScreenPoints = null;
            _currentPointWidths = null;
            _currentZoomFactors = null;
            _currentTimestamps = null;
            _palmReleaseHitCount = 0;
        }

        private void DeactivatePalmEraser()
        {
            if (!_isPalmEraserActive) return;

            _isPalmEraserActive = false;
            EraserCursorUpdate?.Invoke(default, 0, false);
            InvalidateVisual();
        }

        private void ResetPalmDetectionState()
        {
            _palmActivationHitCount = 0;
            _palmReleaseHitCount = 0;
            _lastPalmHitTimeUtc = DateTime.MinValue;
        }

        private void ScheduleInvalidate()
        {
            if (_invalidateScheduled) return;
            _invalidateScheduled = true;
            Dispatcher.UIThread.Post(() =>
            {
                _invalidateScheduled = false;
                InvalidateVisual();
            });
        }

        private float CalculatePalmEraserSize(double touchArea)
        {
            double baseSize = Math.Sqrt(touchArea) * 0.1;
            return Math.Max(20, (float)baseSize);
        }

        private void ApplyEraserAtPoint(SKPoint videoPoint, float eraserSize)
        {
            if (_strokes.Count == 0) return;

            if (_tempStrokes == null)
            {
                _tempStrokes = CloneStrokes(_strokes);
            }

            float eraserWidthScreen = eraserSize * 1.6f;
            float eraserHeightScreen = eraserSize * 2.0f;
            var eraserRectVideo = GetEraserRectInVideo(videoPoint, eraserWidthScreen, eraserHeightScreen);

            var newStrokes = new List<InkStroke>();
            foreach (var stroke in _tempStrokes)
            {
                if (stroke.VideoPoints == null || stroke.VideoPoints.Count < 2)
                {
                    newStrokes.Add(stroke);
                    continue;
                }

                var segments = EraseStrokeWithRectangle(stroke, eraserRectVideo);
                newStrokes.AddRange(segments);
            }

            _tempStrokes = newStrokes;
        }

        public void Dispose()
        {
            if (_transformSource != null && _transformSourceHandler != null)
            {
                _transformSource.PropertyChanged -= _transformSourceHandler;
            }
            _cacheRebuildTimer?.Stop();
            _inkCache?.Dispose();
            _inkCache = null;
            _displayBitmap?.Dispose();
        }
    }

    public class InkStroke
    {
        public List<SKPoint>? VideoPoints { get; set; }
        public List<float>? PointWidths { get; set; }
        public bool IsEraser { get; set; }
        public float Size { get; set; }
        public SKColor Color { get; set; }
    }
}
