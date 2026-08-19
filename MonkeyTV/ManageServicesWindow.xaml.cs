using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace MonkeyTV;

public partial class ManageServicesWindow : Window
{
    private readonly MonkeySettings _settings;
    private readonly SettingsStore _store;
    private readonly List<ManageServiceItem> _items;
    private readonly List<ServiceDefinition> _pendingCustom = new();

    public ManageServicesWindow(MonkeySettings settings, SettingsStore store)
    {
        InitializeComponent(); _settings = settings; _store = store;
        RegionBox.SelectedIndex = 0;
        _items = BuiltInServiceCatalog.All.Select(template => new ManageServiceItem(template, _settings.Services.FirstOrDefault(service => service.BuiltInServiceId == template.Id))).ToList();
        Refresh();
    }

    private void Refresh()
    {
        var term = SearchBox.Text?.Trim() ?? "";
        var region = (RegionBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All";
        ServicesList.ItemsSource = _items.Where(item => (region == "All" || item.Template.Region == region) && item.Template.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase)).ToList();
    }
    private void FilterChanged(object sender, RoutedEventArgs e) => Refresh();
    private void ServiceChecked(object sender, RoutedEventArgs e) { if (sender is CheckBox { Tag: ManageServiceItem item }) item.RefreshStatus(); }
    private void ServicesList_SelectionChanged(object sender, SelectionChangedEventArgs e) { }
    private void EnableAll_Click(object sender, RoutedEventArgs e) { foreach (var item in _items) item.Enabled = true; Refresh(); }
    private void DisableAll_Click(object sender, RoutedEventArgs e) { foreach (var item in _items) item.Enabled = false; Refresh(); }

    private void RestoreDefaults_Click(object sender, RoutedEventArgs e)
    {
        if (ServicesList.SelectedItem is not ManageServiceItem { Existing: { } service, Customized: true } item) return;
        var confirmation = new ConfirmWindow("Restore service defaults?", $"Restore Monkey TV's default name, URL, artwork, and Experience for {item.Template.Name}?", "Restore") { Owner = this };
        if (confirmation.ShowDialog() != true) return;
        BuiltInServiceCatalog.RestoreDefaults(service, item.Template); item.RefreshStatus(); Refresh();
    }

    private void AddCustom_Click(object sender, RoutedEventArgs e)
    {
        var editor = new ServiceEditorWindow { Owner = this };
        if (editor.ShowDialog() == true) _pendingCustom.Add(editor.Result);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var removals = _items.Where(item => item.Existing is not null && !item.Enabled).Select(item => item.Existing!).ToList();
        if (removals.Count > 0)
        {
            var confirmation = new ConfirmWindow("Remove selected services?", $"Remove {removals.Count} service(s) from Monkey TV? Provider cookies, logins, and site data will not be cleared.", "Remove") { Owner = this };
            if (confirmation.ShowDialog() != true) return;
        }
        foreach (var service in removals) _settings.Services.Remove(service);
        foreach (var item in _items.Where(item => item.Enabled && item.Existing is null))
        {
            var match = BuiltInServiceCatalog.FindProbableCustomMatch(_settings.Services.Concat(_pendingCustom), item.Template);
            if (match is not null)
            {
                var choice = new ConfirmWindow("Possible existing service", $"{match.Name} may already be your {item.Template.Name} service. Add the built-in service anyway?", "Add built-in service") { Owner = this };
                if (choice.ShowDialog() != true) continue;
            }
            _settings.Services.Add(item.Template.Create());
        }
        foreach (var item in _items.Where(item => item.Existing is not null && !item.Customized && item.Existing.BuiltInDefinitionRevision < item.Template.Revision))
            BuiltInServiceCatalog.RestoreDefaults(item.Existing!, item.Template);
        _settings.Services.AddRange(_pendingCustom); _store.Save(_settings); DialogResult = true; Close();
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}

public sealed class ManageServiceItem : INotifyPropertyChanged
{
    private bool _enabled;
    public BuiltInService Template { get; }
    public ServiceDefinition? Existing { get; }
    public BitmapImage? Artwork => BuiltInArtworkCatalog.TryLoad(Template.ArtworkReference);
    public bool Enabled { get => _enabled; set { _enabled = value; RefreshStatus(); OnChanged(); } }
    public bool Customized => Existing is not null && BuiltInServiceCatalog.IsCustomized(Existing, Template);
    public string Status { get; private set; } = "Not added";
    public ManageServiceItem(BuiltInService template, ServiceDefinition? existing) { Template = template; Existing = existing; _enabled = existing is not null; RefreshStatus(); }
    public void RefreshStatus() { Status = Enabled ? (Customized ? "Enabled · Customized" : "Enabled") : Existing is not null ? "Will be removed" : "Not added"; OnChanged(nameof(Status)); OnChanged(nameof(Customized)); }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
