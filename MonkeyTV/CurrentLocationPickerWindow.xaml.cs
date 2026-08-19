using System.Windows;
using System.Windows.Controls;

namespace MonkeyTV;

public partial class CurrentLocationPickerWindow : Window
{
    private readonly NordLocation? _lastUsed;
    private string? _country;
    private NordLocation? _selection;
    public NordLocation? SelectedLocation { get; private set; }
    public bool SelectDirect { get; private set; }

    public CurrentLocationPickerWindow(NordLocation? lastUsed)
    {
        _lastUsed = lastUsed;
        InitializeComponent();
        ShowCountries();
    }

    private void ShowCountries()
    {
        _country = null; _selection = null; BackButton.Visibility = Visibility.Collapsed;
        TitleText.Text = "Current Location";
        DescriptionText.Text = "Choose the browser route Monkey TV should use.";
        SelectedText.Text = ""; ConnectButton.IsEnabled = false;
        var items = new List<LocationChoice> { new("🌐 Direct", null, true) };
        if (_lastUsed is not null) items.Add(new("🌐 " + _lastUsed.Name + " — Last used", _lastUsed));
        items.AddRange(NordLocationCatalog.Countries.Select(country => new LocationChoice(country, null, false, country)));
        LocationsList.ItemsSource = items;
    }

    private void ShowCities(string country)
    {
        _country = country; _selection = null; BackButton.Visibility = Visibility.Visible;
        TitleText.Text = country; DescriptionText.Text = "Select a location, then connect.";
        SelectedText.Text = "No location selected"; ConnectButton.IsEnabled = false;
        LocationsList.ItemsSource = NordLocationCatalog.ForCountry(country)
            .Select(location => new LocationChoice(location.City is null ? country + " — Automatic" : location.City, location));
    }

    private void LocationsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LocationsList.SelectedItem is not LocationChoice choice) return;
        if (_country is null && choice.Country is not null) { ShowCities(choice.Country); return; }
        if (_country is null && choice.Direct) { SelectDirect = true; DialogResult = true; Close(); return; }
        if (_country is null && choice.Location is not null) { SelectedLocation = choice.Location; DialogResult = true; Close(); return; }
        _selection = choice.Location; SelectedText.Text = _selection is null ? "No location selected" : "Selected: " + _selection.Name;
        ConnectButton.IsEnabled = _selection is not null;
    }
    private void LocationsList_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_country is not null && _selection is not null) Connect_Click(this, new RoutedEventArgs());
    }
    private void Back_Click(object sender, RoutedEventArgs e) => ShowCountries();
    private void Connect_Click(object sender, RoutedEventArgs e) { if (_selection is null) return; SelectedLocation = _selection; DialogResult = true; Close(); }
    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private sealed record LocationChoice(string Label, NordLocation? Location, bool Direct = false, string? Country = null) { public override string ToString() => Label; }
}
