using System;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ShowWrite.Models; // 添加这个 using

namespace ShowWrite
{
    /// <summary>
    /// 支持笔迹的照片包装类
    /// </summary>
    public class PhotoWithStrokes
    {
        public ShowWrite.Models.CapturedImage CapturedImage { get; set; } // 使用完全限定名
        public StrokeCollection Strokes { get; set; }
        public BitmapSource Image => CapturedImage.Image;
        public BitmapSource Thumbnail { get; set; }
        public string Timestamp { get; set; }

        public PhotoWithStrokes(ShowWrite.Models.CapturedImage capturedImage) // 使用完全限定名
        {
            CapturedImage = capturedImage;
            Strokes = new StrokeCollection();
            Timestamp = DateTime.Now.ToString("MM-dd HH:mm:ss");
            // 创建缩略图
            Thumbnail = CreateThumbnail(capturedImage.Image, 120, 90);
        }

        /// <summary>
        /// 创建缩略图
        /// </summary>
        private BitmapSource CreateThumbnail(BitmapSource source, int width, int height)
        {
            try
            {
                var scaleX = (double)width / source.PixelWidth;
                var scaleY = (double)height / source.PixelHeight;
                var scale = Math.Min(scaleX, scaleY);
                var scaledWidth = (int)(source.PixelWidth * scale);
                var scaledHeight = (int)(source.PixelHeight * scale);
                var thumbnail = new TransformedBitmap(source,
                    new ScaleTransform(scale, scale));
                var result = new CroppedBitmap(thumbnail,
                    new Int32Rect((thumbnail.PixelWidth - scaledWidth) / 2,
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
    }
}