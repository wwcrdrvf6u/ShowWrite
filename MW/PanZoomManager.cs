using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Application = System.Windows.Application;
using Point = System.Windows.Point;


namespace ShowWrite
{
    /// <summary>
    /// 处理平移和缩放功能的管理器
    /// </summary>
    public class PanZoomManager
    {

        private readonly ScaleTransform _zoomTransform;
        private readonly TranslateTransform _panTransform;
        private readonly UIElement _targetElement;
        private readonly DrawingManager _drawingManager;

        private double _currentZoom = 1.0;
        private bool _isPanning = false;
        private System.Windows.Point _lastMousePos; // 明确命名空间

        // 笔迹缩放补偿相关
        private double _originalPenWidth = 2.0;

        // 添加：管理器启用状态
        private bool _isEnabled = true;

        public double CurrentZoom => _currentZoom;
        public ScaleTransform ZoomTransform => _zoomTransform;
        public TranslateTransform PanTransform => _panTransform;

        public PanZoomManager(ScaleTransform zoomTransform, TranslateTransform panTransform,
                           UIElement targetElement, DrawingManager drawingManager)
        {
            _zoomTransform = zoomTransform;
            _panTransform = panTransform;
            _targetElement = targetElement;
            _drawingManager = drawingManager;
        }

        /// <summary>
        /// 设置管理器是否启用
        /// </summary>
        public void SetEnabled(bool enabled)
        {
            _isEnabled = enabled;
        }

        /// <summary>
        /// 检查管理器是否启用
        /// </summary>
        private bool CheckEnabled()
        {
            return _isEnabled;
        }

        /// <summary>
        /// 重置缩放和平移
        /// </summary>
        public void ResetZoom()
        {
            _currentZoom = 1.0;
            _zoomTransform.ScaleX = 1.0;
            _zoomTransform.ScaleY = 1.0;
            _panTransform.X = 0;
            _panTransform.Y = 0;

            ApplyStrokeScaleCompensation();
        }

        /// <summary>
        /// 应用笔迹缩放补偿
        /// </summary>
        public void ApplyStrokeScaleCompensation()  // 确保这是 public
        {
            if (_drawingManager == null) return;

            try
            {
                double compensatedWidth = _originalPenWidth / _currentZoom;
                compensatedWidth = Math.Max(1.0, Math.Min(50.0, compensatedWidth));

                _drawingManager.UserPenWidth = compensatedWidth;
                _drawingManager.UpdatePenAttributes();

                Console.WriteLine($"缩放补偿: 缩放因子={_currentZoom:F2}, 笔迹宽度={compensatedWidth:F2}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"应用笔迹缩放补偿失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 设置原始笔迹宽度
        /// </summary>
        public void SetOriginalPenWidth(double width)
        {
            _originalPenWidth = width;
            ApplyStrokeScaleCompensation();
        }

        /// <summary>
        /// 鼠标按下事件处理
        /// </summary>
        public void HandleMouseDown(MouseButtonEventArgs e, DrawingManager.ToolMode currentMode)
        {
            if (!CheckEnabled()) return;

            if (currentMode == DrawingManager.ToolMode.Move && e.ChangedButton == MouseButton.Left)
            {
                try
                {
                    _isPanning = true;
                    _lastMousePos = e.GetPosition(System.Windows.Application.Current.MainWindow);
                    _targetElement.CaptureMouse();
                    e.Handled = true;

                    System.Windows.Application.Current.MainWindow.Cursor = System.Windows.Input.Cursors.SizeAll;
                    Console.WriteLine($"开始拖拽: 起始点=({_lastMousePos.X:F1}, {_lastMousePos.Y:F1})");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"鼠标按下错误: {ex.Message}");
                    _isPanning = false;
                }
            }
        }

        /// <summary>
        /// 鼠标移动事件处理
        /// </summary>
        public void HandleMouseMove(System.Windows.Input.MouseEventArgs e, DrawingManager.ToolMode currentMode)
        {
            if (!CheckEnabled()) return;

            if (_isPanning && currentMode == DrawingManager.ToolMode.Move)
            {
                try
                {
                    var currentPos = e.GetPosition(System.Windows.Application.Current.MainWindow);
                    _panTransform.X += currentPos.X - _lastMousePos.X;
                    _panTransform.Y += currentPos.Y - _lastMousePos.Y;
                    _lastMousePos = currentPos;
                    e.Handled = true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"鼠标移动错误: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 鼠标释放事件处理
        /// </summary>
        public void HandleMouseUp(MouseButtonEventArgs e, DrawingManager.ToolMode currentMode)
        {
            if (!CheckEnabled()) return;

            if (_isPanning && e.ChangedButton == MouseButton.Left)
            {
                try
                {
                    _isPanning = false;
                    _targetElement.ReleaseMouseCapture();
                    Application.Current.MainWindow.Cursor = System.Windows.Input.Cursors.Arrow;
                    e.Handled = true;
                    Console.WriteLine($"结束拖拽: 最终平移=({_panTransform.X:F1}, {_panTransform.Y:F1})");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"鼠标释放错误: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 鼠标滚轮事件处理
        /// </summary>
        public void HandleMouseWheel(MouseWheelEventArgs e, DrawingManager.ToolMode currentMode, UIElement zoomContainer)
        {
            if (!CheckEnabled()) return;

            if (currentMode == DrawingManager.ToolMode.Move)
            {
                Point mousePos = e.GetPosition(zoomContainer);
                double zoomFactor = e.Delta > 0 ? 1.1 : 0.9;
                double newZoom = _currentZoom * zoomFactor;
                newZoom = Math.Max(0.1, Math.Min(10, newZoom));

                Point relative = new Point(
                    (mousePos.X - _panTransform.X) / _currentZoom,
                    (mousePos.Y - _panTransform.Y) / _currentZoom);

                _currentZoom = newZoom;
                _zoomTransform.ScaleX = _currentZoom;
                _zoomTransform.ScaleY = _currentZoom;

                _panTransform.X = mousePos.X - relative.X * _currentZoom;
                _panTransform.Y = mousePos.Y - relative.Y * _currentZoom;

                ApplyStrokeScaleCompensation();
                e.Handled = true;
            }
        }

        /// <summary>
        /// 手势操作开始
        /// </summary>
        public void HandleManipulationStarting(ManipulationStartingEventArgs e, DrawingManager.ToolMode currentMode)
        {
            if (!CheckEnabled()) return;

            if (currentMode == DrawingManager.ToolMode.Move)
            {
                e.ManipulationContainer = Application.Current.MainWindow;
                e.Mode = ManipulationModes.Scale | ManipulationModes.Translate;
            }
            else
            {
                e.Mode = ManipulationModes.None;
            }
        }

        /// <summary>
        /// 手势操作处理
        /// </summary>
        public void HandleManipulationDelta(ManipulationDeltaEventArgs e, DrawingManager.ToolMode currentMode, UIElement container)
        {
            if (!CheckEnabled()) return;
            if (currentMode != DrawingManager.ToolMode.Move) return;

            var delta = e.DeltaManipulation;

            // 处理缩放
            if (delta.Scale.X != 1.0 || delta.Scale.Y != 1.0)
            {
                Point center = e.ManipulationOrigin;
                Point relativeCenter = container.TranslatePoint(center, Application.Current.MainWindow);

                Point relative = new Point(
                    (relativeCenter.X - _panTransform.X) / _currentZoom,
                    (relativeCenter.Y - _panTransform.Y) / _currentZoom);

                _currentZoom *= delta.Scale.X;
                _currentZoom = Math.Max(0.1, Math.Min(10, _currentZoom));
                _zoomTransform.ScaleX = _currentZoom;
                _zoomTransform.ScaleY = _currentZoom;

                _panTransform.X = relativeCenter.X - relative.X * _currentZoom;
                _panTransform.Y = relativeCenter.Y - relative.Y * _currentZoom;
            }

            // 处理平移
            _panTransform.X += delta.Translation.X;
            _panTransform.Y += delta.Translation.Y;

            ApplyStrokeScaleCompensation();
            e.Handled = true;
        }
    }
}