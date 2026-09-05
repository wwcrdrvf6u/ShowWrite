using Avalonia.Controls;
using Avalonia.Media.Imaging;
using System;
using System.IO;

namespace ShowWrite
{
    public partial class SplashWindow : Window
    {
        private const string EmbeddedBootImage = "avares://ShowWrite/boot.png";

        public SplashWindow()
        {
            InitializeComponent();
            LoadSplashImage();
        }

        /// <summary>
        /// 优先显示目录文件（m.json）中命中今天的启动图；否则显示内置启动图 boot.png。
        /// 加载失败不影响主程序启动。
        /// </summary>
        private void LoadSplashImage()
        {
            try
            {
                // 1. 目录文件命中今天的日期 → 显示指向的图片
                var manifest = BootManifest.Load();
                var imagePath = manifest?.ResolveImageForDate(DateTime.Today);
                if (imagePath != null)
                {
                    var bitmap = new Bitmap(imagePath);
                    SplashImage.Source = bitmap;
                    SplashImageBlur.Source = bitmap;
                    return;
                }

                // 2. 未命中（或无目录文件）→ 显示内置启动图
                using (var stream = Avalonia.Platform.AssetLoader.Open(new Uri(EmbeddedBootImage)))
                {
                    var bitmap = new Bitmap(stream);
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
