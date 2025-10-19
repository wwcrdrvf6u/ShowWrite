using ShowWrite.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using Path = System.IO.Path;

namespace ShowWrite
{
    public class DrawingManager : IDisposable
    {
        public enum ToolMode { None, Move, Pen, Eraser }

        private readonly InkCanvas _inkCanvas;
        private readonly FrameworkElement _videoArea;
        private readonly Window _mainWindow;

        public ToolMode CurrentMode { get; private set; } = ToolMode.None;

        // 编辑历史
        private readonly Stack<EditAction> _editHistory = new Stack<EditAction>();
        private readonly Stack<EditAction> _redoHistory = new Stack<EditAction>();
        private EditAction? _currentEdit = null;
        private bool _isEditing = false;

        private class EditAction
        {
            public List<Stroke> AddedStrokes { get; } = new();
            public List<Stroke> RemovedStrokes { get; } = new();
        }

        // 缩放比例 & 用户笔宽
        public double CurrentZoom { get; private set; } = 1.0;
        public double UserPenWidth { get; set; } = 2.0; // 改为 set 可访问
        public Color PenColor => _inkCanvas.DefaultDrawingAttributes.Color;

        // 触摸点跟踪
        private readonly Dictionary<int, System.Windows.Point> _touchPoints = new Dictionary<int, System.Windows.Point>();
        private double _lastTouchDistance = -1;
        private System.Windows.Point _lastTouchCenter;

        // 橡皮擦设置
        private double _manualEraserSize = 20.0;

        // 鼠标平移状态
        private bool _isPanning = false;
        private System.Windows.Point _lastMousePos;

        // TouchSDK 相关字段
        private bool _touchSDKInitialized = false;
        private double _sdkTouchArea = 0;

        // =========================
        // 手掌擦手势功能 (从 MW_TouchEvents.cs 移植)
        // =========================
        private bool _isPalmEraserActive = false;
        private ToolMode _lastModeBeforePalmEraser = ToolMode.Pen;
        private double _palmEraserThreshold = 1000.0; // 默认阈值
        private double _currentTouchArea = 0.0;
        private bool _enablePalmEraser = true;

        // 手掌擦配置属性
        public double PalmEraserThreshold
        {
            get => _palmEraserThreshold;
            set
            {
                _palmEraserThreshold = Math.Max(1, value);
                Console.WriteLine($"设置手掌擦阈值: {_palmEraserThreshold}");
            }
        }

        public bool EnablePalmEraser
        {
            get => _enablePalmEraser;
            set => _enablePalmEraser = value;
        }

        public bool IsPalmEraserActive => _isPalmEraserActive;

        // TouchSDK 回调委托
        private delegate void FuncTouchPointData(IntPtr pDevInfo, IntPtr pdata, int maxpointnum, int nValidPointNum, IntPtr pObj);
        private delegate void FuncHotplugDevInfo(IntPtr devInfo, byte attached, IntPtr callbackobject);

        // 设备信息结构体
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DeviceInfo
        {
            public int deviceID;
            public int vendorID;
            public int productID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string deviceName;
            public int maxTouchPoints;
            public int resolutionX;
            public int resolutionY;
        }

        // 触摸点数据结构体
        [StructLayout(LayoutKind.Sequential)]
        public struct TouchPointData
        {
            public int x;           // X 坐标
            public int y;           // Y 坐标
            public int width;       // 触摸宽度
            public int height;      // 触摸高度
            public int pressure;    // 压力值
            public byte touchState; // 触摸状态
            public byte touchID;    // 触摸点ID
            public byte area;       // 触摸面积
            public byte reserved;   // 保留字段
        }

        // TouchSDK DLL 导入
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        [DllImport("TouchSDKDll.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int InitTouch(
            [In, Out] DeviceInfo[] pDevInfos,
            int nMaxDevInfoNum,
            FuncTouchPointData funcTouchPointData,
            FuncHotplugDevInfo funcHotplugDevInfo,
            IntPtr pObj);

        [DllImport("TouchSDKDll.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern bool EnableTouch(DeviceInfo DevInfo, int nTimeout = 20);

        [DllImport("TouchSDKDll.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern bool EnableRawData(DeviceInfo DevInfo, int nTimeout = 20);

        [DllImport("TouchSDKDll.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern bool EnableTouchWidthData(DeviceInfo DevInfo, int nTimeout = 20);

        [DllImport("TouchSDKDll.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int GetTouchDeviceCount();

        [DllImport("TouchSDKDll.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void ExitTouch();

        // SDK 面积属性
        public double SDKTouchArea
        {
            get => _sdkTouchArea;
            private set
            {
                if (_sdkTouchArea != value)
                {
                    _sdkTouchArea = value;
                    OnSDKTouchAreaChanged?.Invoke(value);
                }
            }
        }

        // SDK 面积变化事件
        public event Action<double> OnSDKTouchAreaChanged;

        // 手掌擦状态变化事件
        public event Action<bool> OnPalmEraserStateChanged;

        public DrawingManager(InkCanvas inkCanvas, FrameworkElement videoArea, Window mainWindow)
        {
            _inkCanvas = inkCanvas;
            _videoArea = videoArea;
            _mainWindow = mainWindow;

            InitializeEventHandlers();
            SetMode(ToolMode.Move, initial: true);

            // 初始化 TouchSDK
            InitializeTouchSDK();
        }

        private void InitializeEventHandlers()
        {
            // 捕捉画笔/橡皮事件
            _inkCanvas.StrokeCollected += Ink_StrokeCollected;
            _inkCanvas.PreviewMouseLeftButtonDown += Ink_PreviewMouseDown;
            _inkCanvas.PreviewMouseLeftButtonUp += Ink_PreviewMouseUp;
            _inkCanvas.PreviewStylusDown += Ink_PreviewStylusDown;
            _inkCanvas.PreviewStylusUp += Ink_PreviewStylusUp;
            _inkCanvas.Strokes.StrokesChanged += Ink_StrokesChanged;

            // 设置初始橡皮擦形状
            _inkCanvas.EraserShape = new RectangleStylusShape(_manualEraserSize, _manualEraserSize);
        }

        // =========================
        // TouchSDK 初始化和管理
        // =========================

        /// <summary>
        /// 初始化 TouchSDK
        /// </summary>
        public bool InitializeTouchSDK()
        {
            try
            {
                Console.WriteLine("开始初始化 TouchSDK...");

                // 直接根据架构设置 DLL 路径
                string arch = IntPtr.Size == 8 ? "x64" : "x86";
                string dllPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, arch, "TouchSDKDll.dll");

                Console.WriteLine($"直接查找 DLL 路径: {dllPath}");

                // 检查 DLL 文件是否存在
                if (!File.Exists(dllPath))
                {
                    Console.WriteLine($"错误: 未找到 TouchSDKDll.dll 在路径: {dllPath}");
                    Console.WriteLine("请确保以下文件存在:");
                    Console.WriteLine($"- {Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "x64", "TouchSDKDll.dll")}");
                    Console.WriteLine($"- {Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "x86", "TouchSDKDll.dll")}");
                    return false;
                }

                Console.WriteLine($"找到 TouchSDK DLL 文件: {dllPath}");

                // 设置 DLL 目录到架构特定的文件夹
                string archDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, arch);
                if (!SetDllDirectory(archDirectory))
                {
                    int errorCode = Marshal.GetLastWin32Error();
                    Console.WriteLine($"设置 DLL 目录失败: {archDirectory}, 错误代码: {errorCode}");
                    return false;
                }
                Console.WriteLine($"已设置 DLL 目录: {archDirectory}");

                // 检查设备数量
                Console.WriteLine("正在获取 TouchSDK 设备数量...");
                int deviceCount = GetTouchDeviceCount();
                Console.WriteLine($"检测到 {deviceCount} 个 TouchSDK 设备");

                if (deviceCount <= 0)
                {
                    Console.WriteLine("未检测到 TouchSDK 兼容设备，TouchSDK 将不可用");
                    Console.WriteLine("可能的原因:");
                    Console.WriteLine("1. 没有连接 TouchSDK 兼容设备");
                    Console.WriteLine("2. 设备驱动未正确安装");
                    Console.WriteLine("3. 设备权限不足");
                    return false;
                }

                // 初始化设备信息数组
                var deviceInfos = new DeviceInfo[10];

                Console.WriteLine("正在初始化 TouchSDK...");
                int initResult = InitTouch(
                    deviceInfos,
                    deviceInfos.Length,
                    OnTouchPointData,
                    OnHotplugEvent,
                    IntPtr.Zero);

                Console.WriteLine($"InitTouch 返回代码: {initResult}");

                if (initResult != 0)
                {
                    Console.WriteLine($"TouchSDK 初始化失败，错误码: {initResult}");
                    return false;
                }

                // 启用第一个设备的功能
                if (deviceCount > 0)
                {
                    var firstDevice = deviceInfos[0];
                    Console.WriteLine($"正在启用设备: {firstDevice.deviceName} (ID: {firstDevice.deviceID})");

                    if (!EnableTouch(firstDevice))
                    {
                        Console.WriteLine("启用触摸功能失败");
                        return false;
                    }
                    Console.WriteLine("触摸功能已启用");

                    if (!EnableRawData(firstDevice))
                    {
                        Console.WriteLine("启用原始数据失败");
                        return false;
                    }
                    Console.WriteLine("原始数据功能已启用");

                    if (!EnableTouchWidthData(firstDevice))
                    {
                        Console.WriteLine("启用触摸面积数据失败");
                        return false;
                    }
                    Console.WriteLine("触摸面积数据功能已启用");
                }

                _touchSDKInitialized = true;
                Console.WriteLine("TouchSDK 初始化成功 ✓");
                return true;
            }
            catch (DllNotFoundException dllEx)
            {
                Console.WriteLine($"DLL 未找到异常: {dllEx.Message}");
                Console.WriteLine($"这通常意味着:");
                Console.WriteLine($"1. TouchSDKDll.dll 文件不存在");
                Console.WriteLine($"2. DLL 文件损坏");
                Console.WriteLine($"3. 架构不匹配 (32位/64位)");
                return false;
            }
            catch (BadImageFormatException badImageEx)
            {
                Console.WriteLine($"DLL 格式异常: {badImageEx.Message}");
                Console.WriteLine($"架构不匹配: 当前应用程序是 {(IntPtr.Size == 8 ? "64位" : "32位")}");
                Console.WriteLine($"请确保使用正确的架构版本:");
                Console.WriteLine($"- 64位应用程序使用 x64/TouchSDKDll.dll");
                Console.WriteLine($"- 32位应用程序使用 x86/TouchSDKDll.dll");
                return false;
            }
            catch (EntryPointNotFoundException entryEx)
            {
                Console.WriteLine($"入口点未找到异常: {entryEx.Message}");
                Console.WriteLine($"DLL 中的函数签名可能已更改或不兼容");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"TouchSDK 初始化异常: {ex.Message}");
                Console.WriteLine($"异常类型: {ex.GetType()}");
                Console.WriteLine($"堆栈跟踪: {ex.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// 触摸点数据回调
        /// </summary>
        private void OnTouchPointData(IntPtr pDevInfo, IntPtr pdata, int maxpointnum, int nValidPointNum, IntPtr pObj)
        {
            try
            {
                if (nValidPointNum <= 0)
                {
                    SDKTouchArea = 0;
                    return;
                }

                double totalArea = 0;

                for (int i = 0; i < nValidPointNum; i++)
                {
                    int offset = i * Marshal.SizeOf<TouchPointData>();
                    IntPtr pointPtr = IntPtr.Add(pdata, offset);

                    var point = Marshal.PtrToStructure<TouchPointData>(pointPtr);

                    // 计算单个触摸点的面积（使用 width * height）
                    double pointArea = point.width * point.height;
                    totalArea += pointArea;

                    // 调试输出（可选）
                    // Console.WriteLine($"TouchSDK 点 {i}: X={point.x}, Y={point.y}, Width={point.width}, Height={point.height}, Area={pointArea}");
                }

                // 更新 SDK 面积（通过属性触发事件）
                SDKTouchArea = totalArea;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"处理 TouchSDK 数据异常: {ex.Message}");
                SDKTouchArea = 0;
            }
        }

        /// <summary>
        /// 热插拔事件回调
        /// </summary>
        private void OnHotplugEvent(IntPtr devInfo, byte attached, IntPtr callbackobject)
        {
            string status = attached != 0 ? "连接" : "断开";
            Console.WriteLine($"TouchSDK 设备{status}");
        }

        /// <summary>
        /// 清理 TouchSDK 资源
        /// </summary>
        private void CleanupTouchSDK()
        {
            if (_touchSDKInitialized)
            {
                try
                {
                    ExitTouch();
                    _touchSDKInitialized = false;
                    Console.WriteLine("TouchSDK 已清理");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"清理 TouchSDK 异常: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 获取 TouchSDK 初始化状态
        /// </summary>
        public bool IsTouchSDKInitialized => _touchSDKInitialized;

        // =========================
        // 配置和应用设置
        // =========================
        public void ApplyConfig(AppConfig config)
        {
            var penColor = (Color)System.Windows.Media.ColorConverter.ConvertFromString(config.DefaultPenColor ?? "#FF000000");
            _inkCanvas.DefaultDrawingAttributes.Color = penColor;
            UserPenWidth = config.DefaultPenWidth;

            // 应用手掌擦配置
            if (config.EnablePalmEraser)
            {
                EnablePalmEraser = true;
                PalmEraserThreshold = config.PalmEraserThreshold;
            }
            else
            {
                EnablePalmEraser = false;
            }

            UpdatePenAttributes();
        }

        // =========================
        // 设置画笔颜色
        // =========================
        public void SetPenColor(Color color)
        {
            _inkCanvas.DefaultDrawingAttributes.Color = color;
        }

        // =========================
        // 编辑操作管理
        // =========================
        private void StartEdit()
        {
            if (_isEditing) return;
            _currentEdit = new EditAction();
            _isEditing = true;
        }

        private void EndEdit()
        {
            if (!_isEditing || _currentEdit == null) return;
            if (_currentEdit.AddedStrokes.Count > 0 || _currentEdit.RemovedStrokes.Count > 0)
            {
                _editHistory.Push(_currentEdit);
                // 新操作后清空重做历史
                _redoHistory.Clear();
            }
            _currentEdit = null;
            _isEditing = false;
        }

        private void Ink_PreviewMouseDown(object sender, MouseButtonEventArgs e) => StartEdit();
        private void Ink_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (CurrentMode != ToolMode.Pen)
                EndEdit();
        }

        private void Ink_PreviewStylusDown(object sender, StylusDownEventArgs e) => StartEdit();
        private void Ink_PreviewStylusUp(object sender, StylusEventArgs e)
        {
            if (CurrentMode != ToolMode.Pen)
                EndEdit();
        }

        // 画笔：在收集到 Stroke 时一次性加入并结束本次手势
        private void Ink_StrokeCollected(object sender, InkCanvasStrokeCollectedEventArgs e)
        {
            if (CurrentMode != ToolMode.Pen) return;
            if (!_isEditing || _currentEdit == null) StartEdit();
            _currentEdit!.AddedStrokes.Add(e.Stroke);
            EndEdit();
        }

        // 橡皮：StrokesChanged 会持续触发，等 MouseUp 再 EndEdit()
        private void Ink_StrokesChanged(object? sender, StrokeCollectionChangedEventArgs e)
        {
            if (CurrentMode == ToolMode.Pen) return;
            if (!_isEditing || _currentEdit == null) return;
            foreach (var s in e.Added) _currentEdit.AddedStrokes.Add(s);
            foreach (var s in e.Removed) _currentEdit.RemovedStrokes.Add(s);
        }

        // =========================
        // 模式切换
        // =========================
        public void SetMode(ToolMode mode, bool initial = false)
        {
            // 如果手动切换模式，停用手掌擦
            if (_isPalmEraserActive && mode != ToolMode.Eraser)
            {
                DeactivatePalmEraser();
            }

            CurrentMode = mode;

            switch (mode)
            {
                case ToolMode.Move:
                    _inkCanvas.EditingMode = InkCanvasEditingMode.None;
                    _mainWindow.Cursor = Cursors.Hand;
                    break;
                case ToolMode.Pen:
                    _inkCanvas.EditingMode = InkCanvasEditingMode.Ink;
                    _mainWindow.Cursor = Cursors.Arrow;
                    break;
                case ToolMode.Eraser:
                    _inkCanvas.EditingMode = InkCanvasEditingMode.EraseByPoint;
                    _mainWindow.Cursor = Cursors.Arrow;
                    // 如果不是手掌擦激活，使用手动设置的橡皮擦大小
                    if (!_isPalmEraserActive)
                    {
                        _inkCanvas.EraserShape = new RectangleStylusShape(_manualEraserSize, _manualEraserSize);
                    }
                    break;
            }
        }

        public void OpenPenSettings()
        {
            double currentEraserWidth = _manualEraserSize;
            if (_inkCanvas.EraserShape is RectangleStylusShape rectShape)
            {
                currentEraserWidth = rectShape.Width;
            }

            var dlg = new PenSettingsWindow(_inkCanvas.DefaultDrawingAttributes.Color, UserPenWidth, currentEraserWidth);
            if (dlg.ShowDialog() == true)
            {
                _inkCanvas.DefaultDrawingAttributes.Color = dlg.SelectedColor;
                UserPenWidth = dlg.SelectedPenWidth;
                _manualEraserSize = dlg.SelectedEraserWidth;
                _inkCanvas.EraserShape = new RectangleStylusShape(_manualEraserSize, _manualEraserSize);
                UpdatePenAttributes();
            }
        }

        public void UpdatePenAttributes()
        {
            // 保持视觉笔宽与缩放无关 - 现在由 MainWindow 控制缩放补偿
            _inkCanvas.DefaultDrawingAttributes.Width = UserPenWidth;
            _inkCanvas.DefaultDrawingAttributes.Height = UserPenWidth;
        }

        // =========================
        // 绘制操作
        // =========================
        public void ClearStrokes()
        {
            _inkCanvas.Strokes.Clear();
            _editHistory.Clear();
            _redoHistory.Clear();
        }

        public void Undo()
        {
            if (_editHistory.Count == 0) return;
            var lastAction = _editHistory.Pop();

            // 将撤销的操作加入重做历史
            var redoAction = new EditAction();
            foreach (var stroke in lastAction.AddedStrokes)
            {
                if (_inkCanvas.Strokes.Contains(stroke))
                {
                    _inkCanvas.Strokes.Remove(stroke);
                    redoAction.RemovedStrokes.Add(stroke);
                }
            }
            foreach (var stroke in lastAction.RemovedStrokes)
            {
                if (!_inkCanvas.Strokes.Contains(stroke))
                {
                    _inkCanvas.Strokes.Add(stroke);
                    redoAction.AddedStrokes.Add(stroke);
                }
            }

            _redoHistory.Push(redoAction);
        }

        public void Redo()
        {
            if (_redoHistory.Count == 0) return;
            var redoAction = _redoHistory.Pop();

            // 执行重做操作（与撤销相反）
            foreach (var stroke in redoAction.RemovedStrokes)
            {
                if (!_inkCanvas.Strokes.Contains(stroke))
                    _inkCanvas.Strokes.Add(stroke);
            }
            foreach (var stroke in redoAction.AddedStrokes)
            {
                if (_inkCanvas.Strokes.Contains(stroke))
                    _inkCanvas.Strokes.Remove(stroke);
            }

            // 将重做的操作重新加入撤销历史
            _editHistory.Push(redoAction);
        }

        public bool CanRedo => _redoHistory.Count > 0;

        public void SwitchToPhotoStrokes(StrokeCollection strokes)
        {
            _inkCanvas.Strokes.StrokesChanged -= Ink_StrokesChanged;
            _inkCanvas.Strokes = strokes;
            _inkCanvas.Strokes.StrokesChanged += Ink_StrokesChanged;
            _editHistory.Clear();
            _redoHistory.Clear();
        }

        // =========================
        // 缩放/平移功能 - 修复空引用异常
        // =========================
        public void HandleMouseWheel(MouseWheelEventArgs e)
        {
            // 现在缩放功能由 MainWindow 统一处理
            // 这里只处理非移动模式下的鼠标滚轮事件
            if (CurrentMode != ToolMode.Move)
            {
                // 可以在这里添加其他模式的滚轮处理逻辑
                // 例如：调整笔迹大小等
            }
        }

        public void HandleManipulationDelta(ManipulationDeltaEventArgs e)
        {
            // 现在手势操作由 MainWindow 统一处理
            // 这里只处理非移动模式下的手势
            if (CurrentMode != ToolMode.Move)
            {
                // 可以在这里添加其他模式的手势处理逻辑
            }
        }

        // =========================
        // 鼠标事件处理
        // =========================
        public void HandleMouseDown(MouseButtonEventArgs e)
        {
            // 只在移动模式下启用平移
            if (CurrentMode == ToolMode.Move)
            {
                var p = e.GetPosition(_mainWindow);
                _lastMousePos = new System.Windows.Point(p.X, p.Y);
                _isPanning = true;
                _mainWindow.Cursor = Cursors.Hand;
            }
        }

        public void HandleMouseMove(System.Windows.Input.MouseEventArgs e)
        {
            // 只在移动模式下处理平移
            if (_isPanning && CurrentMode == ToolMode.Move && e.LeftButton == MouseButtonState.Pressed)
            {
                var pos = e.GetPosition(_mainWindow);
                // 平移功能现在由 MainWindow 统一处理
                // 这里只更新鼠标位置
                _lastMousePos = new System.Windows.Point(pos.X, pos.Y);
            }
        }

        public void HandleMouseUp(MouseButtonEventArgs e)
        {
            _isPanning = false;
            _mainWindow.Cursor = Cursors.Arrow;
        }

        // =========================
        // 触摸事件处理
        // =========================
        public void HandleTouchDown(TouchEventArgs e)
        {
            // 记录触摸点
            var touchPoint = e.GetTouchPoint(_videoArea);
            _touchPoints[e.TouchDevice.Id] = touchPoint.Position;

            // 更新触摸中心点和距离
            UpdateTouchCenterAndDistance();

            // 检测手掌擦（只在画笔模式下且启用手掌擦功能）
            if (CurrentMode == ToolMode.Pen && _enablePalmEraser)
            {
                HandleTouchDownForPalmEraser(e);
            }
        }

        public void HandleTouchMove(TouchEventArgs e)
        {
            // 更新触摸点位置
            if (_touchPoints.ContainsKey(e.TouchDevice.Id))
            {
                var touchPoint = e.GetTouchPoint(_videoArea);
                _touchPoints[e.TouchDevice.Id] = touchPoint.Position;

                // 更新触摸中心点和距离
                UpdateTouchCenterAndDistance();

                // 更新手掌擦（如果激活）
                if (_isPalmEraserActive)
                {
                    HandleTouchMoveForPalmEraser(e);
                }

                // 只在移动模式下处理手势
                if (CurrentMode == ToolMode.Move && _touchPoints.Count >= 2)
                {
                    HandleMultiTouchGesture();
                }
            }
        }

        public void HandleTouchUp(TouchEventArgs e)
        {
            // 移除触摸点
            if (_touchPoints.ContainsKey(e.TouchDevice.Id))
            {
                _touchPoints.Remove(e.TouchDevice.Id);
            }

            // 停用手掌擦（如果激活）
            if (_isPalmEraserActive)
            {
                HandleTouchUpForPalmEraser(e);
            }

            // 更新触摸中心点和距离
            UpdateTouchCenterAndDistance();

            // 重置最后触摸距离
            if (_touchPoints.Count < 2)
            {
                _lastTouchDistance = -1;
            }
        }

        // 更新触摸中心点和距离
        private void UpdateTouchCenterAndDistance()
        {
            if (_touchPoints.Count == 0)
            {
                _lastTouchCenter = new System.Windows.Point(0, 0);
                _lastTouchDistance = -1;
                return;
            }

            // 计算中心点
            double centerX = 0, centerY = 0;
            foreach (var point in _touchPoints.Values)
            {
                centerX += point.X;
                centerY += point.Y;
            }
            centerX /= _touchPoints.Count;
            centerY /= _touchPoints.Count;
            _lastTouchCenter = new System.Windows.Point(centerX, centerY);

            // 计算两点之间的距离（如果是双指）
            if (_touchPoints.Count == 2)
            {
                var points = _touchPoints.Values.ToArray();
                double dx = points[1].X - points[0].X;
                double dy = points[1].Y - points[0].Y;
                _lastTouchDistance = Math.Sqrt(dx * dx + dy * dy);
            }
            else
            {
                _lastTouchDistance = -1;
            }
        }

        // 处理多指手势（缩放和平移）
        private void HandleMultiTouchGesture()
        {
            if (_touchPoints.Count < 2 || _lastTouchDistance <= 0)
                return;

            // 计算当前两点之间的距离
            var points = _touchPoints.Values.ToArray();
            double dx = points[1].X - points[0].X;
            double dy = points[1].Y - points[0].Y;
            double currentDistance = Math.Sqrt(dx * dx + dy * dy);

            // 计算缩放比例
            if (_lastTouchDistance > 0)
            {
                double scaleFactor = currentDistance / _lastTouchDistance;
                // 现在缩放功能由 MainWindow 统一处理
                // 这里只更新触摸距离
            }

            // 更新最后触摸距离
            _lastTouchDistance = currentDistance;
        }

        // =========================
        // 手掌擦手势功能实现
        // =========================

        /// <summary>
        /// 处理触摸按下事件，检测手掌擦
        /// </summary>
        private void HandleTouchDownForPalmEraser(TouchEventArgs e)
        {
            try
            {
                // 只在画笔模式下检测手掌擦
                if (CurrentMode != ToolMode.Pen) return;

                var touchPoint = e.GetTouchPoint(_videoArea);
                var bounds = touchPoint.Bounds;

                // 计算触摸面积
                _currentTouchArea = bounds.Width * bounds.Height;

                Console.WriteLine($"触摸面积: {_currentTouchArea}, 阈值: {_palmEraserThreshold}");

                // 如果触摸面积超过阈值，激活手掌擦
                if (_currentTouchArea > _palmEraserThreshold)
                {
                    ActivatePalmEraser(touchPoint);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"手掌擦触摸按下处理错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理触摸移动事件，更新手掌擦
        /// </summary>
        private void HandleTouchMoveForPalmEraser(TouchEventArgs e)
        {
            try
            {
                if (!_isPalmEraserActive) return;

                var touchPoint = e.GetTouchPoint(_videoArea);
                var bounds = touchPoint.Bounds;

                // 更新触摸面积
                _currentTouchArea = bounds.Width * bounds.Height;

                // 动态调整橡皮擦大小基于触摸面积
                if (_currentTouchArea > _palmEraserThreshold)
                {
                    UpdateEraserSizeBasedOnTouchArea(_currentTouchArea);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"手掌擦触摸移动处理错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理触摸抬起事件，停用手掌擦
        /// </summary>
        private void HandleTouchUpForPalmEraser(TouchEventArgs e)
        {
            try
            {
                if (_isPalmEraserActive)
                {
                    DeactivatePalmEraser();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"手掌擦触摸抬起处理错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 激活手掌擦
        /// </summary>
        private void ActivatePalmEraser(TouchPoint touchPoint)
        {
            if (_isPalmEraserActive) return;

            Console.WriteLine("激活手掌擦");

            // 保存当前模式
            _lastModeBeforePalmEraser = CurrentMode;

            // 切换到橡皮擦模式
            SetMode(ToolMode.Eraser);

            // 根据触摸面积调整橡皮擦大小
            UpdateEraserSizeBasedOnTouchArea(_currentTouchArea);

            _isPalmEraserActive = true;

            // 触发手掌擦激活事件
            OnPalmEraserStateChanged?.Invoke(true);
        }

        /// <summary>
        /// 停用手掌擦
        /// </summary>
        private void DeactivatePalmEraser()
        {
            if (!_isPalmEraserActive) return;

            Console.WriteLine("停用手掌擦");

            // 恢复之前的模式
            SetMode(_lastModeBeforePalmEraser);

            // 恢复原始橡皮擦大小
            _inkCanvas.EraserShape = new RectangleStylusShape(_manualEraserSize, _manualEraserSize);

            _isPalmEraserActive = false;

            // 触发手掌擦状态变化事件
            OnPalmEraserStateChanged?.Invoke(false);
        }

        /// <summary>
        /// 基于触摸面积更新橡皮擦大小
        /// </summary>
        private void UpdateEraserSizeBasedOnTouchArea(double touchArea)
        {
            try
            {
                // 根据触摸面积计算橡皮擦大小
                // 使用对数缩放以避免过大或过小的橡皮擦
                double baseSize = Math.Sqrt(touchArea) * 0.1;
                double newEraserSize = Math.Max(10, Math.Min(100, baseSize));

                _inkCanvas.EraserShape = new RectangleStylusShape(newEraserSize, newEraserSize);

                Console.WriteLine($"更新橡皮擦大小: {newEraserSize} (基于触摸面积: {touchArea})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"更新橡皮擦大小错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 设置手掌擦阈值
        /// </summary>
        public void SetPalmEraserThreshold(double threshold)
        {
            PalmEraserThreshold = threshold;
        }

        /// <summary>
        /// 强制停用手掌擦（用于外部调用）
        /// </summary>
        public void ForceDeactivatePalmEraser()
        {
            DeactivatePalmEraser();
        }

        public bool HasStrokes => _inkCanvas.Strokes.Count > 0;

        public StrokeCollection GetStrokes()
        {
            return new StrokeCollection(_inkCanvas.Strokes);
        }

        public void Dispose()
        {
            // 清理事件处理程序
            _inkCanvas.StrokeCollected -= Ink_StrokeCollected;
            _inkCanvas.PreviewMouseLeftButtonDown -= Ink_PreviewMouseDown;
            _inkCanvas.PreviewMouseLeftButtonUp -= Ink_PreviewMouseUp;
            _inkCanvas.PreviewStylusDown -= Ink_PreviewStylusDown;
            _inkCanvas.PreviewStylusUp -= Ink_PreviewStylusUp;
            _inkCanvas.Strokes.StrokesChanged -= Ink_StrokesChanged;

            // 清理资源
            _editHistory.Clear();
            _redoHistory.Clear();
            _touchPoints.Clear();

            // 清理 TouchSDK
            CleanupTouchSDK();
        }
    }
}