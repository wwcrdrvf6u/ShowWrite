using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace ShowWrite
{
    /// <summary>
    /// 内存管理类，负责资源释放和垃圾回收
    /// </summary>
    public class MemoryManager : IDisposable
    {
        private readonly object _frameLock = new object();
        private Bitmap _lastProcessedFrame;
        private bool _isClosing = false;

        public bool IsClosing => _isClosing;

        public MemoryManager()
        {
            Console.WriteLine("内存管理器初始化完成");
        }

        /// <summary>
        /// 设置关闭标志
        /// </summary>
        public void SetClosing()
        {
            _isClosing = true;
        }

        /// <summary>
        /// 更新最后处理的帧
        /// </summary>
        public void UpdateLastProcessedFrame(Bitmap frame)
        {
            if (_isClosing) return;

            lock (_frameLock)
            {
                // 释放上一帧资源
                if (_lastProcessedFrame != null && !_lastProcessedFrame.Equals(frame))
                {
                    _lastProcessedFrame.Dispose();
                    _lastProcessedFrame = null;
                }

                // 保存当前帧引用
                if (frame != null)
                {
                    _lastProcessedFrame = (Bitmap)frame.Clone();
                }
            }
        }

        /// <summary>
        /// 触发内存清理
        /// </summary>
        public void TriggerMemoryCleanup()
        {
            if (_isClosing) return;

            Task.Run(() =>
            {
                try
                {
                    // 清理未使用的位图资源
                    if (_lastProcessedFrame != null)
                    {
                        lock (_frameLock)
                        {
                            _lastProcessedFrame?.Dispose();
                            _lastProcessedFrame = null;
                        }
                    }

                    // 强制垃圾回收
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();

                    // 记录内存状态（调试用）
                    var memory = GC.GetTotalMemory(false) / 1024 / 1024;
                    Console.WriteLine($"内存清理完成，当前内存使用: {memory} MB");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"内存清理错误: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 释放帧资源
        /// </summary>
        public void DisposeFrame(Bitmap frame, bool forceDispose = false)
        {
            if (frame == null) return;

            try
            {
                if (forceDispose || !frame.Equals(_lastProcessedFrame))
                {
                    frame.Dispose();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"释放帧资源失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 清理所有资源
        /// </summary>
        public void CleanupAllResources()
        {
            try
            {
                Console.WriteLine("开始清理所有内存资源...");

                SetClosing();

                lock (_frameLock)
                {
                    _lastProcessedFrame?.Dispose();
                    _lastProcessedFrame = null;
                }

                // 强制垃圾回收
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                Console.WriteLine("内存资源清理完成");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"清理内存资源失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 将 Bitmap 转换为 BitmapImage（优化内存使用）
        /// </summary>
        public BitmapImage BitmapToBitmapImage(Bitmap bitmap)
        {
            if (bitmap == null) return null;

            try
            {
                using var memory = new System.IO.MemoryStream();
                bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Bmp);
                memory.Position = 0;

                var bmpImage = new BitmapImage();
                bmpImage.BeginInit();
                bmpImage.CacheOption = BitmapCacheOption.OnLoad;
                bmpImage.StreamSource = memory;
                bmpImage.EndInit();
                bmpImage.Freeze(); // 冻结对象以提高性能

                return bmpImage;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Bitmap转换失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 获取当前内存使用情况
        /// </summary>
        public string GetMemoryUsage()
        {
            var memory = GC.GetTotalMemory(false) / 1024 / 1024;
            return $"{memory} MB";
        }

        public void Dispose()
        {
            CleanupAllResources();
        }
    }
}