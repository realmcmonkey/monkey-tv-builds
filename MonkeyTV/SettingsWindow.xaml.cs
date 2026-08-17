using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MonkeyTV;

public partial class SettingsWindow : Window
{
    private MonkeySettings _settings;
    private readonly SettingsStore _store;
    private bool _appearanceReady;
    private bool _appearanceSaved;

    public SettingsWindow(MonkeySettings settings, SettingsStore store)
    {
        InitializeComponent(); _settings = settings; _store = store;
        CardStyleBox.SelectedIndex = (int)_settings.CardStyle; TileSizeBox.SelectedIndex = (int)_settings.TileSize;
        ThemeBox.SelectedIndex = (int)_settings.Theme; AccentBox.SelectedIndex = (int)_settings.Accent;
        ShowNamesCheck.IsChecked = _settings.ShowServiceNames; ShowClockCheck.IsChecked = _settings.ShowClock;
        AboutVersionText.Text = $"Version v{App.DisplayVersion} — Build #{App.BuildNumber}";
        RefreshList(); RefreshAbout(); _appearanceReady = true;
    }

    private void ShowPage(FrameworkElement page)
    {
        foreach (var candidate in new FrameworkElement[] { ServicesPage, AppearancePage, StoragePage, BackupPage, AboutPage }) candidate.Visibility = Visibility.Collapsed;
        page.Visibility = Visibility.Visible;
        if (ReferenceEquals(page, StoragePage)) RefreshStorage();
        if (ReferenceEquals(page, AboutPage)) RefreshAbout();
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag }) return;
        ShowPage(tag switch { "Appearance" => AppearancePage, "Storage" => StoragePage, "Backup" => BackupPage, "About" => AboutPage, _ => ServicesPage });
    }

    private void RefreshList(int selected = -1)
    {
        ServicesList.ItemsSource = null; ServicesList.ItemsSource = _settings.Services;
        if (selected >= 0 && selected < _settings.Services.Count) ServicesList.SelectedIndex = selected;
    }

    private ServiceDefinition? Selected => ServicesList.SelectedItem as ServiceDefinition;
    private void ServicesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var has = Selected is not null; EditButton.IsEnabled = RemoveButton.IsEnabled = has;
        UpButton.IsEnabled = has && ServicesList.SelectedIndex > 0; DownButton.IsEnabled = has && ServicesList.SelectedIndex < _settings.Services.Count - 1;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var editor = new ServiceEditorWindow { Owner = this };
        if (editor.ShowDialog() == true) { _settings.Services.Add(editor.Result); SaveAndRefresh(_settings.Services.Count - 1); }
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } selected) return;
        var index = ServicesList.SelectedIndex; var editor = new ServiceEditorWindow(selected) { Owner = this };
        if (editor.ShowDialog() != true) return;
        if (editor.ClearDataRequested) (Owner as MainWindow)?.ClearServiceData(selected.Id);
        _settings.Services[index] = editor.Result; SaveAndRefresh(index);
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } selected) return;
        var confirmation = new ConfirmWindow("Remove service?", $"Remove {selected.Name}? Retained Build #1 profile data belonging only to this service will also be removed. The current shared browser profile is kept.", "Remove") { Owner = this };
        if (confirmation.ShowDialog() != true) return;
        StorageService.RemoveRetainedServiceProfileData(selected);
        var index = ServicesList.SelectedIndex; _settings.Services.RemoveAt(index); SaveAndRefresh(Math.Min(index, _settings.Services.Count - 1));
    }

    private void Up_Click(object sender, RoutedEventArgs e) => Move(-1);
    private void Down_Click(object sender, RoutedEventArgs e) => Move(1);
    private void Move(int delta)
    {
        var from = ServicesList.SelectedIndex; var to = from + delta;
        if (from < 0 || to < 0 || to >= _settings.Services.Count) return;
        var item = _settings.Services[from]; _settings.Services.RemoveAt(from); _settings.Services.Insert(to, item); SaveAndRefresh(to);
    }

    private void SaveAndRefresh(int selected)
    {
        _store.Save(_settings); StorageService.CleanupOrphanAssets(_settings); RefreshList(selected);
    }

    private void SaveAppearance_Click(object sender, RoutedEventArgs e)
    {
        _settings.CardStyle = (ServiceCardStyle)Math.Max(0, CardStyleBox.SelectedIndex); _settings.TileSize = (TileSize)Math.Max(0, TileSizeBox.SelectedIndex);
        _settings.Theme = (AppTheme)Math.Max(0, ThemeBox.SelectedIndex); _settings.Accent = (AppAccent)Math.Max(0, AccentBox.SelectedIndex);
        _settings.ShowServiceNames = ShowNamesCheck.IsChecked == true; _settings.ShowClock = ShowClockCheck.IsChecked == true; _store.Save(_settings);
        ThemeService.Apply(_settings);
        _appearanceSaved = true;
        Close();
    }

    private void AppearancePreview_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_appearanceReady) return;
        var preview = new MonkeySettings { Theme = (AppTheme)Math.Max(0, ThemeBox.SelectedIndex), Accent = (AppAccent)Math.Max(0, AccentBox.SelectedIndex) };
        ThemeService.Apply(preview);
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_appearanceSaved) ThemeService.Apply(_settings);
    }

    private void RefreshStorage_Click(object sender, RoutedEventArgs e) => RefreshStorage();
    private void RefreshStorage()
    {
        var snapshot = StorageService.GetSnapshot();
        TotalStorageText.Text = "Total portable Data: " + StorageService.FormatBytes(snapshot.TotalBytes);
        SettingsStorageText.Text = StorageService.FormatBytes(snapshot.SettingsBytes);
        ArtworkStorageText.Text = StorageService.FormatBytes(snapshot.ArtworkBytes);
        BrowserPersistentStorageText.Text = StorageService.FormatBytes(snapshot.BrowserPersistentBytes);
        BrowserTemporaryStorageText.Text = StorageService.FormatBytes(snapshot.BrowserTemporaryBytes);
        LogsStorageText.Text = StorageService.FormatBytes(snapshot.LogBytes);
    }

    private async void ClearTemporaryStorage_Click(object sender, RoutedEventArgs e)
    {
        StorageStatusText.Text = "Clearing safe temporary browser/cache data…";
        if (Owner is MainWindow main) await main.ClearTemporaryDataAsync();
        RefreshStorage(); StorageStatusText.Text = "Temporary browser/cache data cleared. Streaming logins were preserved; files in use finish clearing after close/restart.";
    }

    private void ClearBrowserData_Click(object sender, RoutedEventArgs e)
    {
        var confirmation = new ConfirmWindow("Clear all browser data?", "This removes all WebView2 profiles, streaming-service logins, cookies, and site preferences. Your Monkey TV services and artwork remain.", "Clear Browser Data") { Owner = this };
        if (confirmation.ShowDialog() != true) return;
        (Owner as MainWindow)?.ClearAllBrowserData();
        RefreshStorage(); StorageStatusText.Text = "All browser profiles were cleared.";
    }

    private void ResetMonkeyTv_Click(object sender, RoutedEventArgs e)
    {
        var confirmation = new ConfirmWindow("Reset Monkey TV?", "This permanently removes your services, appearance settings, artwork, browser profiles, and streaming-service logins. You will return to first-time setup.", "Reset Monkey TV") { Owner = this };
        if (confirmation.ShowDialog() != true) return;
        (Owner as MainWindow)?.ResetMonkeyTv();
        Close();
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "Monkey TV backup|*.mtvbackup", FileName = $"MonkeyTV-{DateTime.Now:yyyy-MM-dd}.mtvbackup", AddExtension = true };
        if (dialog.ShowDialog(this) != true) return;
        try { BackupService.Export(dialog.FileName, _settings); BackupStatus.Text = "Backup exported. Browser profiles and login data were not included."; }
        catch (Exception ex) { AppLog.Write("backup", ex.Message); BackupStatus.Text = "Could not export the backup: " + ex.Message; }
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Monkey TV backup|*.mtvbackup|All files|*.*" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var preview = BackupService.Validate(dialog.FileName);
            var confirmation = new ConfirmWindow("Import configuration?", $"Replace the current configuration with {preview.Settings.Services.Count} service(s) and its appearance settings? Streaming logins are not included.", "Import") { Owner = this };
            if (confirmation.ShowDialog() != true) return;
            (Owner as MainWindow)?.PrepareForConfigurationImport();
            _settings = BackupService.Import(preview, _store); RefreshList();
            CardStyleBox.SelectedIndex = (int)_settings.CardStyle; TileSizeBox.SelectedIndex = (int)_settings.TileSize;
            ThemeBox.SelectedIndex = (int)_settings.Theme; AccentBox.SelectedIndex = (int)_settings.Accent; ThemeService.Apply(_settings);
            ShowNamesCheck.IsChecked = _settings.ShowServiceNames; ShowClockCheck.IsChecked = _settings.ShowClock;
            BackupStatus.Text = "Configuration imported. Streaming-service logins were not changed.";
        }
        catch (Exception ex) { AppLog.Write("backup", ex.Message); BackupStatus.Text = "That backup could not be imported: " + ex.Message; }
    }

    private void RefreshAbout()
    {
        string webView; try { webView = CoreWebView2Environment.GetAvailableBrowserVersionString(); } catch { webView = "Not detected"; }
        RuntimeText.Text = $"WebView2 Runtime: {webView}\n.NET: {RuntimeInformation.FrameworkDescription}\nWindows: {Environment.OSVersion.VersionString}\nArchitecture: {RuntimeInformation.ProcessArchitecture}\nHome ready after: {App.LastStartupMilliseconds} ms";
        AboutStorageText.Text = $"Portable Data: {PortablePaths.DataRoot}\nCurrent size: {StorageService.FormatBytes(StorageService.DirectorySize(PortablePaths.DataRoot))}";
    }

    private void CopyDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        var text = new StringBuilder().AppendLine($"Monkey TV v{App.DisplayVersion} — Build #{App.BuildNumber}").AppendLine(RuntimeText.Text)
            .AppendLine($"Data size: {StorageService.FormatBytes(StorageService.DirectorySize(PortablePaths.DataRoot))}")
            .AppendLine($"Configured services: {_settings.Services.Count}").AppendLine("Release channel: Alpha").ToString();
        Clipboard.SetText(text); CopyStatus.Text = "Diagnostic summary copied. It contains no URLs, cookies, or browser-profile data.";
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11 && Owner is MainWindow main) { main.ToggleApplicationFullscreen(); e.Handled = true; }
    }
}
