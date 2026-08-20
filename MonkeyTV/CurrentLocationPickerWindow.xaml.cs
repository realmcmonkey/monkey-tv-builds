using System.Windows;
using System.Windows.Controls;

namespace MonkeyTV;

public partial class CurrentLocationPickerWindow : Window
{
    private readonly NordLocation? _lastUsed;
    private NordLocation? _selection;
    private VpnV2ImportedConfig? _v2Selection;
    private readonly IReadOnlyList<VpnV2ImportedConfig> _v2Locations;
    private bool _selectDirect;
    private bool _changingSelection;
    public NordLocation? SelectedLocation { get; private set; }
    public VpnV2ImportedConfig? SelectedV2Location { get; private set; }
    public bool SelectDirect { get; private set; }

    public CurrentLocationPickerWindow(NordLocation? lastUsed, IReadOnlyList<VpnV2ImportedConfig>? v2Locations = null)
    {
        _lastUsed = lastUsed;
        _v2Locations = v2Locations ?? [];
        InitializeComponent();
        CountryList.ItemsSource = new[]
        {
            new CountryChoice("Direct Connection", "Use your normal connection", "⌁", true, true),
            new CountryChoice("United States", "Documented locations available", "US", true),
            CreateV2CountryChoice("Canada", "CA"),
            CreateV2CountryChoice("United Kingdom", "UK")
        };
        if (_lastUsed is not null && NordLocationCatalog.IsPickerLocation(_lastUsed))
        {
            LastUsedText.Text = _lastUsed.Name;
            LastUsedButton.Visibility = Visibility.Visible;
        }
        ShowCountry("United States");
    }

    private void CountryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_changingSelection || CountryList.SelectedItem is not CountryChoice choice) return;
        if (choice.IsDirect) { SelectDirectChoice(); return; }
        ShowCountry(choice.Name);
    }

    private void ShowCountry(string country)
    {
        _selectDirect = false;
        _selection = null;
        _v2Selection = null;
        SearchBox.Text = "";
        SetCountrySelection(country);
        CountryHeadingText.Text = country;
        ClearSelectionButton.Visibility = Visibility.Collapsed;
        SelectedText.Text = "Select a location, then press Connect.";
        ConnectButton.IsEnabled = false;
        UnavailablePanel.Visibility = Visibility.Collapsed;
        if (NordLocationCatalog.ForPickerCountry(country).Count == 0 && !_v2Locations.Any(item => item.Country == country))
        {
            SearchPanel.Visibility = Visibility.Collapsed;
            LocationsList.Visibility = Visibility.Collapsed;
            UnavailablePanel.Visibility = Visibility.Visible;
            CountryDescriptionText.Text = "No supported route is available for this country.";
            UnavailableTitleText.Text = country + " is not available";
            UnavailableDescriptionText.Text = "Nord's documented SOCKS5 list does not include a " + country + " location. Monkey TV will not substitute another country or infer an endpoint.";
            return;
        }
        SearchPanel.Visibility = Visibility.Visible;
        LocationsList.Visibility = Visibility.Visible;
        CountryDescriptionText.Text = _v2Locations.Any(item => item.Country.Equals(country, StringComparison.OrdinalIgnoreCase))
            ? "Choose a downloaded experimental Nord OpenVPN location, then connect."
            : "Choose a documented SOCKS5 location, then connect.";
        PopulateLocations();
    }

    private CountryChoice CreateV2CountryChoice(string country, string symbol)
    {
        var count = _v2Locations.Count(item => item.Country.Equals(country, StringComparison.OrdinalIgnoreCase));
        return new CountryChoice(country, count == 0 ? "Download in Settings → Experiments" :
            $"{count} downloaded experimental location{(count == 1 ? "" : "s")}", symbol, count > 0);
    }

    private void LocationsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_changingSelection || LocationsList.SelectedItem is not LocationChoice choice) return;
        _selection = choice.Location;
        _v2Selection = choice.V2Location;
        _selectDirect = false;
        UpdateSelectionText();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        if (!_changingSelection && CountryList.SelectedItem is CountryChoice choice && !choice.IsDirect && choice.IsAvailable)
            PopulateLocations();
    }

    private void PopulateLocations()
    {
        if (CountryList.SelectedItem is not CountryChoice country) return;
        var query = SearchBox.Text.Trim();
        var locations = NordLocationCatalog.ForPickerCountry(country.Name)
            .Where(location => string.IsNullOrEmpty(query) || location.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) || location.City?.Contains(query, StringComparison.CurrentCultureIgnoreCase) == true)
            .Select(location => new LocationChoice(location.City is null ? "Automatic (United States)" : location.City, "Documented Nord SOCKS5 endpoint", location, null))
            .Concat(_v2Locations.Where(item => item.Country.Equals(country.Name, StringComparison.OrdinalIgnoreCase))
                .Where(item => string.IsNullOrEmpty(query) || item.ServerName.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Select(item => new LocationChoice(item.ServerName, "Experimental • downloaded official Nord OpenVPN config", null, item)))
            .ToList();
        if (_selection is not null && locations.All(item => item.Location?.Id != _selection.Id))
        {
            _selection = null;
            UpdateSelectionText();
        }
        _changingSelection = true;
        LocationsList.ItemsSource = locations;
        LocationsList.SelectedItem = _selection is not null ? locations.FirstOrDefault(item => item.Location?.Id == _selection.Id)
            : _v2Selection is not null ? locations.FirstOrDefault(item => item.V2Location?.Id == _v2Selection.Id) : null;
        _changingSelection = false;
    }

    private void LastUsed_Click(object sender, RoutedEventArgs e)
    {
        if (_lastUsed is null || !NordLocationCatalog.IsPickerLocation(_lastUsed)) return;
        ShowCountry(_lastUsed.Country);
        _selection = _lastUsed;
        PopulateLocations();
        UpdateSelectionText();
    }

    private void SelectDirectChoice()
    {
        _selection = null;
        _v2Selection = null;
        _selectDirect = true;
        SearchBox.Text = "";
        CountryHeadingText.Text = "Direct Connection";
        CountryDescriptionText.Text = "Monkey TV will use your normal connection when you press Connect.";
        SearchPanel.Visibility = Visibility.Collapsed;
        LocationsList.Visibility = Visibility.Collapsed;
        UnavailablePanel.Visibility = Visibility.Collapsed;
        ClearSelectionButton.Visibility = Visibility.Visible;
        SelectedText.Text = "Selected: Direct Connection";
        ConnectButton.IsEnabled = true;
    }

    private void ClearSelection_Click(object sender, RoutedEventArgs e)
    {
        _selection = null;
        _v2Selection = null;
        _selectDirect = false;
        _changingSelection = true;
        CountryList.SelectedItem = null;
        LocationsList.SelectedItem = null;
        _changingSelection = false;
        CountryHeadingText.Text = "Choose a route";
        CountryDescriptionText.Text = "Select Direct Connection or a documented country location.";
        SearchPanel.Visibility = Visibility.Collapsed;
        LocationsList.Visibility = Visibility.Collapsed;
        UnavailablePanel.Visibility = Visibility.Collapsed;
        ClearSelectionButton.Visibility = Visibility.Collapsed;
        SelectedText.Text = "No route selected.";
        ConnectButton.IsEnabled = false;
    }

    private void UpdateSelectionText()
    {
        ClearSelectionButton.Visibility = Visibility.Visible;
        SelectedText.Text = _v2Selection is not null ? "Selected: " + _v2Selection.DisplayName : _selection is null ? "No location selected." : "Selected: " + _selection.Name;
        ConnectButton.IsEnabled = _selection is not null || _v2Selection is not null;
    }

    private void SetCountrySelection(string country)
    {
        _changingSelection = true;
        CountryList.SelectedItem = ((IEnumerable<CountryChoice>)CountryList.ItemsSource).FirstOrDefault(item => item.Name.Equals(country, StringComparison.OrdinalIgnoreCase));
        _changingSelection = false;
    }

    private void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (_selectDirect) { SelectDirect = true; DialogResult = true; Close(); return; }
        if (_selection is null && _v2Selection is null) return;
        SelectedLocation = _selection; SelectedV2Location = _v2Selection;
        DialogResult = true;
        Close();
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }

    private sealed record CountryChoice(string Name, string Detail, string Symbol, bool IsAvailable, bool IsDirect = false)
    {
        public Visibility ChevronVisibility => IsDirect ? Visibility.Collapsed : Visibility.Visible;
    }

    private sealed record LocationChoice(string Label, string Detail, NordLocation? Location, VpnV2ImportedConfig? V2Location);
}
