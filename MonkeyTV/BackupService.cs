using System.IO.Compression;
using System.Text.Json;

namespace MonkeyTV;

public sealed record BackupPreview(MonkeySettings Settings, Dictionary<string, byte[]> Assets);

public static class BackupService
{
    private const int MaxAssetCount = 500;
    private const long MaxTotalAssetBytes = 100L * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public static void Export(string destination, MonkeySettings settings)
    {
        using var stream = File.Create(destination);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        var settingsEntry = archive.CreateEntry("settings.json", CompressionLevel.Optimal);
        using (var writer = new StreamWriter(settingsEntry.Open())) writer.Write(JsonSerializer.Serialize(settings, JsonOptions));
        foreach (var path in ReferencedAssets(settings))
        {
            var relative = Path.GetRelativePath(PortablePaths.DataRoot, path).Replace('\\', '/');
            if (relative.StartsWith("..", StringComparison.Ordinal)) continue;
            archive.CreateEntryFromFile(path, "assets/" + relative, CompressionLevel.Optimal);
        }
    }

    public static BackupPreview Validate(string source)
    {
        using var archive = ZipFile.OpenRead(source);
        var settingsEntry = archive.GetEntry("settings.json") ?? throw new InvalidDataException("This backup does not contain settings.json.");
        if (settingsEntry.Length > 2 * 1024 * 1024) throw new InvalidDataException("The backup configuration is unexpectedly large.");
        MonkeySettings settings;
        using (var reader = new StreamReader(settingsEntry.Open()))
            settings = JsonSerializer.Deserialize<MonkeySettings>(reader.ReadToEnd(), JsonOptions) ?? throw new InvalidDataException("The backup configuration is invalid.");
        ValidateSettings(settings);

        var assets = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in archive.Entries.Where(e => e.FullName.StartsWith("assets/", StringComparison.OrdinalIgnoreCase) && e.Length > 0))
        {
            if (assets.Count >= MaxAssetCount || (total += entry.Length) > MaxTotalAssetBytes) throw new InvalidDataException("The backup contains too much artwork.");
            var relative = entry.FullName[7..].Replace('/', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(relative) || relative.Split(Path.DirectorySeparatorChar).Contains("..")) throw new InvalidDataException("The backup contains an unsafe path.");
            using var memory = new MemoryStream(); entry.Open().CopyTo(memory); assets[relative] = memory.ToArray();
        }
        return new(settings, assets);
    }

    public static MonkeySettings Import(BackupPreview preview, SettingsStore store)
    {
        var assetsRoot = Path.GetFullPath(PortablePaths.AssetsDirectory)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var asset in preview.Assets)
        {
            var target = Path.GetFullPath(Path.Combine(PortablePaths.DataRoot, asset.Key));
            if (!target.StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.WriteAllBytes(target, asset.Value);
        }
        preview.Settings.SetupComplete = true;
        store.Save(preview.Settings);
        StorageService.CleanupOrphanAssets(preview.Settings);
        return preview.Settings;
    }

    private static IEnumerable<string> ReferencedAssets(MonkeySettings settings) => settings.Services
        .SelectMany(s => new[] { s.IconPath, s.BannerPath }).Where(p => !string.IsNullOrWhiteSpace(p))
        .Where(p => !BuiltInArtworkCatalog.IsBuiltInReference(p))
        .Select(p => Path.GetFullPath(Path.Combine(PortablePaths.AppRoot, p!)))
        .Where(p => p.StartsWith(Path.GetFullPath(PortablePaths.AssetsDirectory), StringComparison.OrdinalIgnoreCase) && File.Exists(p)).Distinct(StringComparer.OrdinalIgnoreCase);

    private static void ValidateSettings(MonkeySettings settings)
    {
        if (settings.Services is null || settings.Services.Count > 500) throw new InvalidDataException("The service list is invalid.");
        if (settings.Services.Select(s => s.Id).Distinct().Count() != settings.Services.Count)
            throw new InvalidDataException("The backup contains duplicate service identifiers.");
        foreach (var service in settings.Services)
        {
            if (service.Id == Guid.Empty || string.IsNullOrWhiteSpace(service.Name) || service.Name.Length > 200 ||
                !Uri.TryCreate(service.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                throw new InvalidDataException("The backup contains an invalid service.");
            service.FutureSettings ??= new Dictionary<string, string>();
            ValidatePortableAssetPath(service.IconPath);
            ValidatePortableAssetPath(service.BannerPath);
        }
    }

    private static void ValidatePortableAssetPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (BuiltInArtworkCatalog.IsBuiltInReference(path))
        {
            if (BuiltInArtworkCatalog.TryGet(path) is null)
                throw new InvalidDataException("The backup references unknown built-in artwork.");
            return;
        }
        var full = Path.GetFullPath(Path.Combine(PortablePaths.AppRoot, path));
        var root = Path.GetFullPath(PortablePaths.AssetsDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The backup references artwork outside Monkey TV's portable Assets folder.");
    }
}
