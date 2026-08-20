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

        private bool _isDrawing;
        private bool _invalidateScheduled = false;

        private Image? _videoImage;

        private double _currentZoom = 1.0;
        private Point _currentPan = new Point(0, 0);
        private Point _zoomBorderOffset = new Point(0, 0);

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
        public int EraserSize { get; set; } = 20;
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
            IsHitTestVisible = IsPenMode || IsEraserMode;
        }

        public void ExitPhotoMode()
        {
            _isPhotoMode = false;
            _photoWidth = 0;
            _photoHeight = 0;
        }

        public void SetWhiteboardMode()
        {
            _isWhiteboardMode = true;
        }

        public void ExitWhiteboardMode()
        {
            _isWhiteboardMode = false;
            _whiteboardBackgroundPath = null;
            _whiteboardBackgroundBitmap?.Dispose();
            _whiteboardBackgroundBitmap = null;
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
            InvalidateVisual();
        }

        public void ClearStrokes()
        {
            _strokes.Clear();
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

        private SKPoint VideoToScreen(SKPoint videoPoint)
        {
            if (_isWhiteboardMode)
            {
                return videoPoint;
            }

            var screenX = videoPoint.X * (float)_currentZoom + (float)(_currentPan.X + _zoomBorderOffset.X);
            var screenY = videoPoint.Y * (float)_currentZoom + (float)(_currentPan.Y + _zoomBorderOffset.Y);
            return new SKPoint(screenX, screenY);
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

            var newStrokes = new List<InkStroke>();
            float step = 1.0f;

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

            for (int i = 0; i < videoPoints.Count; i++)
            {
                var point = videoPoints[i];
                var width = hasWidths ? stroke.PointWidths![i] : stroke.Size;
                float radius = width / 2;

                // 判断圆是否与矩形相交
                bool intersects = CircleIntersectsRect(point, radius, rectVideo);

                if (!intersects)
                {
                    currentSegment.Add(point);
                    currentWidths.Add(width);
                }
                else
                {
                    // 擦除该点，结束当前段
                    if (currentSegment.Count >= 2)
                    {
                        result.Add(CreateStrokeSegment(currentSegment, currentWidths, stroke));
                    }
                    currentSegment.Clear();
                    currentWidths.Clear();
                }
            }

            // 添加最后一段
            if (currentSegment.Count >= 2)
            {
                result.Add(CreateStrokeSegment(currentSegment, currentWidths, stroke));
            }

            return result;
        }

        private bool CircleIntersectsRect(SKPoint center, float radius, SKRect rect)
        {
            float closestX = Math.Max(rect.Left, Math.Min(center.X, rect.Right));
            float closestY = Math.Max(rect.Top, Math.Min(center.Y, rect.Bottom));
            float dx = center.X - closestX;
            float dy = center.Y - closestY;
            return (dx * dx + dy * dy) < radius * radius;
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

                if (_isDrawing && _currentIsEraser && _tempStrokes != null)
                {
                    RenderStrokes(canvas, _tempStrokes);
                }
                else
                {
                    RenderStrokes(canvas, _strokes);
                }
                
                if (_isDrawing && !_currentIsEraser)
                {
                    RenderCurrentWetStroke(canvas);
                }
                canvas.Restore();
            }

            context.DrawImage(_displayBitmap, new Rect(_displayBitmap.Size), Bounds);
        }

        private void RenderStrokes(SKCanvas canvas, List<InkStroke> strokes)
        {
            float zoom = (_isWhiteboardMode) ? 1.0f : (float)_currentZoom;
            float renderScaling = GetRenderScaling();

            foreach (var stroke in strokes)
            {
                RenderStroke(canvas, stroke, zoom, renderScaling);
            }
        }

        private void RenderStroke(SKCanvas canvas, InkStroke stroke, float zoom, float renderScaling)
        {
            if (stroke.VideoPoints == null || stroke.VideoPoints.Count < 2) return;

            if (stroke.PointWidths != null && stroke.PointWidths.Count == stroke.VideoPoints.Count)
            {
                RenderVariableWidthStroke(canvas, stroke, zoom, renderScaling);
            }
            else
            {
                var displaySize = stroke.Size * zoom;
                var paint = CreatePenPaint(stroke.Color, displaySize);

                for (int i = 1; i < stroke.VideoPoints.Count; i++)
                {
                    var screenFrom = VideoToScreen(stroke.VideoPoints[i - 1]);
                    var screenTo = VideoToScreen(stroke.VideoPoints[i]);
                    canvas.DrawLine(
                        new SKPoint(screenFrom.X / renderScaling, screenFrom.Y / renderScaling),
                        new SKPoint(screenTo.X / renderScaling, screenTo.Y / renderScaling),
                        paint);
                }
            }
        }

        private void RenderVariableWidthStroke(SKCanvas canvas, InkStroke stroke, float zoom, float renderScaling)
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
                var p1 = VideoToScreen(stroke.VideoPoints[i]);
                var p2 = VideoToScreen(stroke.VideoPoints[i + 1]);
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

            int subdivisions = Math.Max(2, (int)(length / 1f));

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
            InvalidateVisual();
        }

        public void Undo()
        {
            if (_strokes.Count == 0) return;

            _strokes.RemoveAt(_strokes.Count - 1);
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
