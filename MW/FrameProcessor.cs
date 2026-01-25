using AForge;
using AForge.Imaging.Filters;
using System;
using System.Drawing;
using System.Windows.Media.Imaging;
using ZXing;
using ZXing.Common;

namespace ShowWrite
{
    /// <summary>
    /// 视频帧处理类，负责各种图像处理操作
    /// </summary>
    public class FrameProcessor
    {
        private readonly CameraManager _cameraManager;
        private readonly MemoryManager _memoryManager;

        public FrameProcessor(CameraManager cameraManager, MemoryManager memoryManager)
        {
            _cameraManager = cameraManager;
            _memoryManager = memoryManager;
        }

        /// <summary>
        /// 处理帧并转换为 BitmapImage
        /// </summary>
        public BitmapImage ProcessFrameToBitmapImage(Bitmap sourceFrame, bool applyAdjustments = true)
        {
            if (sourceFrame == null) return null;

            Bitmap processedFrame = null;
            try
            {
                // 使用 CameraManager 处理帧
                processedFrame = _cameraManager.ProcessFrame(sourceFrame, applyAdjustments);

                // 转换为 BitmapImage
                var bitmapImage = _memoryManager.BitmapToBitmapImage(processedFrame);

                return bitmapImage;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"帧处理失败: {ex.Message}");
                return null;
            }
            finally
            {
                // 释放处理后的帧（如果与源帧不同）
                if (processedFrame != null && !ReferenceEquals(processedFrame, sourceFrame))
                {
                    processedFrame.Dispose();
                }
            }
        }

        /// <summary>
        /// 扫描二维码/条码
        /// </summary>
        public Result DecodeBarcodeFromBitmap(Bitmap src)
        {
            if (src == null) return null;

            using var bmp24 = new Bitmap(src.Width, src.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(bmp24))
            {
                g.DrawImage(src, 0, 0, bmp24.Width, bmp24.Height);
            }

            var rect = new Rectangle(0, 0, bmp24.Width, bmp24.Height);
            var data = bmp24.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly,
                                    System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            try
            {
                int stride = Math.Abs(data.Stride);
                int length = stride * bmp24.Height;
                byte[] buffer = new byte[length];
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buffer, 0, length);

                var luminance = new RGBLuminanceSource(buffer, bmp24.Width, bmp24.Height,
                                                     RGBLuminanceSource.BitmapFormat.BGR24);
                var binary = new BinaryBitmap(new HybridBinarizer(luminance));
                var reader = new MultiFormatReader();

                var hints = new System.Collections.Generic.Dictionary<DecodeHintType, object>
                {
                    { DecodeHintType.TRY_HARDER, true },
                    { DecodeHintType.POSSIBLE_FORMATS, new[]
                        {
                            BarcodeFormat.QR_CODE, BarcodeFormat.DATA_MATRIX, BarcodeFormat.AZTEC,
                            BarcodeFormat.PDF_417, BarcodeFormat.CODE_128, BarcodeFormat.CODE_39,
                            BarcodeFormat.EAN_13, BarcodeFormat.EAN_8, BarcodeFormat.UPC_A
                        }
                    }
                };

                return reader.decode(binary, hints);
            }
            catch (ReaderException)
            {
                return null;
            }
            finally
            {
                bmp24.UnlockBits(data);
            }
        }

        /// <summary>
        /// 文档扫描处理（黑白二值化）
        /// </summary>
        public Bitmap ProcessDocumentScan(Bitmap source)
        {
            if (source == null) return null;

            try
            {
                // 应用摄像头处理（校正和调节）
                var processed = _cameraManager.ProcessFrame(source, applyAdjustments: true);

                // 转换为灰度图
                var gray = Grayscale.CommonAlgorithms.BT709.Apply(processed);

                // 应用局部阈值处理
                var threshold = new BradleyLocalThresholding
                {
                    WindowSize = 41,
                    PixelBrightnessDifferenceLimit = 0.1f
                };
                threshold.ApplyInPlace(gray);

                return gray;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"文档扫描处理失败: {ex.Message}");
                return source;
            }
        }

        /// <summary>
        /// 创建缩略图
        /// </summary>
        public BitmapSource CreateThumbnail(BitmapSource source, int width, int height)
        {
            try
            {
                var scaleX = (double)width / source.PixelWidth;
                var scaleY = (double)height / source.PixelHeight;
                var scale = Math.Min(scaleX, scaleY);
                var scaledWidth = (int)(source.PixelWidth * scale);
                var scaledHeight = (int)(source.PixelHeight * scale);

                var thumbnail = new TransformedBitmap(source, new System.Windows.Media.ScaleTransform(scale, scale));
                var result = new CroppedBitmap(thumbnail,
                    new System.Windows.Int32Rect((thumbnail.PixelWidth - scaledWidth) / 2,
                                               (thumbnail.PixelHeight - scaledHeight) / 2,
                                               scaledWidth, scaledHeight));
                result.Freeze();
                return result;
            }
            catch (Exception)
            {
                return source;
            }
        }

        /// <summary>
        /// 保存图片到文件
        /// </summary>
        public void SaveBitmapSourceToFile(BitmapSource bitmap, string filePath)
        {
            if (bitmap == null || string.IsNullOrEmpty(filePath)) return;

            try
            {
                BitmapEncoder encoder = filePath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                    ? new JpegBitmapEncoder()
                    : new PngBitmapEncoder();

                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = new System.IO.FileStream(filePath, System.IO.FileMode.Create);
                encoder.Save(stream);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"保存图片失败: {ex.Message}");
                throw;
            }
        }
    }
}