using System.Windows;
using System.Windows.Input;

namespace MonkeyTV;

public partial class ConfirmWindow : Window
{
    public ConfirmWindow(string heading, string message, string confirmLabel = "Confirm")
    {
        InitializeComponent(); Heading.Text = heading; Message.Text = message; ConfirmButton.Content = confirmLabel;
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
    }
    private void Confirm_Click(object sender, RoutedEventArgs e) { DialogResult = true; Close(); }
    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}
