using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MonkeyTV;

public partial class SetupWindow : Window
{
    private readonly MonkeySettings _settings;
    private readonly SettingsStore _store;
    private readonly bool _deferCommit;
    private NordCredentials? _pendingNordCredentials;
    private readonly List<ServiceLibrarySelectionItem> _serviceSelections;
    private readonly List<ServiceDefinition> _pendingCustomServices = new();
    private bool _serviceFiltersReady;
    private bool _serviceSelectionCommitted;

    internal MonkeySettings ResultSettings => _settings;
    internal NordCredentials? PendingNordCredentials => _pendingNordCredentials;

    public SetupWindow(MonkeySettings settings, SettingsStore store, bool deferCommit = false)
    {
        _settings = settings; _store = store; _deferCommit = deferCommit;
        _serviceSelections = ServiceLibrarySelection.CreateItems(_settings).ToList();
        InitializeComponent();
        SetupServiceRegionBox.SelectedIndex = 0;
        _serviceFiltersReady = true;
        RefreshServices();
        Loaded += (_, _) => AnimateIn();
    }

    private void RefreshServices()
    {
        if (!_serviceFiltersReady || SetupServiceSearchBox is null || SetupServiceRegionBox is null || SetupServicesList is null) return;
        var query = SetupServiceSearchBox.Text;
        var region = (SetupServiceRegionBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All";
        var allowed = ServiceLibrarySelection.Filter(query, region).Select(template => template.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        SetupServicesList.ItemsSource = null;
        SetupServicesList.ItemsSource = _serviceSelections.Where(item => allowed.Contains(item.Template.Id)).ToList();
    }

    private void SetupServiceFilter_Changed(object sender, RoutedEventArgs e) => RefreshServices();

    private void SetupServiceSelection_Changed(object sender, RoutedEventArgs e)
    {
        RefreshServices();
    }

    private void AddCustomService_Click(object sender, RoutedEventArgs e)
    {
        var editor = new ServiceEditorWindow(defaultExperience: _settings.DefaultCustomServiceExperience) { Owner = this };
        if (editor.ShowDialog() == true)
            _pendingCustomServices.Add(editor.Result);
    }

    private bool CommitServiceSelection()
    {
        if (_serviceSelectionCommitted) return true;
        var removals = _serviceSelections.Where(item => item.Existing is not null && !item.Enabled)
            .Select(item => item.Existing!).ToList();
        if (removals.Count > 0)
        {
            var confirmation = new ConfirmWindow("Remove selected services?",
                $"Remove {removals.Count} service(s) from Monkey TV? Provider cookies, logins, and site data will not be cleared.", "Remove") { Owner = this };
            if (confirmation.ShowDialog() != true) return false;
        }

        foreach (var service in removals) _settings.Services.Remove(service);
        foreach (var item in _serviceSelections.Where(item => item.Enabled && item.Existing is null))
        {
            var match = BuiltInServiceCatalog.FindProbableCustomMatch(_settings.Services.Concat(_pendingCustomServices), item.Template);
            if (match is not null)
            {
                var choice = new ConfirmWindow("Possible existing service",
                    $"{match.Name} may already be your {item.Template.Name} service. Add the built-in service anyway?", "Add built-in service") { Owner = this };
                if (choice.ShowDialog() != true) continue;
            }
            _settings.Services.Add(item.Template.Create());
        }
        foreach (var item in _serviceSelections.Where(item => item.Existing is not null && !item.Customized && item.Existing.BuiltInDefinitionRevision < item.Template.Revision))
            BuiltInServiceCatalog.RestoreDefaults(item.Existing!, item.Template);

        _settings.Services.AddRange(_pendingCustomServices);
        _pendingCustomServices.Clear();
        _serviceSelectionCommitted = true;
        return true;
    }

    private void ServicesDone_Click(object sender, RoutedEventArgs e)
    {
        ServicesStep.Visibility = Visibility.Collapsed; AppearanceStep.Visibility = Visibility.Visible;
        Step1Label.Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush");
        Step2Label.Foreground = (System.Windows.Media.Brush)FindResource("TextBrush");
        SetActiveStep(Step2Surface);
    }

    private void AppearanceBack_Click(object sender, RoutedEventArgs e)
    {
        AppearanceStep.Visibility = Visibility.Collapsed; ServicesStep.Visibility = Visibility.Visible;
        Step1Label.Foreground = (System.Windows.Media.Brush)FindResource("TextBrush");
        Step2Label.Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush");
        SetActiveStep(Step1Surface);
    }

    private void AppearanceDone_Click(object sender, RoutedEventArgs e)
    {
        _settings.CardStyle = (ServiceCardStyle)Math.Max(0, CardStyleBox.SelectedIndex);
        _settings.TileSize = (TileSize)Math.Max(0, TileSizeBox.SelectedIndex);
        _settings.ShowServiceNames = ShowNamesCheck.IsChecked == true;
        _settings.ShowClock = ShowClockCheck.IsChecked == true;
        if (!_deferCommit) _store.Save(_settings);
        AppearanceStep.Visibility = Visibility.Collapsed; LocationStep.Visibility = Visibility.Visible;
        Step2Label.Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush");
        Step3Label.Foreground = (System.Windows.Media.Brush)FindResource("TextBrush");
        SetActiveStep(Step3Surface);
    }

    private void SetUpLocation_Click(object sender, RoutedEventArgs e)
    {
        LocationStep.Visibility = Visibility.Collapsed; LocationCredentialsStep.Visibility = Visibility.Visible;
    }

    private void SkipLocation_Click(object sender, RoutedEventArgs e)
    {
        _settings.CurrentLocationEnabled = false;
        if (!_deferCommit) _store.Save(_settings);
        ShowFinish();
    }

    private void SaveLocationCredentials_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_deferCommit)
            {
                if (string.IsNullOrWhiteSpace(NordUsernameBox.Text) || string.IsNullOrWhiteSpace(NordPasswordBox.Password))
                    throw new ArgumentException("Both NordVPN service credentials are required.");
                _pendingNordCredentials = new NordCredentials(NordUsernameBox.Text.Trim(), NordPasswordBox.Password);
            }
            else
                NordCredentialStore.Save(NordUsernameBox.Text, NordPasswordBox.Password);
            _settings.CurrentLocationEnabled = true;
            if (!_deferCommit) _store.Save(_settings);
            NordPasswordBox.Clear();
            ShowFinish();
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
        SetActiveStep(Step4Surface);
    }

    private void OpenMonkeyTV_Click(object sender, RoutedEventArgs e)
    {
        if (!CommitServiceSelection()) return;
        _settings.SetupComplete = true;
        if (!_deferCommit) _store.Save(_settings);
        DialogResult = true; Close();
    }

    // Closing setup remains cancellation: the normal first-run path leaves setup incomplete,
    // while the rerun path is transactional and does not reset existing data until Finish.
    private void CloseSetup_Click(object sender, RoutedEventArgs e) => Close();

    private void SetActiveStep(FrameworkElement active)
    {
        foreach (var step in new[] { Step1Surface, Step2Surface, Step3Surface, Step4Surface })
        {
            var isActive = ReferenceEquals(step, active);
            step.Background = isActive ? (Brush)FindResource("AccentSubtleBrush") : Brushes.Transparent;
            step.BorderBrush = isActive ? (Brush)FindResource("AccentBrush") : Brushes.Transparent;
        }
    }

    private void AnimateIn()
    {
        if (ThemeService.ReduceMotion) { SetupRoot.Opacity = 1; SetupScale.ScaleX = SetupScale.ScaleY = 1; return; }
        var duration = TimeSpan.FromMilliseconds(170);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        SetupRoot.BeginAnimation(OpacityProperty, new DoubleAnimation(1, duration));
        SetupScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, duration) { EasingFunction = easing });
        SetupScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, duration) { EasingFunction = easing });
    }
}
