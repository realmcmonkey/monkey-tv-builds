using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;

namespace MonkeyTV;

public partial class ServiceEditorWindow : Window
{
    private readonly ServiceDefinition _service;
    private string? _chosenIcon;
    private string? _chosenBanner;
    private readonly bool _isNew;
    private bool _bannerReset;
    public bool ClearDataRequested { get; private set; }
    public ServiceDefinition Result => _service;

    public ServiceEditorWindow(ServiceDefinition? existing = null)
    {
        InitializeComponent();
        _isNew = existing is null;
        // WebView2 cannot safely clear every storage category by a configured-service name.
        // Never wipe the shared profile as a side effect of editing one service.
        ClearDataButton.IsEnabled = false;
        _service = existing is null ? new ServiceDefinition() : Clone(existing);
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
            ClearDataStatus.Text = "Per-service site-data clearing is unavailable with the shared browser profile. Use Storage to clear safe shared temporary data.";
        }
        UpdateExperienceWarning();
    }

    private static ServiceDefinition Clone(ServiceDefinition s) => new()
    {
        Id = s.Id, Name = s.Name, Url = s.Url, IconPath = s.IconPath, BannerPath = s.BannerPath, BannerFit = s.BannerFit,
        Experience = s.Experience, CustomUserAgentOverride = s.CustomUserAgentOverride,
        ProfileConfiguration = s.ProfileConfiguration, BrowserStorageMode = s.BrowserStorageMode,
        FutureSettings = new Dictionary<string, string>(s.FutureSettings)
    };

    private void ExperienceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateExperienceWarning();
    }

    private void UpdateExperienceWarning()
    {
        if (TvExperienceWarning is not null)
            TvExperienceWarning.Visibility = ExperienceBox?.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ChooseIcon_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.ico;*.webp;*.bmp|All files|*.*" };
        if (picker.ShowDialog(this) != true) return;
        _chosenIcon = picker.FileName;
        IconStatus.Text = picker.FileName;
    }

    private void ChooseBanner_Click(object sender, RoutedEventArgs e)
    {
        var picker = new ArtworkPickerWindow { Owner = this };
        if (picker.ShowDialog() != true) return;
        _chosenBanner = picker.SelectedReference ?? picker.SelectedFilePath;
        _bannerReset = false;
        BannerStatus.Text = DescribeBanner(_chosenBanner);
    }

    private void ResetBanner_Click(object sender, RoutedEventArgs e)
    {
        _chosenBanner = null; _bannerReset = true; BannerStatus.Text = "Generated banner fallback";
    }

    private void ClearData_Click(object sender, RoutedEventArgs e)
    {
        var name = string.IsNullOrWhiteSpace(NameBox.Text) ? "this service" : NameBox.Text.Trim();
        var confirmation = new ConfirmWindow("Clear browser data?", $"This will sign out of {name} and remove its isolated browser profile after you save.", "Clear data") { Owner = this };
        if (confirmation.ShowDialog() != true) return;
        ClearDataRequested = true; ClearDataStatus.Text = "Browser data will be cleared after Save.";
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = "";
        if (string.IsNullOrWhiteSpace(NameBox.Text)) { ErrorText.Text = "Enter a service name."; return; }
        var rawUrl = UrlBox.Text.Trim();
        if (!rawUrl.Contains("://")) rawUrl = "https://" + rawUrl;
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http"))
        { ErrorText.Text = "Enter a valid http or https website address."; return; }
        SaveButton.IsEnabled = false;
        _service.Name = NameBox.Text.Trim(); _service.Url = uri.AbsoluteUri;
        _service.Experience = ExperienceBox.SelectedIndex == 1 ? ServiceExperience.TV : ServiceExperience.Default;
        _service.BannerFit = BannerFitBox.SelectedIndex == 1 ? BannerFit.Fit : BannerFit.Fill;
        _service.CustomUserAgentOverride = string.IsNullOrWhiteSpace(CustomUserAgentBox.Text) ? null : CustomUserAgentBox.Text.Trim();
        _service.LegacyUserAgentMode = null;
        _service.LegacyCustomUserAgent = null;
        _service.BrowserStorageMode = BrowserStorageMode.UnifiedSharedProfile;

        if (!string.IsNullOrWhiteSpace(_chosenIcon) && Path.IsPathRooted(_chosenIcon))
        {
            try
            {
                _service.IconPath = PortableAssetImporter.ImportIcon(_chosenIcon, _service.Id);
            }
            catch (Exception ex) { ErrorText.Text = "Could not copy that icon: " + ex.Message; SaveButton.IsEnabled = true; return; }
        }
        else if (string.IsNullOrWhiteSpace(_chosenIcon))
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
            catch (Exception ex) { ErrorText.Text = "Could not copy that banner: " + ex.Message; SaveButton.IsEnabled = true; return; }
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
        DialogResult = true; Close();
    }

    private static string DescribeBanner(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Automatically choose built-in or website artwork";
        return BuiltInArtworkCatalog.TryGet(value) is { } builtIn ? $"Built-in: {builtIn.Name}" : value;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}
