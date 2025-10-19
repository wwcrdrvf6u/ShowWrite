using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using AForge;
using AForge.Imaging.Filters;
using Newtonsoft.Json;
using ShowWrite.Models;
using ShowWrite.Services;
using System.Drawing;
using System.Drawing.Imaging;
using WpfPoint = System.Windows.Point; // 明确指定使用 WPF 的 Point
using WpfBrushes = System.Windows.Media.Brushes; // 明确指定使用 WPF 的 Brushes

namespace ShowWrite.Views
{
    public partial class RealTimePerspectiveCorrectionWindow : Window
    {
        private readonly VideoService _videoService;
        private readonly AppConfig _config;
        private readonly int _cameraIndex;

        private List<Ellipse> _correctionPoints;
        private List<Line> _correctionLines;
        private Ellipse _selectedPoint;
        private bool _isDragging = false;

        // 校正点位置（相对于视频区域的百分比）- 使用 WPF Point
        private WpfPoint[] _points = new WpfPoint[4]
        {
            new WpfPoint(0.1, 0.1),   // 左上
            new WpfPoint(0.9, 0.1),   // 右上
            new WpfPoint(0.9, 0.9),   // 右下
            new WpfPoint(0.1, 0.9)    // 左下
        };

        // 公共属性，供 MainWindow 访问 - 添加默认值
        public List<IntPoint> CorrectionPoints { get; private set; } = new List<IntPoint>();
        public int SourceWidth { get; private set; } = 0;
        public int SourceHeight { get; private set; } = 0;

        // 修改构造函数以匹配 MainWindow 中的调用
        public RealTimePerspectiveCorrectionWindow(VideoService videoService, AppConfig config, int cameraIndex)
        {
            _videoService = videoService;
            _config = config;
            _cameraIndex = cameraIndex;

            InitializeComponent();
            InitializeCorrectionUI();
            LoadSavedCorrection();

            // 订阅视频帧事件
            _videoService.OnNewFrameProcessed += VideoService_OnNewFrameProcessed;
        }

        private void InitializeCorrectionUI()
        {
            _correctionPoints = new List<Ellipse>();
            _correctionLines = new List<Line>();

            // 创建四个可拖动的校正点
            for (int i = 0; i < 4; i++)
            {
                var ellipse = new Ellipse
                {
                    Width = 20,
                    Height = 20,
                    Fill = WpfBrushes.Red, // 使用 WPF Brushes
                    Stroke = WpfBrushes.White, // 使用 WPF Brushes
                    StrokeThickness = 2,
                    Tag = i
                };

                // 设置拖拽事件
                ellipse.MouseLeftButtonDown += CorrectionPoint_MouseLeftButtonDown;
                ellipse.MouseMove += CorrectionPoint_MouseMove;
                ellipse.MouseLeftButtonUp += CorrectionPoint_MouseLeftButtonUp;

                _correctionPoints.Add(ellipse);
                CorrectionCanvas.Children.Add(ellipse);
            }

            // 创建连接线
            for (int i = 0; i < 4; i++)
            {
                var line = new Line
                {
                    Stroke = WpfBrushes.Yellow, // 使用 WPF Brushes
                    StrokeThickness = 2,
                    StrokeDashArray = new DoubleCollection { 4, 2 }
                };
                _correctionLines.Add(line);
                CorrectionCanvas.Children.Add(line);
            }

            UpdateCorrectionUI();
        }

        private void LoadSavedCorrection()
        {
            if (_config.CameraCorrections.TryGetValue(_cameraIndex, out var correctionConfig))
            {
                if (correctionConfig.CorrectionPoints != null && correctionConfig.CorrectionPoints.Count == 4)
                {
                    // 将保存的坐标转换为百分比坐标
                    double width = correctionConfig.SourceWidth;
                    double height = correctionConfig.SourceHeight;

                    if (width > 0 && height > 0)
                    {
                        for (int i = 0; i < 4; i++)
                        {
                            _points[i] = new WpfPoint(
                                correctionConfig.CorrectionPoints[i].X / width,
                                correctionConfig.CorrectionPoints[i].Y / height
                            );
                        }
                        UpdateCorrectionUI();
                    }
                }
            }
        }

        private void SaveCorrection()
        {
            var currentFrame = _videoService.GetFrameCopy();
            if (currentFrame == null) return;

            using (currentFrame)
            {
                var correctionConfig = new CameraCorrectionConfig
                {
                    CorrectionPoints = new List<IntPoint>(),
                    SourceWidth = currentFrame.Width,
                    SourceHeight = currentFrame.Height,
                    OriginalCameraWidth = currentFrame.Width,
                    OriginalCameraHeight = currentFrame.Height
                };

                // 将百分比坐标转换为实际坐标
                foreach (var point in _points)
                {
                    correctionConfig.CorrectionPoints.Add(new IntPoint(
                        (int)(point.X * currentFrame.Width),
                        (int)(point.Y * currentFrame.Height)
                    ));
                }

                if (_config.CameraCorrections.ContainsKey(_cameraIndex))
                {
                    _config.CameraCorrections[_cameraIndex] = correctionConfig;
                }
                else
                {
                    _config.CameraCorrections.Add(_cameraIndex, correctionConfig);
                }

                // 保存配置到文件
                SaveConfigToFile(_config);

                // 设置公共属性供 MainWindow 使用
                CorrectionPoints = correctionConfig.CorrectionPoints;
                SourceWidth = correctionConfig.SourceWidth;
                SourceHeight = correctionConfig.SourceHeight;
            }
        }

        private void SaveConfigToFile(AppConfig config)
        {
            try
            {
                string configPath = "appconfig.json";
                var json = JsonConvert.SerializeObject(config, Formatting.Indented);
                File.WriteAllText(configPath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"保存配置失败: {ex.Message}");
                MessageBox.Show($"保存配置失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateCorrectionUI()
        {
            if (CorrectionCanvas.ActualWidth == 0 || CorrectionCanvas.ActualHeight == 0)
                return;

            for (int i = 0; i < 4; i++)
            {
                // 更新点位置
                var ellipse = _correctionPoints[i];
                Canvas.SetLeft(ellipse, _points[i].X * CorrectionCanvas.ActualWidth - ellipse.Width / 2);
                Canvas.SetTop(ellipse, _points[i].Y * CorrectionCanvas.ActualHeight - ellipse.Height / 2);

                // 更新连线
                var line = _correctionLines[i];
                var nextPoint = _points[(i + 1) % 4];

                line.X1 = _points[i].X * CorrectionCanvas.ActualWidth;
                line.Y1 = _points[i].Y * CorrectionCanvas.ActualHeight;
                line.X2 = nextPoint.X * CorrectionCanvas.ActualWidth;
                line.Y2 = nextPoint.Y * CorrectionCanvas.ActualHeight;
            }
        }

        private void CorrectionPoint_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _selectedPoint = sender as Ellipse;
            _isDragging = true;
            _selectedPoint.CaptureMouse();
            e.Handled = true;
        }

        private void CorrectionPoint_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging || _selectedPoint == null) return;

            var position = e.GetPosition(CorrectionCanvas);
            int pointIndex = (int)_selectedPoint.Tag;

            // 限制点在画布范围内
            position.X = Math.Max(0, Math.Min(position.X, CorrectionCanvas.ActualWidth));
            position.Y = Math.Max(0, Math.Min(position.Y, CorrectionCanvas.ActualHeight));

            // 更新点位置（存储为百分比）
            _points[pointIndex] = new WpfPoint(
                position.X / CorrectionCanvas.ActualWidth,
                position.Y / CorrectionCanvas.ActualHeight
            );

            UpdateCorrectionUI();
        }

        private void CorrectionPoint_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_selectedPoint != null)
            {
                _selectedPoint.ReleaseMouseCapture();
                _selectedPoint = null;
            }
            _isDragging = false;
        }

        private void VideoService_OnNewFrameProcessed(System.Drawing.Bitmap frame)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    // 应用透视校正
                    var correctedFrame = ApplyPerspectiveCorrection(frame);
                    CorrectedImage.Source = BitmapToBitmapImage(correctedFrame);

                    // 清理资源
                    correctedFrame?.Dispose();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"透视校正处理失败: {ex.Message}");
                }
            }));
        }

        private System.Drawing.Bitmap ApplyPerspectiveCorrection(System.Drawing.Bitmap source)
        {
            // 使用当前点进行校正
            var points = _points;

            // 转换为实际坐标
            var sourcePoints = new List<IntPoint>();
            foreach (var point in points)
            {
                sourcePoints.Add(new IntPoint(
                    (int)(point.X * source.Width),
                    (int)(point.Y * source.Height)
                ));
            }

            // 目标矩形（校正后的图像）
            var destPoints = new List<IntPoint>
            {
                new IntPoint(0, 0),
                new IntPoint(source.Width, 0),
                new IntPoint(source.Width, source.Height),
                new IntPoint(0, source.Height)
            };

            try
            {
                // 使用AForge.NET进行透视变换
                var filter = new SimpleQuadrilateralTransformation(sourcePoints, source.Width, source.Height);
                return filter.Apply(source);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"透视变换失败: {ex.Message}");
                return new System.Drawing.Bitmap(source); // 返回原图
            }
        }

        // 集成Bitmap转换功能
        private BitmapImage BitmapToBitmapImage(System.Drawing.Bitmap bitmap)
        {
            if (bitmap == null) return null;

            try
            {
                using var memory = new MemoryStream();
                bitmap.Save(memory, ImageFormat.Bmp);
                memory.Position = 0;

                var bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.StreamSource = memory;
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.EndInit();
                bitmapImage.Freeze();

                return bitmapImage;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Bitmap转换失败: {ex.Message}");
                return null;
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            SaveCorrection();
            MessageBox.Show("梯形校正已保存", "提示", MessageBoxButton.OK, MessageBoxImage.Information);

            // 设置对话框结果为 true，表示成功保存
            this.DialogResult = true;
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            // 重置为默认矩形
            _points = new WpfPoint[4]
            {
                new WpfPoint(0.1, 0.1),
                new WpfPoint(0.9, 0.1),
                new WpfPoint(0.9, 0.9),
                new WpfPoint(0.1, 0.9)
            };

            UpdateCorrectionUI();
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            // 清除保存的校正
            if (_config.CameraCorrections.ContainsKey(_cameraIndex))
            {
                _config.CameraCorrections.Remove(_cameraIndex);
                SaveConfigToFile(_config);
            }

            ResetButton_Click(sender, e);
            MessageBox.Show("校正设置已清除", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // 设置对话框结果为 false，表示取消
            this.DialogResult = false;
            Close();
        }

        private void CorrectionCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateCorrectionUI();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _videoService.OnNewFrameProcessed -= VideoService_OnNewFrameProcessed;
        }
    }
}