using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace MonkeyTV;

public partial class ConfirmWindow : Window
{
    public ConfirmWindow(string heading, string message, string confirmLabel = "Confirm", bool destructive = false)
    {
        InitializeComponent(); Heading.Text = heading; Message.Text = message; ConfirmButton.Content = confirmLabel;
        if (destructive)
        {
            ConfirmButton.Background = new SolidColorBrush(Color.FromRgb(186, 48, 48));
            ConfirmButton.Foreground = Brushes.White;
        }
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
    }
    private void Confirm_Click(object sender, RoutedEventArgs e) { DialogResult = true; Close(); }
    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}
