using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using AForge.Video;
using AForge.Video.DirectShow;
using ShowWrite;

namespace ShowWrite.Services
{
    public sealed class VideoService : IDisposable
    {
        private VideoCaptureDevice? _device;
        private readonly object _frameLock = new();
        private Bitmap? _current;
        private DateTime _last = DateTime.MinValue;

        // 停止超时和状态管理
        private readonly object _stopLock = new();
        private bool _isStopping = false;
        private ManualResetEvent _stopCompleteEvent = new(false);

        // 帧率限制
        public const double MinFrameIntervalMs = 100;

        private bool _isDisposed = false;

        // 修复事件声明 - 正确的事件声明方式
        public event Action<Bitmap>? OnNewFrameForUI;
        public event Action<Bitmap>? OnNewFrameProcessed;

        /// <summary>
        /// 启动摄像头
        /// </summary>
        public bool Start(int cameraIndex)
        {
            if (_isDisposed) return false;

            lock (_stopLock)
            {
                if (_isStopping) return false;
            }

            var devices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
            if (devices.Count == 0) return false;

            if (cameraIndex < 0 || cameraIndex >= devices.Count)
                cameraIndex = 0;

            try
            {
                // 确保之前的设备完全停止
                InternalStop(true);

                _device = new VideoCaptureDevice(devices[cameraIndex].MonikerString);
                _device.NewFrame += Device_NewFrame;
                _device.Start();

                _stopCompleteEvent.Reset();
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("VideoService", $"启动摄像头失败: {ex.Message}", ex);
                return false;
            }
        }

        private void Device_NewFrame(object? sender, NewFrameEventArgs e)
        {
            lock (_stopLock)
            {
                if (_isDisposed || _isStopping) return;
            }

            // 帧率限制
            if ((DateTime.Now - _last).TotalMilliseconds < MinFrameIntervalMs)
                return;
            _last = DateTime.Now;

            Bitmap? newFrame = null;

            try
            {
                // Clone 当前帧
                newFrame = (Bitmap)e.Frame.Clone();

                // 更新当前帧缓存
                lock (_frameLock)
                {
                    _current?.Dispose();
                    _current = newFrame;
                    newFrame = null; // 移交所有权
                }

                // 通知外部（外部 Clone/Dispose）
                try
                {
                    OnNewFrameProcessed?.Invoke(GetFrameCopy()!);
                }
                catch (Exception ex)
                {
                    Logger.Error("VideoService", $"调用帧处理事件失败: {ex.Message}", ex);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("VideoService", $"处理新帧时出错: {ex.Message}", ex);
            }
            finally
            {
                // 确保未使用的临时对象被释放
                newFrame?.Dispose();
            }
        }

        /// <summary>
        /// 内部停止方法
        /// </summary>
        private void InternalStop(bool waitForStop = true)
        {
            if (_device == null) return;

            try
            {
                // 移除事件处理器
                _device.NewFrame -= Device_NewFrame;

                // 检查是否正在运行
                if (_device.IsRunning)
                {
                    _device.SignalToStop();

                    if (waitForStop)
                    {
                        // 等待停止，最多5秒
                        for (int i = 0; i < 50; i++)
                        {
                            if (!_device.IsRunning) break;
                            Thread.Sleep(100);
                        }

                        // 如果仍未停止，强制停止
                        if (_device.IsRunning)
                        {
                            try
                            {
                                _device.Stop();
                            }
                            catch { }
                        }
                    }
                    else
                    {
                        // 不等待，直接强制停止
                        try
                        {
                            _device.Stop();
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("VideoService", $"停止摄像头时出错: {ex.Message}", ex);
            }
            finally
            {
                _device = null;
                _stopCompleteEvent.Set();
            }
        }

        /// <summary>
        /// 停止摄像头
        /// </summary>
        public void Stop()
        {
            lock (_stopLock)
            {
                if (_isStopping) return;
                _isStopping = true;
            }

            InternalStop();

            lock (_stopLock)
            {
                _isStopping = false;
            }
        }

        /// <summary>
        /// 强制停止（不等待）
        /// </summary>
        public void ForceStop()
        {
            lock (_stopLock)
            {
                if (_isStopping) return;
                _isStopping = true;
            }

            InternalStop(false);

            lock (_stopLock)
            {
                _isStopping = false;
            }
        }

        /// <summary>
        /// 外部可随时复制当前帧
        /// </summary>
        public Bitmap? GetFrameCopy()
        {
            if (_isDisposed) return null;

            lock (_frameLock)
            {
                return _current == null ? null : new Bitmap(_current);
            }
        }

        /// <summary>
        /// 自动对焦
        /// </summary>
        public void AutoFocus()
        {
            if (_device == null || _isDisposed) return;

            try
            {
                _device.SetCameraProperty(
                    CameraControlProperty.Focus,
                    0,
                    CameraControlFlags.Auto
                );
            }
            catch (Exception ex)
            {
                Logger.Error("VideoService", $"自动对焦失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 等待停止完成
        /// </summary>
        public bool WaitForStop(int timeoutMs = 5000)
        {
            return _stopCompleteEvent.WaitOne(timeoutMs);
        }

        /// <summary>
        /// 获取可用摄像头列表
        /// </summary>
        public List<string> GetAvailableCameras()
        {
            var list = new List<string>();
            if (_isDisposed) return list;

            try
            {
                var devices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
                foreach (FilterInfo fi in devices)
                    list.Add(fi.Name);
            }
            catch (Exception ex)
            {
                Logger.Error("VideoService", $"获取摄像头列表失败: {ex.Message}", ex);
            }

            return list;
        }

        /// <summary>
        /// 清除所有事件订阅
        /// </summary>
        public void ClearAllEventSubscriptions()
        {
            // 由于事件使用字段存储，我们可以通过赋值为 null 来清除所有订阅
            OnNewFrameProcessed = null;
            OnNewFrameForUI = null;
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            Logger.Info("VideoService", "开始释放资源...");

            try
            {
                // 1. 清除事件订阅
                ClearAllEventSubscriptions();

                // 2. 停止设备
                lock (_stopLock)
                {
                    _isStopping = true;
                }

                InternalStop(false);

                // 3. 释放当前帧
                lock (_frameLock)
                {
                    _current?.Dispose();
                    _current = null;
                }

                // 4. 等待停止完成
                _stopCompleteEvent?.WaitOne(3000);
                _stopCompleteEvent?.Dispose();
                _stopCompleteEvent = null;

                Logger.Info("VideoService", "资源释放完成");
            }
            catch (Exception ex)
            {
                Logger.Error("VideoService", $"释放资源时出错: {ex.Message}", ex);
            }
        }

        // 析构函数，以防 Dispose 未被调用
        ~VideoService()
        {
            if (!_isDisposed)
            {
                Logger.Warning("VideoService", "析构函数被调用，资源未正确释放");
                Dispose();
            }
        }
    }
}