using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MonkeyTV;

public enum PreferredVpnLaunchChoice { Cancel, OpenAnyway, SwitchAndOpen }

public partial class PreferredVpnLaunchWindow : Window
{
    public PreferredVpnLaunchChoice Choice { get; private set; }
    private double _ownerOpacity = 1;

    public PreferredVpnLaunchWindow(string serviceName, string preferredLocation, string currentLocation)
    {
        InitializeComponent();
        Heading.Text = $"Open {serviceName} with {preferredLocation}?";
        PreferredLocationText.Text = preferredLocation;
        CurrentLocationText.Text = currentLocation;
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        Loaded += (_, _) => AnimateIn();
        Closed += (_, _) => RestoreOwner();
    }

    private void AnimateIn()
    {
        if (Owner is not null)
        {
            _ownerOpacity = Owner.Opacity;
            Owner.Opacity = Math.Min(_ownerOpacity, 0.88);
        }
        if (ThemeService.ReduceMotion) { DialogSurface.Opacity = 1; DialogScale.ScaleX = DialogScale.ScaleY = 1; return; }
        var duration = TimeSpan.FromMilliseconds(160);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        DialogSurface.BeginAnimation(OpacityProperty, new DoubleAnimation(1, duration));
        DialogScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, duration) { EasingFunction = easing });
        DialogScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, duration) { EasingFunction = easing });
    }

    private void RestoreOwner()
    {
        if (Owner is not null) Owner.Opacity = _ownerOpacity;
    }

    private void SwitchAndOpen_Click(object sender, RoutedEventArgs e) { Choice = PreferredVpnLaunchChoice.SwitchAndOpen; DialogResult = true; Close(); }
    private void OpenAnyway_Click(object sender, RoutedEventArgs e) { Choice = PreferredVpnLaunchChoice.OpenAnyway; DialogResult = true; Close(); }
    private void Cancel_Click(object sender, RoutedEventArgs e) { Choice = PreferredVpnLaunchChoice.Cancel; DialogResult = false; Close(); }
}
