using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace ShowWrite
{
    /// <summary>
    /// 触控管理类
    /// </summary>
    public class TouchManager
    {
        private readonly Dictionary<int, System.Windows.Point> _currentTouches = new Dictionary<int, System.Windows.Point>();
        private readonly DispatcherTimer _touchUpdateTimer;
        private readonly DrawingManager _drawingManager;

        private double _sdkTouchArea = 0;

        // 事件 - 使用 System.Windows.Point
        public event Action<int> OnTouchCountChanged;
        public event Action<double> OnTouchAreaChanged;
        public event Action<System.Windows.Point> OnTouchCenterChanged;

        public int TouchCount => _currentTouches.Count;
        public double SDKTouchArea => _sdkTouchArea;

        public TouchManager(DrawingManager drawingManager)
        {
            _drawingManager = drawingManager;

            // 初始化触控更新定时器
            _touchUpdateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100) // 每100ms更新一次
            };
            _touchUpdateTimer.Tick += (s, e) => UpdateTouchInfo();
        }

        /// <summary>
        /// 开始触控跟踪
        /// </summary>
        public void StartTracking()
        {
            _touchUpdateTimer.Start();
        }

        /// <summary>
        /// 停止触控跟踪
        /// </summary>
        public void StopTracking()
        {
            _touchUpdateTimer.Stop();
            _currentTouches.Clear();
        }

        /// <summary>
        /// 触控按下事件处理
        /// </summary>
        public void HandleTouchDown(TouchEventArgs e, UIElement touchContainer)
        {
            var touchPoint = e.GetTouchPoint(touchContainer);
            _currentTouches[e.TouchDevice.Id] = touchPoint.Position;

            OnTouchCountChanged?.Invoke(_currentTouches.Count);
        }

        /// <summary>
        /// 触控移动事件处理
        /// </summary>
        public void HandleTouchMove(TouchEventArgs e, UIElement touchContainer)
        {
            if (_currentTouches.ContainsKey(e.TouchDevice.Id))
            {
                var touchPoint = e.GetTouchPoint(touchContainer);
                _currentTouches[e.TouchDevice.Id] = touchPoint.Position;
            }
        }

        /// <summary>
        /// 触控释放事件处理
        /// </summary>
        public void HandleTouchUp(TouchEventArgs e)
        {
            if (_currentTouches.ContainsKey(e.TouchDevice.Id))
            {
                _currentTouches.Remove(e.TouchDevice.Id);
                OnTouchCountChanged?.Invoke(_currentTouches.Count);
            }
        }

        /// <summary>
        /// 更新 TouchSDK 面积
        /// </summary>
        public void UpdateSDKTouchArea(double area)
        {
            _sdkTouchArea = area;
            OnTouchAreaChanged?.Invoke(area);
        }

        /// <summary>
        /// 更新触控信息
        /// </summary>
        private void UpdateTouchInfo()
        {
            if (_currentTouches.Count > 0)
            {
                // 计算触控中心点
                System.Windows.Point center = CalculateTouchCenter();
                OnTouchCenterChanged?.Invoke(center);

                // 计算触控面积（需要3个以上点）
                if (_currentTouches.Count >= 3)
                {
                    double area = CalculatePolygonArea(_currentTouches.Values.ToList());
                    OnTouchAreaChanged?.Invoke(area);
                }
            }
        }

        /// <summary>
        /// 计算触控中心点
        /// </summary>
        public System.Windows.Point CalculateTouchCenter()
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

        /// <summary>
        /// 计算多边形面积（鞋带公式）
        /// </summary>
        public double CalculatePolygonArea(List<System.Windows.Point> points)
        {
            if (points.Count < 3)
                return 0;

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

        /// <summary>
        /// 获取当前触控点列表
        /// </summary>
        public List<System.Windows.Point> GetCurrentTouchPoints()
        {
            return _currentTouches.Values.ToList();
        }

        /// <summary>
        /// 获取触控信息摘要
        /// </summary>
        public string GetTouchInfoSummary()
        {
            var center = CalculateTouchCenter();
            var area = _currentTouches.Count >= 3 ? CalculatePolygonArea(_currentTouches.Values.ToList()) : 0;

            return $"触控点数: {_currentTouches.Count}, 中心: ({center.X:F0}, {center.Y:F0}), 面积: {area:F0} 像素²";
        }

        /// <summary>
        /// 获取 TouchSDK 状态文本
        /// </summary>
        public string GetTouchSDKStatusText()
        {
            if (_drawingManager.IsTouchSDKInitialized)
            {
                return $"触控点数: {_currentTouches.Count} (TouchSDK 就绪)";
            }
            else
            {
                return $"触控点数: {_currentTouches.Count} (TouchSDK 未就绪)";
            }
        }
    }
}