using System.Windows;
using System.Windows.Media;
using MIXO6.Services;

namespace MIXO6;

public partial class App : Application
{
    public App()
    {
        LocalSettings.Load();
        InitializeComponent();
        Resources["AppFontFamily"] = new FontFamily(FontResolver.ResolveInstalled(LocalSettings.Current.UiFontFamily));
    }
}
