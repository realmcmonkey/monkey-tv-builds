using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;

namespace MonkeyTV;

public partial class ServiceEditorWindow : Window
{
    public enum EmbeddedCompletion { Cancelled, Saved, TestRequested, ImplicitDismissalRequested }

    private readonly ServiceDefinition _service;
    private string? _chosenIcon;
    private string? _chosenBanner;
    private readonly bool _isNew;
    private bool _bannerReset;
    // XAML populates ComboBox items during InitializeComponent and raises SelectionChanged
    // before the draft service is assigned. Keep preview refresh disabled until construction
    // has supplied a real draft.
    private bool _initializing = true;
    private Action<EmbeddedCompletion>? _embeddedCompletion;
    private Action? _implicitDismissalRequested;
    private Func<ServiceDefinition, Task<bool>>? _immediateResetHandler;
    private EditorDraftState? _originalDraft;
    private bool _explicitClose;
    private bool _promptingForUnsavedChanges;
    public bool ClearDataRequested { get; private set; }
    public bool TestRequested { get; private set; }
    public ServiceDefinition Result => _service;

    public FrameworkElement DetachForEmbeddedHost(Action<EmbeddedCompletion> completion)
    {
        _embeddedCompletion = completion;
        // Once hosted by the main-window drawer, Escape and dismissal are owned by
        // MainWindow. Retaining this standalone Window's handlers can dispatch a
        // second dismissal while the drawer is prompting or animating closed.
        PreviewKeyDown -= ServiceEditorWindow_PreviewKeyDown;
        Closing -= ServiceEditorWindow_Closing;
        var content = (FrameworkElement)Content;
        Content = null;
        return content;
    }

    public void SetImmediateResetHandler(Func<ServiceDefinition, Task<bool>> handler) => _immediateResetHandler = handler;
    public void SetImplicitDismissalHandler(Action handler) => _implicitDismissalRequested = handler;
    internal bool HasMeaningfulUnsavedChanges => _originalDraft is not null && !Equals(_originalDraft, CaptureDraftState());

    // In drawer mode Content is detached into MainWindow's host, so it is null here.
    // Use the main application window as the modal owner in that case.
    private Window DialogOwner => Content is DependencyObject content
        ? Window.GetWindow(content) ?? Application.Current.MainWindow ?? this
        : Application.Current.MainWindow ?? this;

    public ServiceEditorWindow(ServiceDefinition? existing = null, ServiceExperience defaultExperience = ServiceExperience.Default)
    {
        InitializeComponent();
        _isNew = existing is null;
        _service = existing is null ? new ServiceDefinition { Experience = defaultExperience } : Clone(existing);
        ClearDataButton.IsEnabled = existing is not null;
        PreferredVpnLocationBox.SelectedIndex = (int)_service.PreferredVpnLocation;
        if (existing is null) ExperienceBox.SelectedIndex = defaultExperience == ServiceExperience.TV ? 1 : 0;
        if (existing is not null)
        {
            Heading.Text = "Edit service"; SaveButton.Content = "Save changes";
            NameBox.Text = _service.Name; UrlBox.Text = _service.Url;
            _chosenIcon = _service.IconPath;
            _chosenBanner = _service.BannerPath;
            IconStatus.Text = string.IsNullOrWhiteSpace(_chosenIcon) ? "Automatically obtain from website" : _chosenIcon;
            BannerStatus.Text = DescribeBanner(_chosenBanner);
            BannerFitBox.SelectedIndex = _service.BannerFit == BannerFit.Fit ? 1 : 0;
            ExperienceBox.SelectedIndex = _service.Experience == ServiceExperience.TV ? 1 : 0;
            CustomUserAgentBox.Text = _service.CustomUserAgentOverride ?? "";
            ClearDataStatus.Text = "Reset affects only this service and takes effect on its next launch.";
        }
        _initializing = false;
        _originalDraft = CaptureDraftState();
        RefreshDraftPresentation();
        PreviewKeyDown += ServiceEditorWindow_PreviewKeyDown;
        Closing += ServiceEditorWindow_Closing;
    }

    private static ServiceDefinition Clone(ServiceDefinition s) => new()
    {
        Id = s.Id, Name = s.Name, Url = s.Url, IconPath = s.IconPath, BannerPath = s.BannerPath, BannerFit = s.BannerFit,
        Experience = s.Experience, CustomUserAgentOverride = s.CustomUserAgentOverride,
        ProfileConfiguration = s.ProfileConfiguration, BrowserStorageMode = s.BrowserStorageMode,
        BuiltInServiceId = s.BuiltInServiceId, BuiltInDefinitionRevision = s.BuiltInDefinitionRevision, AddedUtc = s.AddedUtc,
        IsFavorite = s.IsFavorite, IsHiddenFromHome = s.IsHiddenFromHome,
        PreferredVpnLocation = s.PreferredVpnLocation,
        FutureSettings = new Dictionary<string, string>(s.FutureSettings)
    };

    private void ExperienceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateExperienceWarning();
        RefreshDraftPresentation();
    }

    private void DraftField_Changed(object sender, RoutedEventArgs e) => RefreshDraftPresentation();

    private void UpdateExperienceWarning()
    {
        if (TvExperienceWarning is not null)
            TvExperienceWarning.Visibility = ExperienceBox?.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ChooseIcon_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.ico;*.webp;*.bmp|All files|*.*" };
        if (picker.ShowDialog(DialogOwner) != true) return;
        _chosenIcon = picker.FileName;
        IconStatus.Text = picker.FileName;
        RefreshDraftPresentation();
    }

    private void ChooseBanner_Click(object sender, RoutedEventArgs e)
    {
        var picker = new ArtworkPickerWindow { Owner = DialogOwner };
        if (picker.ShowDialog() != true) return;
        _chosenBanner = picker.SelectedReference ?? picker.SelectedFilePath;
        _bannerReset = false;
        BannerStatus.Text = DescribeBanner(_chosenBanner);
        RefreshDraftPresentation();
    }

    private void ResetBanner_Click(object sender, RoutedEventArgs e)
    {
        _chosenBanner = null; _bannerReset = true; BannerStatus.Text = "Generated banner fallback";
        RefreshDraftPresentation();
    }

    private async void ClearData_Click(object sender, RoutedEventArgs e)
    {
        if (_immediateResetHandler is null) return;
        ClearDataButton.IsEnabled = false;
        try
        {
            if (await _immediateResetHandler(_service))
                ClearDataStatus.Text = $"{_service.Name} was reset. It will reload as a first visit the next time it opens.";
        }
        finally { ClearDataButton.IsEnabled = true; }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (await SaveDraftAsync())
        {
            _explicitClose = true;
            Complete(EmbeddedCompletion.Saved);
        }
    }

    internal async Task<bool> SaveFromUnsavedChangesPromptAsync()
    {
        if (!await SaveDraftAsync()) return false;
        _explicitClose = true;
        Complete(EmbeddedCompletion.Saved);
        return true;
    }

    private async Task<bool> SaveDraftAsync()
    {
        if (!TryBuildDraft(showErrors: true)) return false;
        SaveButton.IsEnabled = false;

        if (!string.IsNullOrWhiteSpace(_chosenIcon) && Path.IsPathRooted(_chosenIcon))
        {
            try
            {
                _service.IconPath = PortableAssetImporter.ImportIcon(_chosenIcon, _service.Id);
            }
            catch (Exception ex) { ErrorText.Text = "Could not copy that icon: " + ex.Message; SaveButton.IsEnabled = true; return false; }
        }
        else if (_isNew && string.IsNullOrWhiteSpace(_chosenIcon))
        {
            IconStatus.Text = "Looking for an icon…";
            _service.IconPath = await IconService.TryDownloadAsync(_service);
        }
        if (BuiltInArtworkCatalog.IsBuiltInReference(_chosenBanner))
        {
            _service.BannerPath = BuiltInArtworkCatalog.TryGet(_chosenBanner) is not null ? _chosenBanner : null;
        }
        else if (!string.IsNullOrWhiteSpace(_chosenBanner) && Path.IsPathRooted(_chosenBanner))
        {
            try
            {
                _service.BannerPath = PortableAssetImporter.ImportBanner(_chosenBanner, _service.Id);
            }
            catch (Exception ex) { ErrorText.Text = "Could not copy that banner: " + ex.Message; SaveButton.IsEnabled = true; return false; }
        }
        else if (_bannerReset)
        {
            _service.BannerPath = null;
        }
        else if (_isNew && string.IsNullOrWhiteSpace(_chosenBanner))
        {
            BannerStatus.Text = "Looking for landscape artwork…";
            var builtIn = BuiltInArtworkCatalog.FindBestMatch(_service.Name, _service.Url);
            if (builtIn is not null)
            {
                _service.BannerPath = builtIn.Reference;
                BannerStatus.Text = $"Built-in: {builtIn.Name}";
            }
            else
            {
                _service.BannerPath = await ArtworkService.TryDownloadBannerAsync(_service);
            }
        }
        return true;
    }

    private void TestUrl_Click(object sender, RoutedEventArgs e) => RequestTest("URL");

    private void TestConfiguration_Click(object sender, RoutedEventArgs e) => RequestTest("configuration");

    private void RequestTest(string target)
    {
        if (!TryBuildDraft(showErrors: true)) return;
        TestRequested = true;
        ErrorText.Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush");
        ErrorText.Text = $"Opening this unsaved {target} in Monkey TV. Save is still required to keep these changes.";
        _explicitClose = true;
        Complete(EmbeddedCompletion.TestRequested);
    }

    private bool TryBuildDraft(bool showErrors)
    {
        ErrorText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 119, 119));
        ErrorText.Text = ""; UrlValidationText.Text = "";
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            if (showErrors) ErrorText.Text = "Enter a service name.";
            return false;
        }
        var rawUrl = UrlBox.Text.Trim();
        if (!rawUrl.Contains("://")) rawUrl = "https://" + rawUrl;
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http"))
        {
            if (showErrors) UrlValidationText.Text = "Enter a valid http or https website address before testing or saving.";
            return false;
        }
        _service.Name = NameBox.Text.Trim(); _service.Url = uri.AbsoluteUri;
        _service.Experience = ExperienceBox.SelectedIndex == 1 ? ServiceExperience.TV : ServiceExperience.Default;
        _service.BannerFit = BannerFitBox.SelectedIndex == 1 ? BannerFit.Fit : BannerFit.Fill;
        _service.PreferredVpnLocation = PreferredVpnLocationBox.SelectedIndex switch
        {
            1 => PreferredVpnLocation.Direct,
            2 => PreferredVpnLocation.USA,
            3 => PreferredVpnLocation.Canada,
            4 => PreferredVpnLocation.UnitedKingdom,
            _ => PreferredVpnLocation.None
        };
        _service.CustomUserAgentOverride = string.IsNullOrWhiteSpace(CustomUserAgentBox.Text) ? null : CustomUserAgentBox.Text.Trim();
        _service.LegacyUserAgentMode = null;
        _service.LegacyCustomUserAgent = null;
        _service.BrowserStorageMode = BrowserStorageMode.UnifiedSharedProfile;
        _service.IconPath = _chosenIcon;
        _service.BannerPath = _bannerReset ? null : _chosenBanner;
        return true;
    }

    private void RefreshDraftPresentation()
    {
        if (_initializing || PreviewHost is null) return;
        var preview = Clone(_service);
        preview.Name = string.IsNullOrWhiteSpace(NameBox.Text) ? "Service name" : NameBox.Text.Trim();
        preview.BannerFit = BannerFitBox.SelectedIndex == 1 ? BannerFit.Fit : BannerFit.Fill;
        preview.Experience = ExperienceBox.SelectedIndex == 1 ? ServiceExperience.TV : ServiceExperience.Default;
        preview.CustomUserAgentOverride = string.IsNullOrWhiteSpace(CustomUserAgentBox.Text) ? null : CustomUserAgentBox.Text.Trim();
        preview.IconPath = _chosenIcon;
        preview.BannerPath = _bannerReset ? null : _chosenBanner;
        PreviewCard.Background = MainWindow.CreateCardBackground(preview);
        PreviewHost.Children.Clear();
        PreviewHost.Children.Add(MainWindow.CreateCardArtwork(preview, 620, 349, banner: true, showServiceName: true));
        PreviewNameText.Text = preview.Name;

        var custom = !string.IsNullOrWhiteSpace(preview.CustomUserAgentOverride);
        var effective = BrowserIdentity.ResolveUserAgent(preview);
        ExperienceSummaryText.Text = custom
            ? $"{(preview.Experience == ServiceExperience.TV ? "TV Experience" : "Default Experience")} • Custom user-agent override"
            : preview.Experience == ServiceExperience.TV ? "TV Experience • Monkey TV TV user agent" : "Default Experience • WebView2 default user agent";
        EffectiveUserAgentText.Text = effective is null
            ? "Uses the installed WebView2 Runtime default user-agent."
            : effective;
    }

    private static string DescribeBanner(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Automatically choose built-in or website artwork";
        return BuiltInArtworkCatalog.TryGet(value) is { } builtIn ? $"Built-in: {builtIn.Name}" : value;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _explicitClose = true;
        Complete(EmbeddedCompletion.Cancelled);
    }

    private void ImplicitDismiss_Click(object sender, RoutedEventArgs e) => RequestImplicitDismissal();

    internal void RequestImplicitDismissal()
    {
        if (_embeddedCompletion is not null)
        {
            _implicitDismissalRequested?.Invoke();
            return;
        }
        Close();
    }

    internal void DiscardFromUnsavedChangesPrompt()
    {
        _explicitClose = true;
        Complete(EmbeddedCompletion.Cancelled);
    }

    private void ServiceEditorWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Escape) return;
        e.Handled = true;
        RequestImplicitDismissal();
    }

    private void ServiceEditorWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_embeddedCompletion is not null || _explicitClose || !HasMeaningfulUnsavedChanges || _promptingForUnsavedChanges) return;
        e.Cancel = true;
        _promptingForUnsavedChanges = true;
        Dispatcher.BeginInvoke(async () =>
        {
            var choice = ShowUnsavedChangesPrompt();
            _promptingForUnsavedChanges = false;
            if (choice == UnsavedChangesChoice.DiscardChanges)
            {
                _explicitClose = true;
                DialogResult = false;
                Close();
            }
            else if (choice == UnsavedChangesChoice.SaveChanges)
            {
                if (await SaveDraftAsync())
                {
                    _explicitClose = true;
                    DialogResult = true;
                    Close();
                }
            }
        });
    }

    internal UnsavedChangesChoice ShowUnsavedChangesPrompt()
    {
        var title = string.IsNullOrWhiteSpace(NameBox.Text) ? "this service" : NameBox.Text.Trim();
        var prompt = new UnsavedChangesWindow(title) { Owner = DialogOwner };
        prompt.ShowDialog();
        return prompt.Choice;
    }

    private EditorDraftState CaptureDraftState() => new(
        NameBox?.Text.Trim() ?? string.Empty,
        NormalizeUrl(UrlBox?.Text),
        NormalizePath(_chosenIcon),
        NormalizePath(_bannerReset ? null : _chosenBanner),
        BannerFitBox?.SelectedIndex == 1 ? BannerFit.Fit : BannerFit.Fill,
        ExperienceBox?.SelectedIndex == 1 ? ServiceExperience.TV : ServiceExperience.Default,
        string.IsNullOrWhiteSpace(CustomUserAgentBox?.Text) ? null : CustomUserAgentBox.Text.Trim(),
        PreferredVpnLocationBox?.SelectedIndex switch
        {
            1 => PreferredVpnLocation.Direct,
            2 => PreferredVpnLocation.USA,
            3 => PreferredVpnLocation.Canada,
            4 => PreferredVpnLocation.UnitedKingdom,
            _ => PreferredVpnLocation.None
        });

    private static string NormalizeUrl(string? raw)
    {
        var value = raw?.Trim() ?? string.Empty;
        if (!value.Contains("://", StringComparison.Ordinal)) value = "https://" + value;
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? uri.AbsoluteUri
            : raw?.Trim() ?? string.Empty;
    }

    private static string? NormalizePath(string? path) => string.IsNullOrWhiteSpace(path) ? null : path.Trim();
    private sealed record EditorDraftState(string Name, string Url, string? IconPath, string? BannerPath,
        BannerFit BannerFit, ServiceExperience Experience, string? CustomUserAgent, PreferredVpnLocation PreferredVpnLocation);

    private void Complete(EmbeddedCompletion completion)
    {
        if (_embeddedCompletion is { } callback)
        {
            _embeddedCompletion = null;
            callback(completion);
            return;
        }
        // The embedded drawer deliberately detaches this Window's content without ever
        // showing the Window itself. A repeated Escape during its close animation must
        // not attempt to assign DialogResult to that never-shown Window.
        if (!IsLoaded) return;
        DialogResult = completion == EmbeddedCompletion.Saved;
        Close();
    }
}
