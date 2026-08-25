using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace MonkeyTV;

// Shared selection model for the integrated Services catalog and first-time setup.
// It keeps catalog choices in memory until the owning screen decides to commit them.
public sealed class ServiceLibrarySelectionItem : INotifyPropertyChanged
{
    private bool _enabled;

    public ServiceLibrarySelectionItem(BuiltInService template, ServiceDefinition? existing)
    {
        Template = template;
        Existing = existing;
        _enabled = existing is not null;
        RefreshStatus();
    }

    public BuiltInService Template { get; }
    public ServiceDefinition? Existing { get; }
    public BitmapImage? Artwork => BuiltInArtworkCatalog.TryLoad(Template.ArtworkReference);
    public bool Customized => Existing is not null && BuiltInServiceCatalog.IsCustomized(Existing, Template);
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            RefreshStatus();
            OnChanged();
        }
    }

    public string Status { get; private set; } = "Not added";

    public void RefreshStatus()
    {
        Status = Enabled ? (Customized ? "Added · customized" : "Added") : Existing is not null ? "Will be removed" : "Not added";
        OnChanged(nameof(Status));
        OnChanged(nameof(Customized));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public static class ServiceLibrarySelection
{
    public static IReadOnlyList<ServiceLibrarySelectionItem> CreateItems(MonkeySettings settings) =>
        BuiltInServiceCatalog.All.Select(template => new ServiceLibrarySelectionItem(template,
            settings.Services.FirstOrDefault(service => string.Equals(service.BuiltInServiceId, template.Id, StringComparison.OrdinalIgnoreCase)))).ToList();

    public static IEnumerable<BuiltInService> Filter(string? query, string? region) =>
        BuiltInServiceCatalog.All.Where(template =>
            (string.IsNullOrWhiteSpace(region) || region == "All" || template.Region == region) &&
            (string.IsNullOrWhiteSpace(query) || template.Name.Contains(query.Trim(), StringComparison.CurrentCultureIgnoreCase)))
            .OrderBy(template => template.Name, StringComparer.CurrentCultureIgnoreCase);

    public static ServiceDefinition? FindConfigured(MonkeySettings settings, BuiltInService template) =>
        settings.Services.FirstOrDefault(service => string.Equals(service.BuiltInServiceId, template.Id, StringComparison.OrdinalIgnoreCase));
}
