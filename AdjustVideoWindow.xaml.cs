using System.Windows;
using System.Windows.Controls;

namespace ShowWrite
{
    public partial class AdjustVideoWindow : Window
    {
        public double Brightness { get; private set; }
        public double Contrast { get; private set; }
        public int Rotation { get; private set; }
        public bool MirrorH { get; private set; }
        public bool MirrorV { get; private set; }

        public AdjustVideoWindow(double brightness, double contrast, int rotation, bool mirrorH, bool mirrorV)
        {
            InitializeComponent();

            // 初始化滑块值
            BrightnessSlider.Value = brightness;
            ContrastSlider.Value = contrast;
            Brightness = brightness;
            Contrast = contrast;

            RotationBox.SelectedIndex = rotation / 90;
            MirrorHCheck.IsChecked = mirrorH;
            MirrorVCheck.IsChecked = mirrorV;
        }

        // 添加缺失的事件处理方法 - 这是关键修复
        private void BrightnessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            Brightness = e.NewValue;
        }

        private void ContrastSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            Contrast = e.NewValue;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (RotationBox.SelectedItem is ComboBoxItem item && item.Tag != null)
            {
#pragma warning disable CS8604 // 引用类型参数可能为 null。
                Rotation = int.Parse(item.Tag.ToString());
#pragma warning restore CS8604 // 引用类型参数可能为 null。
            }
            MirrorH = MirrorHCheck.IsChecked == true;
            MirrorV = MirrorVCheck.IsChecked == true;

            DialogResult = true;
            Close();
        }
    }
}