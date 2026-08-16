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
        private CancellationTokenSource? _cts;
        private Task? _captureTask;
        private volatile bool _cancelled = false;
        private readonly object _lock = new();
        private volatile bool _connectCancelled = false;

        private WriteableBitmap? _frameBitmap;
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
        /// 获取处理后的帧（应用了透视变换）
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
                return _latestFrame;
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
        /// 启动指定索引的摄像头
        /// </summary>
        public void StartCapture(int cameraIndex)
        {
            if (_connectCancelled) return;

            lock (_lock)
            {
                StopCaptureInternal();
                _cancelled = false;
            }

            Task.Run(() =>
            {
                VideoCapture? capture = null;
                Mat? mat = null;

                try
                {
                    capture = new VideoCapture(cameraIndex, VideoCaptureAPIs.MSMF);

                    if (!capture.IsOpened())
                    {
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
                        if (_cancelled)
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
                            if (_cancelled)
                            {
                                capture?.Release();
                                capture?.Dispose();
                                mat?.Dispose();
                                return;
                            }

                            _capture = capture;
                            _latestFrame = mat;

                            _frameWidth = width;
                            _frameHeight = height;
                            _frameStride = width * 3;

                            _frameBitmap = new WriteableBitmap(
                                new PixelSize(width, height),
                                new Vector(96, 96),
                                PixelFormats.Bgr24);

                            _cts = new CancellationTokenSource();
                            _captureTask = Task.Run(() => CaptureLoop(_cts.Token));

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
        /// 捕获循环（后台线程）
        /// </summary>
        private void CaptureLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    lock (_lock)
                    {
                        if (_capture == null || _latestFrame == null || _cancelled)
                            break;
                        _capture.Read(_latestFrame);
                    }
                    // 控制帧率约 60fps
                    Thread.Sleep(16);
                }
                catch (Exception ex)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        ErrorOccurred?.Invoke($"捕获循环异常: {ex.Message}");
                    });
                    break;
                }
            }
        }

        /// <summary>
        /// 启动 UI 定时器，定期将最新帧绘制到 WriteableBitmap
        /// </summary>
        private void StartUiTimer()
        {
            _uiTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16) // 约 60fps
            };

            _uiTimer.Tick += (_, _) => UpdateFrame();
            _uiTimer.Start();
        }

        /// <summary>
        /// 更新 UI 帧（应用透视变换并复制到 Bitmap）
        /// </summary>
        private unsafe void UpdateFrame()
        {
            lock (_lock)
            {
                if (_frameBitmap == null || _latestFrame == null || _cancelled)
                    return;

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

                var srcPtr = (void*)frameToDisplay.DataPointer;
                var dstPtr = (void*)locked.Address;

                if (locked.RowBytes == _frameStride)
                {
                    Buffer.MemoryCopy(srcPtr, dstPtr,
                        _frameStride * _frameHeight,
                        _frameStride * _frameHeight);
                }
                else
                {
                    for (int i = 0; i < _frameHeight; i++)
                    {
                        Buffer.MemoryCopy(
                            (byte*)srcPtr + i * _frameStride,
                            (byte*)dstPtr + i * locked.RowBytes,
                            _frameStride,
                            _frameStride);
                    }
                }

                if (shouldDispose)
                {
                    frameToDisplay.Dispose();
                }

                FrameReady?.Invoke();
            }
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
        /// 停止捕获
        /// </summary>
        public void StopCapture()
        {
            lock (_lock)
            {
                StopCaptureInternal();
            }
        }

        /// <summary>
        /// 取消正在进行的连接
        /// </summary>
        public void CancelConnecting()
        {
            _connectCancelled = true;
            StopCapture();
        }

        private void StopCaptureInternal()
        {
            _cancelled = true;

            _uiTimer?.Stop();
            _uiTimer = null;

            _cts?.Cancel();
            _captureTask = null;

            _cts?.Dispose();
            _cts = null;

            _capture?.Release();
            _capture?.Dispose();
            _capture = null;

            _latestFrame?.Dispose();
            _latestFrame = null;
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