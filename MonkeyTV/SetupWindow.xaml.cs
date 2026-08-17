using System.Windows;

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
        AppearanceStep.Visibility = Visibility.Collapsed; FinishStep.Visibility = Visibility.Visible;
        Step2Label.Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush");
        Step3Label.Foreground = (System.Windows.Media.Brush)FindResource("TextBrush");
    }

    private void OpenMonkeyTV_Click(object sender, RoutedEventArgs e)
    {
        _settings.SetupComplete = true; _store.Save(_settings); DialogResult = true; Close();
    }
}
