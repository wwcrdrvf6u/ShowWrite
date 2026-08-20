using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;

namespace ShowWrite
{
    /// <summary>
    /// 摄像头服务类，直接使用 OpenCVSharp 捕获摄像头，提供帧数据和透视变换功能。
    /// </summary>
    public class CameraService : IDisposable
    {
        private VideoCapture? _capture;
        private volatile bool _cancelled = false;
        private readonly object _lock = new();
        private volatile bool _connectCancelled = false;
        // 每次 StartCapture 递增，Task.Run 捕获此代次，
        // 防止旧 Task.Run 在新 StartCapture 重置 _cancelled=false 后误以为未取消，
        // 导致旧 VideoCapture 覆盖新 _capture（资源泄漏 + 摄像头设备冲突）。
        private volatile int _captureGeneration = 0;

        private WriteableBitmap? _frameBitmap;
        // 单 Mat：capture.Read 与 UI 拷贝都在 UI 线程的 DispatcherTimer 中完成（同线程不重入），
        // 无需双缓冲。OpenCV 非线程安全——之前后台线程 capture.Read 与 UI 线程 DataPointer 访问
        // 跨线程并发，OpenCV 全局状态冲突导致 native 堆损坏 → AccessViolation。
        private Mat? _latestFrame;
        private Mat? _processedFrame;

        private int _frameWidth;
        private int _frameHeight;
        private int _frameStride;

        private DispatcherTimer? _uiTimer;

        private int _currentCameraIndex = 0;
        private List<int> _availableCameraIndices = new();
        private Config _config = new();   // 使用全局 Config

        private Mat? _perspectiveMatrix;
        private bool _hasPerspectiveTransform = false;
        private Point2f[]? _sourcePoints;
        private Point2f[]? _destinationPoints;

        // 公共属性
        public WriteableBitmap? FrameBitmap => _frameBitmap;
        public Mat? LatestFrame => _latestFrame;
        public int CameraCount => _availableCameraIndices.Count;
        public int CurrentCameraIndex => _currentCameraIndex;
        public bool HasPerspectiveTransform => _hasPerspectiveTransform;
        public bool IsConnected
        {
            get
            {
                lock (_lock)
                {
                    return _capture != null && !_cancelled;
                }
            }
        }

        /// <summary>
        /// 获取处理后的帧（应用了透视变换）。返回独立 Mat 副本，调用方自行 Dispose；
        /// CaptureLoop 在 finally dispose _latestFrame 时不会影响已返回的副本，避免 AccessViolation。
        /// </summary>
        public Mat? GetProcessedFrame()
        {
            lock (_lock)
            {
                if (_latestFrame == null || _latestFrame.Empty())
                    return null;

                if (_hasPerspectiveTransform && _perspectiveMatrix != null)
                {
                    return ApplyPerspectiveTransform(_latestFrame);
                }
                // 无透视变换：必须 clone 返回，否则调用方持有的就是 _latestFrame 本身，
                // CaptureLoop finally dispose 它时调用方仍在用 → AccessViolation。
                return _latestFrame.Clone();
            }
        }

        /// <summary>
        /// 获取当前显示帧的稳定副本（持锁内 clone）。供 UI 线程异步使用（如 PresentCameraFrame→InkCanvas），
        /// 调用方用完自行 Dispose；CaptureLoop dispose 原始 _latestFrame 不影响本副本。
        /// </summary>
        public Mat? GetLatestFrameCopy()
        {
            lock (_lock)
            {
                if (_latestFrame == null || _latestFrame.Empty())
                    return null;
                return _latestFrame.Clone();
            }
        }

        /// <summary>
        /// 持锁读取当前显示帧的尺寸（不 Clone Mat）。供高频 UI 调用（如 60fps PresentCameraFrame）使用：
        /// 读尺寸只需访问 Mat 元数据，不需要 Clone 整帧像素（Clone 2.7MB/帧 × 60fps = 162MB/秒 分配压力，
        /// 会压垮 OpenCV 内存池导致 "Failed to allocate N bytes"）。
        /// </summary>
        public bool TryGetLatestFrameSize(out int width, out int height)
        {
            lock (_lock)
            {
                if (_latestFrame == null || _latestFrame.Empty())
                {
                    width = 0; height = 0;
                    return false;
                }
                width = _latestFrame.Width;
                height = _latestFrame.Height;
                return true;
            }
        }

        // 事件
        public event Action<string>? ErrorOccurred;
        public event Action? FrameReady;
        public event Action? CameraStarted;
        public event Action? ScanComplete;
        public event Action? UsingCachedCameras;

        /// <summary>
        /// 检测并连接摄像头（基于缓存配置或扫描）
        /// </summary>
        public void DetectAndConnectCamera()
        {
            _connectCancelled = false;
            _config = Config.Load();   // 使用全局 Config 的静态方法

            if (_config.AvailableCameraIndices.Count > 0)
            {
                _availableCameraIndices = new List<int>(_config.AvailableCameraIndices);
                _currentCameraIndex = _config.CurrentCameraIndex;

                if (_currentCameraIndex >= _availableCameraIndices.Count)
                    _currentCameraIndex = 0;

                if (_availableCameraIndices.Count > 0)
                {
                    var cameraIdx = _availableCameraIndices[_currentCameraIndex];
                    UsingCachedCameras?.Invoke();
                    if (!_connectCancelled)
                    {
                        StartCapture(cameraIdx);
                    }
                    return;
                }
            }

            ScanCameras();
        }

        /// <summary>
        /// 扫描可用摄像头（0-4）
        /// </summary>
        public void ScanCameras()
        {
            Task.Run(() =>
            {
                var foundCameras = new List<int>();

                for (int i = 0; i < 5; i++)
                {
                    try
                    {
                        using var test = new VideoCapture(i, VideoCaptureAPIs.MSMF);
                        if (test.IsOpened())
                            foundCameras.Add(i);
                    }
                    catch { }
                }

                _availableCameraIndices = foundCameras;
                _config.AvailableCameraIndices = foundCameras;
                _config.AvailableCameraNames = GetCameraDeviceNames();
                _config.LastScanTime = DateTime.Now;
                _config.Save();   // 保存到全局配置

                Dispatcher.UIThread.Post(() =>
                {
                    ScanComplete?.Invoke();

                    if (_availableCameraIndices.Count > 0)
                    {
                        _currentCameraIndex = 0;
                        _config.CurrentCameraIndex = 0;
                        _config.Save();
                        if (!_connectCancelled)
                        {
                            StartCapture(_availableCameraIndices[0]);
                        }
                    }
                    else
                    {
                        ErrorOccurred?.Invoke("未检测到摄像头");
                    }
                });
            });
        }

        public List<string> GetAvailableCameraNames()
        {
            var names = new List<string>();
            var deviceNames = GetCameraDeviceNames();

            for (int i = 0; i < _availableCameraIndices.Count; i++)
            {
                int idx = _availableCameraIndices[i];
                if (deviceNames.TryGetValue(idx, out string? deviceName))
                {
                    names.Add(deviceName);
                }
                else
                {
                    names.Add($"摄像头 {idx}");
                }
            }
            return names;
        }

        public Dictionary<int, string> GetCameraDeviceNames()
        {
            var result = new Dictionary<int, string>();
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity WHERE PNPClass='Camera' OR PNPClass='Image'");
                int index = 0;
                foreach (ManagementObject obj in searcher.Get())
                {
                    string? name = obj["Name"]?.ToString();
                    if (!string.IsNullOrEmpty(name))
                    {
                        result[index] = name;
                        index++;
                    }
                }
            }
            catch
            {
            }
            return result;
        }

        public int GetCameraIndexByName(string name)
        {
            var deviceNames = GetCameraDeviceNames();
            for (int i = 0; i < _availableCameraIndices.Count; i++)
            {
                int idx = _availableCameraIndices[i];
                if (deviceNames.TryGetValue(idx, out string? deviceName) && deviceName == name)
                    return idx;
                if ($"摄像头 {idx}" == name)
                    return idx;
            }
            return -1;
        }

        /// <summary>
        /// 启动指定索引的摄像头。本方法由 UI 线程调用。摄像头初始化（new VideoCapture + 设置 + 丢弃前几帧）
        /// 下到线程池避免阻塞 UI，完成后 Post 回 UI 线程启动 DispatcherTimer。
        /// capture.Read 在 DispatcherTimer Tick 中调用——所有 OpenCV 调用单线程完成。
        /// 使用 _captureGeneration 代次计数器：新 StartCapture 递增代次使旧 Task.Run 失效，
        /// 避免旧 Task.Run 在 _cancelled 被 StartCapture 重置为 false 后继续执行，
        /// 向 UI Post 过期 VideoCapture 覆盖新 _capture（资源泄漏 + 设备冲突）。
        /// 带重试打开：从照片模式退出后摄像头驱动可能未立即释放设备，需短暂等待重试。
        /// </summary>
        public void StartCapture(int cameraIndex)
        {
            if (_connectCancelled) return;

            StopCaptureInternal();
            _cancelled = false;
            int generation = ++_captureGeneration;

            Task.Run(() =>
            {
                VideoCapture? capture = null;
                Mat? mat = null;

                try
                {
                    // 重试打开摄像头：从照片模式退出后 VideoCapture.Release 到驱动实际释放设备
                    // 可能有延迟，立即重新打开会因 "设备被占用" 失败。最多重试 5 次，每次间隔 300ms。
                    int maxRetries = 5;
                    for (int attempt = 0; attempt < maxRetries; attempt++)
                    {
                        if (_captureGeneration != generation || _connectCancelled)
                            return;

                        capture = new VideoCapture(cameraIndex, VideoCaptureAPIs.MSMF);
                        if (capture.IsOpened())
                            break;

                        capture.Dispose();
                        capture = null;

                        if (attempt < maxRetries - 1)
                            Thread.Sleep(300);
                    }

                    if (capture == null || !capture.IsOpened())
                    {
                        capture?.Dispose();
                        Dispatcher.UIThread.Post(() =>
                        {
                            ErrorOccurred?.Invoke($"无法打开摄像头 {cameraIndex}");
                        });
                        return;
                    }

                    // 设置分辨率（可选）
                    capture.Set(VideoCaptureProperties.FrameWidth, 1280);
                    capture.Set(VideoCaptureProperties.FrameHeight, 720);
                    capture.Set(VideoCaptureProperties.Fps, 30);

                    var width = (int)capture.Get(VideoCaptureProperties.FrameWidth);
                    var height = (int)capture.Get(VideoCaptureProperties.FrameHeight);

                    mat = new Mat(height, width, MatType.CV_8UC3);

                    // 丢弃前几帧（让摄像头稳定）
                    for (int i = 0; i < 5; i++)
                    {
                        if (_captureGeneration != generation)
                        {
                            capture?.Release();
                            capture?.Dispose();
                            mat?.Dispose();
                            return;
                        }
                        capture.Read(mat);
                    }

                    Dispatcher.UIThread.Post(() =>
                    {
                        lock (_lock)
                        {
                            // 代次不匹配：本次 StartCapture 已被更新的 StartCapture 取代，
                            // 释放本次资源，不覆盖 _capture（避免旧 VideoCapture 覆盖新连接）。
                            if (_captureGeneration != generation)
                            {
                                capture?.Release();
                                capture?.Dispose();
                                mat?.Dispose();
                                return;
                            }

                            _capture = capture;
                            // 单 Mat：capture.Read 在 UI 线程的 DispatcherTimer 中调用，无需双缓冲
                            _latestFrame?.Dispose();
                            _latestFrame = mat;

                            _frameWidth = width;
                            _frameHeight = height;
                            _frameStride = width * 3;

                            // 仅在尺寸变化或首次创建时重建位图，并释放旧位图。
                            // 旧实现每次 StartCapture 都新建 WriteableBitmap 且不 Dispose，
                            // 导致原生 UnmanagedBlob 泄漏，最终 Lock() 的转码分配抛 OutOfMemoryException。
                            if (_frameBitmap == null
                                || _frameBitmap.PixelSize.Width != width
                                || _frameBitmap.PixelSize.Height != height)
                            {
                                _frameBitmap?.Dispose();
                                // 使用平台原生支持的 Bgra8888 格式，避免 Avalonia 在 Lock
                                // 释放时为非原生格式（Bgr24）分配临时转码缓冲与额外 staging 内存。
                                _frameBitmap = new WriteableBitmap(
                                    new PixelSize(width, height),
                                    new Vector(96, 96),
                                    PixelFormats.Bgra8888,
                                    AlphaFormat.Premul);
                            }

                            // 不再启动后台 CaptureLoop：capture.Read 在 UI 线程 DispatcherTimer 中调用，
                            // 所有 OpenCV 调用单线程完成，消除跨线程 OpenCV 全局状态冲突。
                            StartUiTimer();

                            CameraStarted?.Invoke();
                        }
                    });
                }
                catch (Exception ex)
                {
                    capture?.Release();
                    capture?.Dispose();
                    mat?.Dispose();

                    Dispatcher.UIThread.Post(() =>
                    {
                        ErrorOccurred?.Invoke($"启动摄像头失败: {ex.Message}");
                    });
                }
            });
        }

        /// <summary>
        /// 启动 UI 定时器。30fps（33ms）：capture.Read 在 Tick 中同步调用（OpenCV 非线程安全，
        /// 必须在 UI 线程），33ms 间隔给 Read + 像素拷贝留时间，避免连帧压垮。
        /// </summary>
        private void StartUiTimer()
        {
            _uiTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(33)
            };

            _uiTimer.Tick += (_, _) => UpdateFrame();
            _uiTimer.Start();
        }

        /// <summary>
        /// 更新 UI 帧：单线程架构——capture.Read + 透视变换 + 像素拷贝都在 UI 线程完成。
        /// OpenCV 非线程安全，所有 OpenCV 调用必须在同一线程，否则全局状态冲突 → native 堆损坏。
        /// </summary>
        private unsafe void UpdateFrame()
        {
            lock (_lock)
            {
                if (_frameBitmap == null || _latestFrame == null || _cancelled || _capture == null)
                    return;

                // capture.Read 在 UI 线程调用：与下面的 DataPointer 访问同线程，无跨线程冲突
                try
                {
                    _capture.Read(_latestFrame);
                }
                catch
                {
                    // 摄像头读取异常：不抛，等下次 Tick 重试
                    return;
                }

                if (_latestFrame.Empty())
                    return;

                Mat frameToDisplay = _latestFrame;
                bool shouldDispose = false;

                // 应用透视变换（如果需要）
                if (_hasPerspectiveTransform && _perspectiveMatrix != null)
                {
                    frameToDisplay = ApplyPerspectiveTransform(_latestFrame);
                    shouldDispose = true;
                }

                using var locked = _frameBitmap.Lock();

                // 源 Mat 为 Bgr24（3 字节/像素），目标位图为 Bgra8888（4 字节/像素）。
                // 逐像素 BGR -> BGRA(A=255)，避免 Avalonia 在 Lock 释放时为非原生支持格式
                // 分配临时转码缓冲（原实现每帧约 3.69MB 分配/释放并占用额外 staging 内存）。
                byte* src = (byte*)frameToDisplay.DataPointer;
                byte* dst = (byte*)locked.Address;
                int srcStride = _frameStride;       // width * 3（源 Bgr24 步长）
                int dstStride = locked.RowBytes;     // 通常 width * 4，可能对齐填充

                for (int y = 0; y < _frameHeight; y++)
                {
                    byte* s = src + y * srcStride;
                    byte* d = dst + y * dstStride;
                    for (int x = 0; x < _frameWidth; x++)
                    {
                        d[0] = s[0]; // B
                        d[1] = s[1]; // G
                        d[2] = s[2]; // R
                        d[3] = 255;  // A 完全不透明（预乘 alpha 下等价）
                        s += 3;
                        d += 4;
                    }
                }

                if (shouldDispose)
                {
                    frameToDisplay.Dispose();
                }
            }

            // 在锁外触发事件，避免长时间持有锁阻塞捕获线程
            FrameReady?.Invoke();
        }

        /// <summary>
        /// 应用透视变换
        /// </summary>
        private Mat ApplyPerspectiveTransform(Mat input)
        {
            if (!_hasPerspectiveTransform || _perspectiveMatrix == null)
                return input;

            var output = new Mat();
            Cv2.WarpPerspective(input, output, _perspectiveMatrix, input.Size());
            return output;
        }

        /// <summary>
        /// 停止捕获。本方法由 UI 线程调用；不在本方法持锁，避免与 CaptureLoop.finally 持锁
        /// 释放资源时形成死锁（task.Wait 等 CaptureLoop 退出，CaptureLoop 退出时持锁 dispose）。
        /// </summary>
        public void StopCapture()
        {
            StopCaptureInternal();
        }

        /// <summary>
        /// 取消正在进行的连接
        /// </summary>
        public void CancelConnecting()
        {
            _connectCancelled = true;
            StopCapture();
        }

        /// <summary>
        /// 停止捕获。单线程架构下无后台任务：停 DispatcherTimer 后所有 OpenCV 调用停止，
        /// 即可持锁释放 _capture/_latestFrame（与 UpdateFrame 持锁互斥，无并发访问）。
        /// </summary>
        private void StopCaptureInternal()
        {
            _cancelled = true;
            // 递增代次：使任何在途 Task.Run 的 generation 检查失效，
            // 旧 Task.Run 会在循环检查和 UI Post 中检测到代次不匹配而自行释放资源退出。
            _captureGeneration++;

            // 先停 DispatcherTimer：UpdateFrame 不再调用 capture.Read/DataPointer，
            // 之后持锁释放 _capture/_latestFrame 才安全（不会与 UpdateFrame 并发）。
            _uiTimer?.Stop();
            _uiTimer = null;

            lock (_lock)
            {
                var capture = _capture;
                var display = _latestFrame;
                _capture = null;
                _latestFrame = null;
                try
                {
                    capture?.Release();
                    capture?.Dispose();
                    display?.Dispose();
                }
                catch { }
            }
        }

        /// <summary>
        /// 切换到下一个可用摄像头
        /// </summary>
        public void SwitchCamera()
        {
            if (_availableCameraIndices.Count <= 1)
                return;

            _currentCameraIndex = (_currentCameraIndex + 1) % _availableCameraIndices.Count;
            _config.CurrentCameraIndex = _currentCameraIndex;
            _config.Save();

            StartCapture(_availableCameraIndices[_currentCameraIndex]);
        }

        /// <summary>
        /// 切换到指定索引的摄像头
        /// </summary>
        public void SwitchToCamera(int cameraIndex)
        {
            if (_availableCameraIndices.Count == 0)
                return;

            var pos = _availableCameraIndices.IndexOf(cameraIndex);
            if (pos < 0)
                return;

            _currentCameraIndex = pos;
            _config.CurrentCameraIndex = _currentCameraIndex;
            _config.Save();

            StartCapture(cameraIndex);
        }

        // ---------- 透视变换相关 ----------
        public void SetPerspectiveTransform(Point2f[] sourcePoints, Point2f[] destPoints)
        {
            lock (_lock)
            {
                _sourcePoints = sourcePoints;
                _destinationPoints = destPoints;

                if (_sourcePoints != null && _destinationPoints != null &&
                    _sourcePoints.Length == 4 && _destinationPoints.Length == 4)
                {
                    _perspectiveMatrix?.Dispose();
                    _perspectiveMatrix = Cv2.GetPerspectiveTransform(_sourcePoints, _destinationPoints);
                    _hasPerspectiveTransform = true;
                }
                else
                {
                    _perspectiveMatrix?.Dispose();
                    _perspectiveMatrix = null;
                    _hasPerspectiveTransform = false;
                }
            }
        }

        public void ResetPerspectiveTransform()
        {
            lock (_lock)
            {
                _perspectiveMatrix?.Dispose();
                _perspectiveMatrix = null;
                _hasPerspectiveTransform = false;
                _sourcePoints = null;
                _destinationPoints = null;
            }
        }

        public Point2f[]? GetSourcePoints()
        {
            return _sourcePoints;
        }

        public Point2f[] GetDefaultSourcePoints(int width, int height)
        {
            var margin = 50;
            return new Point2f[]
            {
                new Point2f(margin, margin),
                new Point2f(width - margin, margin),
                new Point2f(width - margin, height - margin),
                new Point2f(margin, height - margin)
            };
        }

        public Point2f[] GetDefaultDestPoints(int width, int height)
        {
            return new Point2f[]
            {
                new Point2f(0, 0),
                new Point2f(width, 0),
                new Point2f(width, height),
                new Point2f(0, height)
            };
        }

        // ---------- 资源释放 ----------
        public void Dispose()
        {
            StopCapture();
            _frameBitmap?.Dispose();
            _processedFrame?.Dispose();
            _perspectiveMatrix?.Dispose();
        }
    }
}