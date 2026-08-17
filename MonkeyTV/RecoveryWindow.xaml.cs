using System.Windows;
namespace MonkeyTV;
public partial class RecoveryWindow : Window
{
    public RecoveryWindow() => InitializeComponent();
    private void Continue_Click(object sender, RoutedEventArgs e) { DialogResult = true; Close(); }
}
