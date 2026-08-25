using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MonkeyTV;

public partial class ConfirmWindow : Window
{
    public ConfirmWindow(string heading, string message, string confirmLabel = "Confirm", bool destructive = false,
        string cancelLabel = "Cancel", string? requestBadge = null, string? requestOrigin = null, string? requestedCapability = null)
    {
        InitializeComponent(); Heading.Text = heading; Message.Text = message; ConfirmButton.Content = confirmLabel; CancelButton.Content = cancelLabel;
        if (!string.IsNullOrWhiteSpace(requestBadge))
        {
            RequestBadge.Visibility = Visibility.Visible;
            RequestBadgeText.Text = requestBadge;
        }
        if (!string.IsNullOrWhiteSpace(requestOrigin) && !string.IsNullOrWhiteSpace(requestedCapability))
        {
            PermissionSummary.Visibility = Visibility.Visible;
            PermissionOrigin.Text = requestOrigin;
            PermissionCapability.Text = requestedCapability;
            DialogIcon.Text = "\uE72E";
        }
        if (destructive)
        {
            ConfirmButton.Background = new SolidColorBrush(Color.FromRgb(186, 48, 48));
            ConfirmButton.Foreground = Brushes.White;
            DialogIcon.Text = "\uE74D";
            DialogIconSurface.Background = new SolidColorBrush(Color.FromArgb(48, 186, 48, 48));
            DialogIconSurface.BorderBrush = new SolidColorBrush(Color.FromRgb(224, 94, 94));
            DialogIcon.Foreground = new SolidColorBrush(Color.FromRgb(255, 137, 137));
        }
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
    private void Confirm_Click(object sender, RoutedEventArgs e) { DialogResult = true; Close(); }
    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}
