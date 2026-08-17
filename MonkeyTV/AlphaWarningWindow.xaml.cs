using System.Windows;
using System.Windows.Input;

namespace MonkeyTV;

public partial class AlphaWarningWindow : Window
{
    public bool DontShowAgain => DontShowCheck.IsChecked == true;

    public AlphaWarningWindow(string version)
    {
        InitializeComponent();
        VersionText.Text = $"Monkey TV v{version}";
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
    }

    private void Continue_Click(object sender, RoutedEventArgs e) { DialogResult = true; Close(); }
    private void Exit_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}
