using System.Windows;
using System.Windows.Input;

namespace MIXO6;

public partial class FloatingWidget : Window
{
    public FloatingWidget() => InitializeComponent();
    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var area = SystemParameters.WorkArea;
        Left = Math.Max(area.Left, area.Right - Width - 18);
        Top = Math.Max(area.Top, area.Bottom - Height - 18);
    }
    public void UpdateTemps(double? cpu, double? gpu)
    {
        CpuTempText.Text = cpu.HasValue ? $"{cpu.Value:0}°C" : "--°C";
        GpuTempText.Text = gpu.HasValue ? $"{gpu.Value:0}°C" : "--°C";
        CpuTempText.Foreground = cpu >= 90 ? System.Windows.Media.Brushes.OrangeRed : cpu >= 80 ? System.Windows.Media.Brushes.Orange : (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#FFB85C")!;
        GpuTempText.Foreground = gpu >= 90 ? System.Windows.Media.Brushes.OrangeRed : (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#EE55D8")!;
    }
    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
    private void Hide_Click(object sender, RoutedEventArgs e) => Hide();
}
