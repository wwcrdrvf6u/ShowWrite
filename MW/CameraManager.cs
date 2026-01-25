using AForge;
using AForge.Imaging.Filters;
using ShowWrite.Models;
using ShowWrite.Services;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;

namespace ShowWrite
{
    /// <summary>
    /// 摄像头管理类
    /// </summary>
    public class CameraManager : IDisposable
    {
        private readonly VideoService _videoService;
        private QuadrilateralTransformation _perspectiveCorrectionFilter;
        private AppConfig _config;

        private int _currentCameraIndex = 0;
        private bool _cameraAvailable = true;
        private bool _cameraStoppedInPhotoMode = false;

        // 画面调节参数
        private double _brightness = 0.0;
        private double _contrast = 0.0;
        private int _rotation = 0;
        private bool _mirrorHorizontal = false;
        private bool _mirrorVertical = false;

        // 状态管理
        private bool _isPaused = false;
        private bool _isDisposed = false;
        private readonly object _disposeLock = new();

        // 事件处理
        private EventHandler _frameEventHandler;

        public event Action<Bitmap> OnNewFrameProcessed;
        public bool IsCameraAvailable => _cameraAvailable && !_isDisposed;
        public int CurrentCameraIndex => _currentCameraIndex;
        public bool IsRunning => _videoService != null && !_isDisposed && !_isPaused && _cameraAvailable && !_cameraStoppedInPhotoMode;

        public CameraManager(VideoService videoService, AppConfig config)
        {
            _videoService = videoService ?? throw new ArgumentNullException(nameof(videoService));
            _config = config ?? throw new ArgumentNullException(nameof(config));

            // 创建帧事件处理器（避免lambda表达式导致的内存泄漏）
            _frameEventHandler = (sender, e) =>
            {
                try
                {
                    var frame = _videoService.GetFrameCopy();
                    if (frame != null)
                    {
                        OnNewFrameProcessed?.Invoke(frame);
                        frame.Dispose(); // 重要：使用后立即释放
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("CameraManager", $"处理帧事件失败: {ex.Message}", ex);
                }
            };

            // 订阅视频服务的事件
            _videoService.OnNewFrameProcessed += HandleVideoServiceFrame;
        }

        /// <summary>
        /// 视频服务帧事件处理
        /// </summary>
        private void HandleVideoServiceFrame(Bitmap frame)
        {
            if (_isDisposed || _isPaused)
            {
                frame?.Dispose();
                return;
            }

            try
            {
                OnNewFrameProcessed?.Invoke(frame);
            }
            catch (Exception ex)
            {
                Logger.Error("CameraManager", $"处理视频帧失败: {ex.Message}", ex);
            }
            finally
            {
                frame?.Dispose();
            }
        }

        /// <summary>
        /// 获取当前摄像头名称
        /// </summary>
        public string GetCurrentCameraName()
        {
            try
            {
                if (_isDisposed) return "未知摄像头";

                var cameras = _videoService.GetAvailableCameras();
                if (cameras.Count > _currentCameraIndex && _currentCameraIndex >= 0)
                {
                    return cameras[_currentCameraIndex];
                }

                return "摄像头 " + _currentCameraIndex;
            }
            catch (Exception ex)
            {
                Logger.Error("CameraManager", $"获取摄像头名称失败: {ex.Message}", ex);
                return "摄像头 " + _currentCameraIndex;
            }
        }

        /// <summary>
        /// 检查摄像头可用性
        /// </summary>
        public bool CheckCameraAvailability()
        {
            try
            {
                if (_isDisposed) return false;

                var cameras = _videoService.GetAvailableCameras();
                _cameraAvailable = cameras != null && cameras.Count > 0;

                if (!_cameraAvailable)
                {
                    Logger.Warning("CameraManager", "未检测到可用摄像头");
                }
                else
                {
                    Logger.Info("CameraManager", $"检测到 {cameras.Count} 个摄像头");
                }

                return _cameraAvailable;
            }
            catch (Exception ex)
            {
                Logger.Error("CameraManager", $"检查摄像头可用性失败: {ex.Message}", ex);
                _cameraAvailable = false;
                return false;
            }
        }

        /// <summary>
        /// 启动摄像头
        /// </summary>
        public bool StartCamera()
        {
            if (!_cameraAvailable || _isDisposed) return false;

            try
            {
                if (!_videoService.Start(_currentCameraIndex))
                {
                    Logger.Error("CameraManager", $"摄像头启动失败，索引: {_currentCameraIndex}");
                    _cameraAvailable = false;
                    return false;
                }

                Logger.Info("CameraManager", $"摄像头启动成功，索引: {_currentCameraIndex}");
                _cameraStoppedInPhotoMode = false;
                _isPaused = false;
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("CameraManager", $"摄像头启动异常: {ex.Message}", ex);
                _cameraAvailable = false;
                return false;
            }
        }

        /// <summary>
        /// 停止摄像头
        /// </summary>
        public void StopCamera()
        {
            try
            {
                if (_isDisposed) return;

                _videoService.Stop();
                _cameraStoppedInPhotoMode = true;
                _isPaused = false;
                Logger.Info("CameraManager", "摄像头已停止");
            }
            catch (Exception ex)
            {
                Logger.Error("CameraManager", $"停止摄像头失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 暂停摄像头
        /// </summary>
        public void PauseCamera()
        {
            if (!IsCameraAvailable || _isDisposed) return;

            try
            {
                _videoService.Stop();
                _isPaused = true;
                Logger.Info("CameraManager", "摄像头已暂停");
            }
            catch (Exception ex)
            {
                Logger.Error("CameraManager", $"暂停摄像头失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 恢复摄像头
        /// </summary>
        public void ResumeCamera()
        {
            if (!IsCameraAvailable || _isDisposed) return;

            try
            {
                if (_isPaused)
                {
                    if (!StartCamera())
                    {
                        _cameraAvailable = false;
                        Logger.Error("CameraManager", "恢复摄像头失败");
                    }
                    else
                    {
                        _isPaused = false;
                        Logger.Info("CameraManager", "摄像头已恢复");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("CameraManager", $"恢复摄像头失败: {ex.Message}", ex);
                _cameraAvailable = false;
            }
        }

        /// <summary>
        /// 释放摄像头资源
        /// </summary>
        public void ReleaseCameraResources()
        {
            try
            {
                if (_isDisposed) return;

                Logger.Info("CameraManager", "开始释放摄像头资源...");

                if (_cameraAvailable)
                {
                    StopCamera();

                    // 等待摄像头完全停止
                    if (!_videoService.WaitForStop(3000))
                    {
                        Logger.Warning("CameraManager", "摄像头停止超时，强制停止");
                        _videoService.ForceStop();
                    }

                    Logger.Info("CameraManager", "摄像头资源已释放");
                }
                else
                {
                    Logger.Info("CameraManager", "摄像头不可用，无需释放");
                }
            }
            catch (Exception ex)
            {
                Logger.Error("CameraManager", $"释放摄像头资源失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 重启摄像头
        /// </summary>
        public bool RestartCamera()
        {
            try
            {
                if (_isDisposed) return false;

                if ((_cameraStoppedInPhotoMode || _isPaused) && _cameraAvailable)
                {
                    Logger.Info("CameraManager", "重新启动摄像头...");
                    if (!StartCamera())
                    {
                        _cameraAvailable = false;
                        return false;
                    }
                    Logger.Info("CameraManager", "摄像头已重新启动");
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Logger.Error("CameraManager", $"重启摄像头失败: {ex.Message}", ex);
                _cameraAvailable = false;
                return false;
            }
        }

        /// <summary>
        /// 切换摄像头
        /// </summary>
        public bool SwitchCamera(int newCameraIndex)
        {
            try
            {
                if (_isDisposed) return false;

                var cameras = _videoService.GetAvailableCameras();
                if (cameras.Count == 0 || newCameraIndex >= cameras.Count)
                {
                    return false;
                }

                // 停止当前摄像头
                StopCamera();

                // 清除当前矫正滤镜
                _perspectiveCorrectionFilter = null;

                // 更新摄像头索引
                _currentCameraIndex = newCameraIndex;

                // 启动新摄像头
                return StartCamera();
            }
            catch (Exception ex)
            {
                Logger.Error("CameraManager", $"切换摄像头失败: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// 获取当前帧
        /// </summary>
        public Bitmap GetCurrentFrame()
        {
            if (_isDisposed) return null;
            return _videoService.GetFrameCopy();
        }

        /// <summary>
        /// 获取当前帧（不应用校正）
        /// </summary>
        public Bitmap GetCurrentFrameWithoutCorrection()
        {
            if (_isDisposed) return null;
            return _videoService.GetFrameCopy();
        }

        /// <summary>
        /// 自动对焦
        /// </summary>
        public void AutoFocus()
        {
            if (_cameraAvailable && !_isDisposed)
            {
                _videoService.AutoFocus();
            }
        }

        /// <summary>
        /// 处理视频帧（应用校正和调节）
        /// </summary>
        public Bitmap ProcessFrame(Bitmap src, bool applyAdjustments = true)
        {
            if (src == null || _isDisposed) return src;

            Bitmap work = src;
            try
            {
                // 1) 透视校正
                if (_perspectiveCorrectionFilter != null)
                {
                    var corrected = _perspectiveCorrectionFilter.Apply(work);
                    if (!ReferenceEquals(work, src)) work.Dispose();
                    work = corrected;
                }

                if (!applyAdjustments) return work;

                // 2) 亮度/对比度
                if (Math.Abs(_brightness) > 0.01)
                {
                    var bc = new BrightnessCorrection((int)Math.Max(-100, Math.Min(100, _brightness)));
                    bc.ApplyInPlace(work);
                }
                if (Math.Abs(_contrast) > 0.01)
                {
                    var cc = new ContrastCorrection((int)Math.Max(-100, Math.Min(100, _contrast)));
                    cc.ApplyInPlace(work);
                }

                // 3) 旋转
                if (_rotation == 90) work.RotateFlip(RotateFlipType.Rotate90FlipNone);
                else if (_rotation == 180) work.RotateFlip(RotateFlipType.Rotate180FlipNone);
                else if (_rotation == 270) work.RotateFlip(RotateFlipType.Rotate270FlipNone);

                // 4) 镜像
                if (_mirrorHorizontal) work.RotateFlip(RotateFlipType.RotateNoneFlipX);
                if (_mirrorVertical) work.RotateFlip(RotateFlipType.RotateNoneFlipY);

                return work;
            }
            catch
            {
                if (!ReferenceEquals(work, src)) work.Dispose();
                return src;
            }
        }

        /// <summary>
        /// 设置画面调节参数
        /// </summary>
        public void SetVideoAdjustments(double brightness, double contrast, int rotation, bool mirrorH, bool mirrorV)
        {
            if (_isDisposed) return;

            _brightness = brightness;
            _contrast = contrast;
            _rotation = rotation;
            _mirrorHorizontal = mirrorH;
            _mirrorVertical = mirrorV;
        }

        /// <summary>
        /// 设置透视校正滤镜
        /// </summary>
        public void SetPerspectiveCorrectionFilter(QuadrilateralTransformation filter)
        {
            if (_isDisposed) return;
            _perspectiveCorrectionFilter = filter;
        }

        /// <summary>
        /// 清除透视校正
        /// </summary>
        public void ClearPerspectiveCorrection()
        {
            if (_isDisposed) return;
            _perspectiveCorrectionFilter = null;
        }

        /// <summary>
        /// 获取可用摄像头列表
        /// </summary>
        public List<string> GetAvailableCameras()
        {
            if (_isDisposed) return new List<string>();
            return _videoService.GetAvailableCameras();
        }

        /// <summary>
        /// 加载摄像头矫正信息（更新为使用新的 CameraConfig 结构）
        /// </summary>
        public void LoadCorrectionForCurrentCamera()
        {
            if (_isDisposed) return;

            var cameraConfig = GetCurrentCameraConfig();
            if (cameraConfig != null && cameraConfig.HasCorrection && cameraConfig.PerspectivePoints != null && cameraConfig.PerspectivePoints.Count == 4)
            {
                try
                {
                    var correctionPoints = cameraConfig.GetCorrectionPoints();
                    _perspectiveCorrectionFilter = new QuadrilateralTransformation(
                        correctionPoints,
                        cameraConfig.SourceWidth,
                        cameraConfig.SourceHeight);

                    Logger.Info("CameraManager", $"已加载摄像头 {_currentCameraIndex} ({cameraConfig.CameraName}) 的透视校正配置");
                }
                catch (Exception ex)
                {
                    Logger.Error("CameraManager", $"加载摄像头 {_currentCameraIndex} 的透视校正失败: {ex.Message}", ex);
                    _perspectiveCorrectionFilter = null;
                }
            }
            else
            {
                _perspectiveCorrectionFilter = null;
                Logger.Info("CameraManager", $"摄像头 {_currentCameraIndex} 无透视校正配置");
            }
        }

        /// <summary>
        /// 获取当前摄像头的配置
        /// </summary>
        public CameraConfig GetCurrentCameraConfig()
        {
            if (_isDisposed) return null;

            if (_config?.CameraConfigs != null && _config.CameraConfigs.ContainsKey(_currentCameraIndex))
            {
                return _config.CameraConfigs[_currentCameraIndex];
            }
            return null;
        }

        /// <summary>
        /// 保存当前摄像头矫正信息（更新为使用新的 CameraConfig 结构）
        /// </summary>
        public void SaveCurrentCameraCorrection(List<IntPoint> points, int sourceWidth, int sourceHeight, int originalWidth, int originalHeight)
        {
            if (_isDisposed) return;

            var cameraName = GetCurrentCameraName();

            // 确保 CameraConfigs 字典被初始化
            if (_config?.CameraConfigs == null)
                _config.CameraConfigs = new Dictionary<int, CameraConfig>();

            // 创建或更新摄像头配置
            if (!_config.CameraConfigs.ContainsKey(_currentCameraIndex))
            {
                _config.CameraConfigs[_currentCameraIndex] = new CameraConfig
                {
                    CameraIndex = _currentCameraIndex,
                    CameraName = cameraName
                };
            }

            // 更新校正配置
            _config.CameraConfigs[_currentCameraIndex].SetCorrectionPoints(points);
            _config.CameraConfigs[_currentCameraIndex].SourceWidth = sourceWidth;
            _config.CameraConfigs[_currentCameraIndex].SourceHeight = sourceHeight;
            _config.CameraConfigs[_currentCameraIndex].OriginalCameraWidth = originalWidth;
            _config.CameraConfigs[_currentCameraIndex].OriginalCameraHeight = originalHeight;
            _config.CameraConfigs[_currentCameraIndex].HasCorrection = true;

            Logger.Info("CameraManager", $"已保存摄像头 {_currentCameraIndex} ({cameraName}) 的校正配置");
        }

        /// <summary>
        /// 清除当前摄像头矫正信息
        /// </summary>
        public void ClearCurrentCameraCorrection()
        {
            if (_isDisposed) return;

            if (_config?.CameraConfigs != null && _config.CameraConfigs.ContainsKey(_currentCameraIndex))
            {
                _config.CameraConfigs[_currentCameraIndex].ClearCorrection();
                Logger.Info("CameraManager", $"已清除摄像头 {_currentCameraIndex} 的校正配置");
            }
        }

        /// <summary>
        /// 应用摄像头配置（包括校正和画面调整）
        /// </summary>
        public void ApplyCameraConfig(CameraConfig cameraConfig)
        {
            if (_isDisposed || cameraConfig == null) return;

            try
            {
                // 1. 应用画面调整参数
                if (cameraConfig.Adjustments != null)
                {
                    var adjustments = cameraConfig.Adjustments;
                    _brightness = (adjustments.Brightness - 100) / 100.0 * 50; // 转换为-50到50的范围
                    _contrast = (adjustments.Contrast - 100) / 100.0 * 50; // 转换为-50到50的范围
                    _rotation = adjustments.Orientation;
                    _mirrorHorizontal = adjustments.FlipHorizontal;

                    Logger.Info("CameraManager", $"已应用画面调整: 亮度={adjustments.Brightness}, 对比度={adjustments.Contrast}, 旋转={adjustments.Orientation}°, 水平翻转={adjustments.FlipHorizontal}");
                }

                // 2. 应用透视校正
                if (cameraConfig.HasCorrection &&
                    cameraConfig.PerspectivePoints != null &&
                    cameraConfig.PerspectivePoints.Count == 4)
                {
                    var correctionPoints = cameraConfig.GetCorrectionPoints();
                    _perspectiveCorrectionFilter = new QuadrilateralTransformation(
                        correctionPoints,
                        cameraConfig.SourceWidth,
                        cameraConfig.SourceHeight);

                    Logger.Info("CameraManager", $"已应用透视校正: 源尺寸={cameraConfig.SourceWidth}x{cameraConfig.SourceHeight}");
                }

                Logger.Info("CameraManager", $"已成功应用摄像头 {_currentCameraIndex} ({cameraConfig.CameraName}) 的配置");
            }
            catch (Exception ex)
            {
                Logger.Error("CameraManager", $"应用摄像头配置失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 强制释放所有资源
        /// </summary>
        public void ForceReleaseAllResources()
        {
            Dispose();
        }

        /// <summary>
        /// 取消所有事件订阅
        /// </summary>
        private void UnsubscribeAllEvents()
        {
            try
            {
                if (_videoService != null)
                {
                    _videoService.OnNewFrameProcessed -= HandleVideoServiceFrame;
                    _videoService.ClearAllEventSubscriptions();
                }

                OnNewFrameProcessed = null;
                Logger.Info("CameraManager", "所有事件订阅已取消");
            }
            catch (Exception ex)
            {
                Logger.Error("CameraManager", $"取消事件订阅失败: {ex.Message}", ex);
            }
        }

        #region IDisposable 实现

        public void Dispose()
        {
            lock (_disposeLock)
            {
                if (_isDisposed) return;
                _isDisposed = true;
            }

            Logger.Info("CameraManager", "开始释放资源...");

            try
            {
                // 1. 取消所有事件订阅
                UnsubscribeAllEvents();

                // 2. 释放摄像头资源
                ReleaseCameraResources();

                // 3. 清理引用
                _config = null;
                _perspectiveCorrectionFilter = null;

                // 4. 强制垃圾回收
                GC.Collect();
                GC.WaitForPendingFinalizers();

                Logger.Info("CameraManager", "资源释放完成");
            }
            catch (Exception ex)
            {
                Logger.Error("CameraManager", $"释放资源时出错: {ex.Message}", ex);
            }
        }

        // 析构函数
        ~CameraManager()
        {
            if (!_isDisposed)
            {
                Logger.Warning("CameraManager", "析构函数被调用，资源未正确释放");
                Dispose();
            }
        }

        #endregion
    }
}