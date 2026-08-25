using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MonkeyTV;

public partial class AlphaWarningWindow : Window
{
    public bool DontShowAgain => DontShowCheck.IsChecked == true;

    public AlphaWarningWindow(string version)
    {
        InitializeComponent();
        VersionText.Text = $"Monkey TV v{version}";
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        Loaded += (_, _) => AnimateIn();
    }

    private void AnimateIn()
    {
        if (ThemeService.ReduceMotion) { DialogSurface.Opacity = 1; DialogScale.ScaleX = DialogScale.ScaleY = 1; return; }
        var duration = TimeSpan.FromMilliseconds(160);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        DialogSurface.BeginAnimation(OpacityProperty, new DoubleAnimation(1, duration));
        DialogScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, duration) { EasingFunction = easing });
        DialogScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, duration) { EasingFunction = easing });
    }

    private void Continue_Click(object sender, RoutedEventArgs e) { DialogResult = true; Close(); }
    private void Exit_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}
