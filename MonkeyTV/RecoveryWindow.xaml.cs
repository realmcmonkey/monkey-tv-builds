using System.Windows;
namespace MonkeyTV;
public partial class RecoveryWindow : Window
{
    public RecoveryChoice Choice { get; private set; } = RecoveryChoice.Continue;
    public RecoveryWindow(RecoveryMode mode = RecoveryMode.UncleanExit, string? detail = null)
    {
        InitializeComponent();
        if (mode == RecoveryMode.RecoveredConfiguration)
        {
            HeadingText.Text = "Monkey TV recovered your settings";
            MessageText.Text = "Your current configuration could not be read, so Monkey TV restored the most recent working backup." + (detail is null ? "" : "\n\nBackup: " + Path.GetFileName(detail));
        }
        else if (mode == RecoveryMode.ConfigurationFailure)
        {
            HeadingText.Text = "Monkey TV couldn't load its configuration";
            MessageText.Text = "Your configuration appears to be damaged and Monkey TV could not automatically restore a working backup.";
            ContinueButton.Visibility = Visibility.Collapsed; RestoreButton.Visibility = Visibility.Visible; FreshButton.Visibility = Visibility.Visible; OpenDataButton.Visibility = Visibility.Visible;
        }
        else if (mode == RecoveryMode.RepeatedStartup)
        {
            HeadingText.Text = "Monkey TV Recovery";
            MessageText.Text = "Monkey TV had trouble starting several times. You can start with the current settings or restore the last working configuration.";
            ContinueButton.Content = "Start with Current Settings"; RestoreButton.Visibility = Visibility.Visible; FreshButton.Visibility = Visibility.Visible; OpenDataButton.Visibility = Visibility.Visible;
        }
    }
    private void Continue_Click(object sender, RoutedEventArgs e) { Choice = RecoveryChoice.Continue; DialogResult = true; Close(); }
    private void Restore_Click(object sender, RoutedEventArgs e) { Choice = RecoveryChoice.Restore; DialogResult = true; Close(); }
    private void Fresh_Click(object sender, RoutedEventArgs e) { Choice = RecoveryChoice.Fresh; DialogResult = true; Close(); }
    private void OpenData_Click(object sender, RoutedEventArgs e) { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{PortablePaths.DataRoot}\"") { UseShellExecute = true }); } catch { } }
}

public enum RecoveryMode { UncleanExit, RecoveredConfiguration, ConfigurationFailure, RepeatedStartup }
public enum RecoveryChoice { Continue, Restore, Fresh }
