using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;
using WinForms = System.Windows.Forms;

namespace ShowWrite
{
    public partial class PenSettingsWindow : Window
    {
        public System.Windows.Media.Color SelectedColor { get; private set; }
        public double SelectedPenWidth { get; private set; }
        public double SelectedEraserWidth { get; private set; }
        public bool UseTouchAreaForEraser { get; private set; } = false;

        public PenSettingsWindow(System.Windows.Media.Color currentColor, double currentPenWidth, double currentEraserWidth)
        {
            InitializeComponent();

            SelectedColor = currentColor;
            SelectedPenWidth = currentPenWidth;
            SelectedEraserWidth = currentEraserWidth;

            // 初始化UI
            PenColorPreview.Background = new SolidColorBrush(SelectedColor);
            PenWidthSlider.Value = SelectedPenWidth;
            PenWidthValue.Text = SelectedPenWidth.ToString();
            EraserWidthSlider.Value = SelectedEraserWidth;
            EraserWidthValue.Text = SelectedEraserWidth.ToString();

            // 绑定滑块值变化事件
            PenWidthSlider.ValueChanged += PenWidthSlider_ValueChanged;
            EraserWidthSlider.ValueChanged += EraserWidthSlider_ValueChanged;

            // 默认使用滑块模式
            UseTouchAreaForEraser = false;
        }

        private void PenWidthSlider_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
        {
            PenWidthValue.Text = ((int)e.NewValue).ToString();
        }

        private void EraserWidthSlider_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
        {
            EraserWidthValue.Text = ((int)e.NewValue).ToString();
        }

        private void ColorPreview_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is string colorHex)
            {
                SelectedColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorHex);
                PenColorPreview.Background = new SolidColorBrush(SelectedColor);
            }
        }

        private void CustomColorButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new WinForms.ColorDialog();
            dlg.Color = System.Drawing.Color.FromArgb(SelectedColor.A, SelectedColor.R, SelectedColor.G, SelectedColor.B);
            if (dlg.ShowDialog() == WinForms.DialogResult.OK)
            {
                SelectedColor = System.Windows.Media.Color.FromArgb(dlg.Color.A, dlg.Color.R, dlg.Color.G, dlg.Color.B);
                PenColorPreview.Background = new SolidColorBrush(SelectedColor);
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            SelectedPenWidth = PenWidthSlider.Value;
            SelectedEraserWidth = EraserWidthSlider.Value;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}