using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;
using System.Runtime.InteropServices;
using System.Text;
using System.Diagnostics;
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

    public SettingsWindow(MonkeySettings settings, SettingsStore store, string? initialPage = null)
    {
        InitializeComponent(); _settings = settings; _store = store;
        CardStyleBox.SelectedIndex = (int)_settings.CardStyle; TileSizeBox.SelectedIndex = (int)_settings.TileSize;
        ThemeBox.SelectedIndex = (int)_settings.Theme; AccentBox.SelectedIndex = (int)_settings.Accent;
        MotionBox.SelectedIndex = (int)_settings.Motion;
        ShowNamesCheck.IsChecked = _settings.ShowServiceNames; ShowClockCheck.IsChecked = _settings.ShowClock;
        ServiceSortingBox.SelectedIndex = (int)_settings.ServiceSorting;
        StartupModeBox.SelectedIndex = (int)_settings.StartupMode;
        StartupFullscreenCheck.IsChecked = _settings.StartupFullscreen;
        CloseBehaviorBox.SelectedIndex = (int)_settings.CloseBehavior;
        DefaultExperienceBox.SelectedIndex = (int)_settings.DefaultCustomServiceExperience;
        ExternalLinksBox.SelectedIndex = (int)_settings.ExternalLinkBehavior;
        AboutVersionText.Text = $"Version v{App.DisplayVersion} — Build #{App.BuildNumber}";
        NordStatusText.Text = NordCredentialStore.HasCredentials ? "NordVPN service credentials are saved securely." : "No NordVPN service credentials are saved.";
        ShowCurrentLocationCheck.IsChecked = _settings.CurrentLocationEnabled;
        RefreshList(); RefreshAbout(); _appearanceReady = true;
        if (initialPage == "VPN") ShowPage(VpnPage);
    }

    private void ShowPage(FrameworkElement page)
    {
        foreach (var candidate in new FrameworkElement[] { ServicesPage, HomePage, AppearancePage, VpnPage, StoragePage, BackupPage, AboutPage }) candidate.Visibility = Visibility.Collapsed;
        page.Visibility = Visibility.Visible;
        if (ReferenceEquals(page, StoragePage)) RefreshStorage();
        if (ReferenceEquals(page, AboutPage)) RefreshAbout();
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag }) return;
        ShowPage(tag switch { "Home" => HomePage, "Appearance" => AppearancePage, "Streaming" or "VPN" => VpnPage, "Storage" => StoragePage, "About" => AboutPage, _ => ServicesPage });
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
        var editor = new ServiceEditorWindow(defaultExperience: _settings.DefaultCustomServiceExperience) { Owner = this };
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
        _settings.Motion = (MotionPreference)Math.Max(0, MotionBox.SelectedIndex);
        _settings.ShowClock = ShowClockCheck.IsChecked == true; _store.Save(_settings);
        ThemeService.Apply(_settings);
        _appearanceSaved = true;
        Close();
    }

    private void SaveHome_Click(object sender, RoutedEventArgs e)
    {
        _settings.ServiceSorting = (ServiceSorting)Math.Max(0, ServiceSortingBox.SelectedIndex);
        _settings.CardStyle = (ServiceCardStyle)Math.Max(0, CardStyleBox.SelectedIndex);
        _settings.TileSize = (TileSize)Math.Max(0, TileSizeBox.SelectedIndex);
        _settings.ShowServiceNames = ShowNamesCheck.IsChecked == true;
        _store.Save(_settings); Close();
    }

    private void SaveGeneral_Click(object sender, RoutedEventArgs e)
    {
        _settings.StartupMode = (StartupMode)Math.Max(0, StartupModeBox.SelectedIndex);
        _settings.StartupFullscreen = StartupFullscreenCheck.IsChecked == true;
        _settings.CloseBehavior = (CloseBehavior)Math.Max(0, CloseBehaviorBox.SelectedIndex);
        _store.Save(_settings);
    }

    private void SaveStreaming_Click(object sender, RoutedEventArgs e)
    {
        _settings.DefaultCustomServiceExperience = (ServiceExperience)Math.Max(0, DefaultExperienceBox.SelectedIndex);
        _settings.ExternalLinkBehavior = (ExternalLinkBehavior)Math.Max(0, ExternalLinksBox.SelectedIndex);
        ExternalNavigationPolicy.AskBeforeOpening = _settings.ExternalLinkBehavior == ExternalLinkBehavior.AskFirst;
        _store.Save(_settings);
    }

    private void ManageServices_Click(object sender, RoutedEventArgs e)
    {
        var manager = new ManageServicesWindow(_settings, _store) { Owner = this };
        if (manager.ShowDialog() == true) RefreshList();
    }

    private void AppearancePreview_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_appearanceReady) return;
        var preview = new MonkeySettings { Theme = (AppTheme)Math.Max(0, ThemeBox.SelectedIndex), Accent = (AppAccent)Math.Max(0, AccentBox.SelectedIndex) };
        ThemeService.Apply(preview);
    }

    private void SaveNordCredentials_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            NordCredentialStore.Save(NordUsernameBox.Text, NordPasswordBox.Password);
            NordPasswordBox.Clear(); NordStatusText.Text = "NordVPN service credentials saved securely for this Windows user.";
        }
        catch (Exception ex) { NordStatusText.Text = "Could not save credentials: " + ex.Message; }
    }

    private void ClearNordCredentials_Click(object sender, RoutedEventArgs e)
    {
        NordCredentialStore.Clear(); NordUsernameBox.Clear(); NordPasswordBox.Clear(); NordStatusText.Text = "NordVPN service credentials cleared.";
    }

    private async void ShowCurrentLocation_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        var enabled = ShowCurrentLocationCheck.IsChecked == true;
        if (Owner is MainWindow main) await main.SetCurrentLocationFeatureEnabledAsync(enabled);
        else { _settings.CurrentLocationEnabled = enabled; _store.Save(_settings); }
    }

    private void GetNordCredentials_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("https://support.nordvpn.com/hc/en-us/articles/19685375790865-NordVPN-Proxy-servers-explained") { UseShellExecute = true }); }
        catch { }
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

    private static void OpenFolder(string path)
    {
        try { Directory.CreateDirectory(path); Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true }); }
        catch { }
    }

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e) => OpenFolder(PortablePaths.DataRoot);
    private void OpenLogsFolder_Click(object sender, RoutedEventArgs e) => OpenFolder(PortablePaths.LogsDirectory);
    private void ClearLogs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Directory.Exists(PortablePaths.LogsDirectory))
                foreach (var file in Directory.EnumerateFiles(PortablePaths.LogsDirectory)) File.Delete(file);
            RefreshStorage(); StorageStatusText.Text = "Diagnostic logs cleared.";
        }
        catch { StorageStatusText.Text = "Some diagnostic logs could not be cleared yet."; }
    }

    private async void ClearTemporaryStorage_Click(object sender, RoutedEventArgs e)
    {
        StorageStatusText.Text = "Clearing safe temporary browser/cache data…";
        if (Owner is MainWindow main) await main.ClearTemporaryDataAsync();
        RefreshStorage(); StorageStatusText.Text = "Temporary browser/cache data cleared. Streaming logins were preserved; files in use finish clearing after close/restart.";
    }

    private async void ClearBrowserData_Click(object sender, RoutedEventArgs e)
    {
        var confirmation = new ConfirmWindow("Reset all website data?", "This will sign you out of all streaming services and remove saved website data for every service in Monkey TV.\n\nYour Monkey TV service configuration, artwork, and application settings will not be deleted.\n\nThis cannot be undone.", "Reset All Website Data", destructive: true) { Owner = this };
        if (confirmation.ShowDialog() != true) return;
        if (Owner is MainWindow main) await main.ResetAllWebsiteDataAsync();
        RefreshStorage(); StorageStatusText.Text = "All browser profiles were cleared.";
    }

    private void ResetMonkeyTv_Click(object sender, RoutedEventArgs e)
    {
        var confirmation = new ConfirmWindow("Reset Monkey TV?", "This permanently removes Monkey TV services, application settings, imported artwork, browser profiles, and streaming-service logins. It does not delete unrelated files outside Monkey TV's portable Data folder.\n\nYou will return to first-time setup. This cannot be undone.", "Reset Monkey TV", destructive: true) { Owner = this };
        if (confirmation.ShowDialog() != true) return;
        (Owner as MainWindow)?.ResetMonkeyTv();
        Close();
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "Monkey TV backup|*.mtvbackup", FileName = $"MonkeyTV-{DateTime.Now:yyyy-MM-dd}.mtvbackup", AddExtension = true };
        if (dialog.ShowDialog(this) != true) return;
        try { BackupService.Export(dialog.FileName, _settings); BackupStatus.Text = "Backup exported. Browser profiles and login data were not included."; }
        catch (Exception ex) { AppLog.Write("backup", ex.Message); BackupStatus.Text = "Could not export the backup."; }
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
        catch (Exception ex) { AppLog.Write("backup", ex.Message); BackupStatus.Text = "That backup could not be imported."; }
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

    private static void OpenProjectLink(string path)
    {
        try { Process.Start(new ProcessStartInfo("https://github.com/realmcmonkey/monkey-tv-builds" + path) { UseShellExecute = true }); }
        catch { }
    }
    private void ProjectLink_Click(object sender, RoutedEventArgs e) => OpenProjectLink("");
    private void ChangelogLink_Click(object sender, RoutedEventArgs e) => OpenProjectLink("/releases");
    private void NoticesLink_Click(object sender, RoutedEventArgs e) => OpenProjectLink("/blob/main/THIRD-PARTY-NOTICES.md");

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11 && Owner is MainWindow main) { main.ToggleApplicationFullscreen(); e.Handled = true; }
    }
}
