using System.Windows;

namespace MonkeyTV;

public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
        VersionText.Text = $"v{App.DisplayVersion}  ·  Build #{App.BuildNumber}";
    }
}
