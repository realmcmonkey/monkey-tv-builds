using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MonkeyTV;

public partial class ServiceControlsWindow : Window
{
    public event EventHandler? BackRequested;
    public event EventHandler? ReloadRequested;
    public event EventHandler? HomeRequested;
    public event EventHandler? FullscreenRequested;
    public event EventHandler? UserActivity;

    public ServiceControlsWindow()
    {
        InitializeComponent();
    }

    public void ShowOverlay()
    {
        if (!IsVisible) Show();
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(145)));
        ((TranslateTransform)Surface.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(0, TimeSpan.FromMilliseconds(145)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
    }

    public void HideOverlay(bool immediate = false)
    {
        if (!IsVisible) return;
        if (immediate)
        {
            BeginAnimation(OpacityProperty, null); Opacity = 0; Hide(); return;
        }
        var animation = new DoubleAnimation(0, TimeSpan.FromMilliseconds(130));
        animation.Completed += (_, _) => { if (Opacity <= .01) Hide(); };
        BeginAnimation(OpacityProperty, animation);
        ((TranslateTransform)Surface.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(-8, TimeSpan.FromMilliseconds(130)));
    }

    public void SetFullscreenState(bool fullscreen)
    {
        FullscreenButton.Content = fullscreen ? "\uE73F" : "\uE740";
        FullscreenButton.ToolTip = fullscreen ? "Exit application fullscreen (F11)" : "Enter application fullscreen (F11)";
    }

    private void Surface_MouseEnter(object sender, MouseEventArgs e) => UserActivity?.Invoke(this, EventArgs.Empty);
    private void Surface_MouseMove(object sender, MouseEventArgs e) => UserActivity?.Invoke(this, EventArgs.Empty);
    private void Back_Click(object sender, RoutedEventArgs e) { UserActivity?.Invoke(this, EventArgs.Empty); BackRequested?.Invoke(this, EventArgs.Empty); }
    private void Reload_Click(object sender, RoutedEventArgs e) { UserActivity?.Invoke(this, EventArgs.Empty); ReloadRequested?.Invoke(this, EventArgs.Empty); }
    private void Home_Click(object sender, RoutedEventArgs e) { UserActivity?.Invoke(this, EventArgs.Empty); HomeRequested?.Invoke(this, EventArgs.Empty); }
    private void Fullscreen_Click(object sender, RoutedEventArgs e) { UserActivity?.Invoke(this, EventArgs.Empty); FullscreenRequested?.Invoke(this, EventArgs.Empty); }
}
