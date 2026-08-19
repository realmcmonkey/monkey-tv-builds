using System.Windows;
using System.Diagnostics;

namespace MonkeyTV;

public partial class SetupWindow : Window
{
    private readonly MonkeySettings _settings;
    private readonly SettingsStore _store;

    public SetupWindow(MonkeySettings settings, SettingsStore store)
    {
        InitializeComponent(); _settings = settings; _store = store; RefreshServices();
    }

    private void RefreshServices()
    {
        SetupServicesList.ItemsSource = null;
        SetupServicesList.ItemsSource = _settings.Services.Select(s => $"{s.Name}    {new Uri(s.Url).Host}").ToList();
    }

    private void AddService_Click(object sender, RoutedEventArgs e)
    {
        var editor = new ServiceEditorWindow { Owner = this };
        if (editor.ShowDialog() == true) { _settings.Services.Add(editor.Result); _store.Save(_settings); RefreshServices(); }
    }

    private void ServicesDone_Click(object sender, RoutedEventArgs e)
    {
        ServicesStep.Visibility = Visibility.Collapsed; AppearanceStep.Visibility = Visibility.Visible;
        Step1Label.Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush");
        Step2Label.Foreground = (System.Windows.Media.Brush)FindResource("TextBrush");
    }

    private void AppearanceBack_Click(object sender, RoutedEventArgs e)
    {
        AppearanceStep.Visibility = Visibility.Collapsed; ServicesStep.Visibility = Visibility.Visible;
        Step1Label.Foreground = (System.Windows.Media.Brush)FindResource("TextBrush");
        Step2Label.Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush");
    }

    private void AppearanceDone_Click(object sender, RoutedEventArgs e)
    {
        _settings.CardStyle = (ServiceCardStyle)Math.Max(0, CardStyleBox.SelectedIndex);
        _settings.TileSize = (TileSize)Math.Max(0, TileSizeBox.SelectedIndex);
        _settings.ShowServiceNames = ShowNamesCheck.IsChecked == true;
        _settings.ShowClock = ShowClockCheck.IsChecked == true;
        _store.Save(_settings);
        AppearanceStep.Visibility = Visibility.Collapsed; LocationStep.Visibility = Visibility.Visible;
        Step2Label.Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush");
        Step3Label.Foreground = (System.Windows.Media.Brush)FindResource("TextBrush");
    }

    private void SetUpLocation_Click(object sender, RoutedEventArgs e)
    {
        LocationStep.Visibility = Visibility.Collapsed; LocationCredentialsStep.Visibility = Visibility.Visible;
    }

    private void SkipLocation_Click(object sender, RoutedEventArgs e)
    {
        _settings.CurrentLocationEnabled = false; _store.Save(_settings); ShowFinish();
    }

    private void SaveLocationCredentials_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            NordCredentialStore.Save(NordUsernameBox.Text, NordPasswordBox.Password);
            _settings.CurrentLocationEnabled = true; _store.Save(_settings); ShowFinish();
        }
        catch (Exception ex) { NordSetupStatus.Text = "Could not save service credentials: " + ex.Message; }
    }

    private void GetNordCredentials_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("https://support.nordvpn.com/hc/en-us/articles/19685375790865-NordVPN-Proxy-servers-explained") { UseShellExecute = true }); }
        catch { }
    }

    private void ShowFinish()
    {
        LocationStep.Visibility = Visibility.Collapsed; LocationCredentialsStep.Visibility = Visibility.Collapsed; FinishStep.Visibility = Visibility.Visible;
        Step3Label.Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush");
        Step4Label.Foreground = (System.Windows.Media.Brush)FindResource("TextBrush");
    }

    private void OpenMonkeyTV_Click(object sender, RoutedEventArgs e)
    {
        _settings.SetupComplete = true; _store.Save(_settings); DialogResult = true; Close();
    }
}
