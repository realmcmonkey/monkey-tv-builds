using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MonkeyTV;

public enum UnsavedChangesChoice { KeepEditing, DiscardChanges, SaveChanges }

public partial class UnsavedChangesWindow : Window
{
    private double _ownerOpacity = 1;
    public UnsavedChangesChoice Choice { get; private set; } = UnsavedChangesChoice.KeepEditing;

    public UnsavedChangesWindow(string serviceName)
    {
        InitializeComponent();
        Message.Text = $"You have unsaved changes to {serviceName}.";
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

    private void KeepEditing_Click(object sender, RoutedEventArgs e) { Choice = UnsavedChangesChoice.KeepEditing; DialogResult = false; Close(); }
    private void Discard_Click(object sender, RoutedEventArgs e) { Choice = UnsavedChangesChoice.DiscardChanges; DialogResult = true; Close(); }
    private void Save_Click(object sender, RoutedEventArgs e) { Choice = UnsavedChangesChoice.SaveChanges; DialogResult = true; Close(); }
}
