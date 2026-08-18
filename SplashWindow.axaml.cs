using Avalonia.Controls;
using Avalonia.Media.Imaging;
using System.IO;
using System.Linq;

namespace ShowWrite
{
    public partial class SplashWindow : Window
    {
        private static readonly string[] SupportedExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };

        public SplashWindow()
        {
            InitializeComponent();
            LoadSplashImage();
        }

        /// <summary>
        /// 默认从配置目录的 bootP 文件夹加载第一张图片；找不到时不报错，仅不显示图片。
        /// </summary>
        private void LoadSplashImage()
        {
            try
            {
                var bootPath = Config.GetBootPath();
                if (!Directory.Exists(bootPath))
                    return;

                var imageFile = Directory.EnumerateFiles(bootPath)
                    .FirstOrDefault(f => SupportedExtensions.Contains(
                        Path.GetExtension(f).ToLowerInvariant()));

                if (!string.IsNullOrEmpty(imageFile) && File.Exists(imageFile))
                {
                    var bitmap = new Bitmap(imageFile);
                    SplashImage.Source = bitmap;
                    SplashImageBlur.Source = bitmap;
                }
            }
            catch
            {
                // 启动图加载失败不影响主程序启动
            }
        }
    }
}
