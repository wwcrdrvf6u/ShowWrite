using System;
using System.Collections.Generic;
using System.Drawing;
using AForge.Video;
using AForge.Video.DirectShow;

namespace ShowWrite.Services
{
    public sealed class VideoService : IDisposable
    {
        private VideoCaptureDevice? _device;
        private readonly object _frameLock = new();
        private Bitmap? _current;
        private DateTime _last = DateTime.MinValue;
        public const double MinFrameIntervalMs = 33; // ~30fps
        private bool _isDisposed = false;

        public event Action<Bitmap>? OnNewFrameProcessed; // 已限制频率

        public bool Start(int cameraIndex)
        {
            if (_isDisposed) return false;

            var devices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
            if (devices.Count == 0) return false;
            if (cameraIndex < 0 || cameraIndex >= devices.Count) cameraIndex = 0;

            try
            {
                _device = new VideoCaptureDevice(devices[cameraIndex].MonikerString);
                _device.NewFrame += Device_NewFrame;
                _device.Start();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"启动摄像头失败: {ex.Message}");
                return false;
            }
        }

        private void Device_NewFrame(object? sender, NewFrameEventArgs e)
        {
            if (_isDisposed) return;

            double elapsed = (DateTime.Now - _last).TotalMilliseconds;
            if (elapsed < MinFrameIntervalMs) return;
            _last = DateTime.Now;

            Bitmap? frameCopy = null;
            try
            {
                frameCopy = (Bitmap)e.Frame.Clone();

                lock (_frameLock)
                {
                    _current?.Dispose();
                    _current = frameCopy;
                    frameCopy = null; // 防止在finally中再次释放
                }

                // 安全地触发事件
                var handler = OnNewFrameProcessed;
                if (handler != null)
                {
                    var currentFrame = GetFrameCopy();
                    if (currentFrame != null)
                    {
                        try
                        {
                            handler(currentFrame);
                        }
                        finally
                        {
                            currentFrame.Dispose();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"处理新帧时出错: {ex.Message}");
            }
            finally
            {
                frameCopy?.Dispose();
            }
        }

        public Bitmap? GetFrameCopy()
        {
            if (_isDisposed) return null;

            lock (_frameLock)
            {
                return _current == null ? null : new Bitmap(_current);
            }
        }

        public void AutoFocus() //自动对焦
        {
            if (_device == null || _isDisposed) return;

            try
            {
                _device.SetCameraProperty(
                    CameraControlProperty.Focus,
                    0,
                    CameraControlFlags.Auto  // 打开自动对焦
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine($"自动对焦失败: {ex.Message}");
            }
        }

        public void Stop()
        {
            if (_device != null && !_isDisposed)
            {
                try
                {
                    _device.SignalToStop();
                    _device.WaitForStop();
                    _device.NewFrame -= Device_NewFrame;
                    _device = null;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"停止摄像头时出错: {ex.Message}");
                }
            }
        }

        public List<string> GetAvailableCameras()
        {
            if (_isDisposed) return new List<string>();

            var devices = new List<string>();
            var deviceCollection = new FilterInfoCollection(FilterCategory.VideoInputDevice);

            for (int i = 0; i < deviceCollection.Count; i++)
            {
                try
                {
                    devices.Add(deviceCollection[i].Name);
                }
                catch
                {
                    break;
                }
            }
            return devices;
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            Stop();

            lock (_frameLock)
            {
                _current?.Dispose();
                _current = null;
            }

            // 移除所有事件订阅者
            OnNewFrameProcessed = null;
        }
    }
}