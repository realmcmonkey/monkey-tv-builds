using MonkeyTV;
using System.IO.Compression;
using System.Windows.Media.Imaging;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

PortablePaths.EnsureCreated();
Require(App.DisplayVersion == "0.04-alpha", "Release version metadata is incorrect.");
Require(App.BuildNumber == "2", "Build #2 metadata is incorrect.");
Require(App.AlphaWarningKey == "0.04-alpha-build-2", "Alpha warning is not keyed to v0.04 Build #2.");
Require(NordLocationCatalog.All.Count >= 3, "Nord location catalog is missing documented presets.");
Require(NordLocationCatalog.Get(NordLocationId.UnitedStates).Host == "us.socks.nordhold.net", "Documented Nord US SOCKS host changed unexpectedly.");
var locationSettings = new MonkeySettings { LastUsedNordLocation = NordLocationId.NewYork };
var locationStore = new SettingsStore(); locationStore.Save(locationSettings);
Require(locationStore.Load().LastUsedNordLocation == NordLocationId.NewYork, "Last used Nord location did not persist as a convenience setting.");
locationSettings.CurrentLocationEnabled = true; locationStore.Save(locationSettings);
Require(locationStore.Load().CurrentLocationEnabled, "Current Location Home visibility preference did not persist.");
await using (var directRouter = new CurrentLocationRouter())
{
    Require(directRouter.State == CurrentLocationState.Direct && directRouter.ActiveLocation is null, "Current Location does not begin Direct.");
    Require(directRouter.EnsureGateway() > 0, "Loopback gateway did not receive a local port.");
    await directRouter.SelectDirectAsync();
    Require(directRouter.State == CurrentLocationState.Direct, "Direct selection did not restore Direct state.");
}
Require(BuiltInArtworkCatalog.All.Count == 46, "The built-in artwork catalog is incomplete.");
Require(BuiltInServiceCatalog.All.Count == BuiltInArtworkCatalog.All.Count, "Manage Services does not match the bundled artwork library.");
Require(BuiltInServiceCatalog.All.All(service => BuiltInArtworkCatalog.TryGet(service.ArtworkReference) is not null), "A built-in service has no bundled artwork.");
var builtInYouTubeTv = BuiltInServiceCatalog.Find("general/youtube-tv") ?? throw new InvalidOperationException("YouTube TV built-in definition is missing.");
var builtInCopy = builtInYouTubeTv.Create();
Require(builtInCopy.BuiltInServiceId == builtInYouTubeTv.Id && builtInCopy.BannerPath == builtInYouTubeTv.ArtworkReference, "Built-in service template was not applied.");
Require(!BuiltInServiceCatalog.IsCustomized(builtInCopy, builtInYouTubeTv), "Fresh built-in service was incorrectly marked customized.");
builtInCopy.Name = "My TV"; Require(BuiltInServiceCatalog.IsCustomized(builtInCopy, builtInYouTubeTv), "Customized built-in service was not recognized.");
BuiltInServiceCatalog.RestoreDefaults(builtInCopy, builtInYouTubeTv); Require(!BuiltInServiceCatalog.IsCustomized(builtInCopy, builtInYouTubeTv), "Restore Defaults did not restore template properties.");
Require(BuiltInArtworkCatalog.All.Select(item => item.Reference).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 46, "Built-in artwork references are not unique.");
Require(BuiltInArtworkCatalog.FindBestMatch("Disney+", "https://www.disneyplus.com/")?.Name == "Disney+", "Disney+ default artwork matching failed.");
Require(BuiltInArtworkCatalog.FindBestMatch("YouTube TV", "https://tv.youtube.com/")?.Name == "YouTube TV", "YouTube TV default artwork matching failed.");
Require(BuiltInArtworkCatalog.TryGet("builtin:general/netflix")?.Name == "Netflix", "Built-in artwork lookup failed.");

var legacyId = Guid.NewGuid();
var legacyJson = $$"""
{
  "SetupComplete": true,
  "Services": [
    {
      "Id": "{{legacyId}}",
      "Name": "Legacy custom",
      "Url": "https://example.test/legacy",
      "UserAgentMode": 1,
      "CustomUserAgent": "Legacy-UA",
      "ProfileConfiguration": "Persistent",
      "BrowserStorageMode": "SharedProfile",
      "FutureSettings": { "SharedProfilePath": "Data/Browser/Shared/EBWebView/WV2Profile_service-example" }
    }
  ]
}
""";

File.WriteAllText(PortablePaths.SettingsFile, legacyJson);
var store = new SettingsStore();
var migrated = store.Load();
Require(migrated.Services.Count == 1, "Legacy service was lost during migration.");
Require(migrated.Services[0].Id == legacyId, "Legacy service ID changed during migration.");
Require(migrated.Services[0].Experience == ServiceExperience.Default, "Legacy custom UA should migrate to Default Experience.");
Require(migrated.Services[0].CustomUserAgentOverride == "Legacy-UA", "Legacy custom UA was not preserved as an override.");
Require(migrated.Services[0].LegacyUserAgentMode is null && migrated.Services[0].LegacyCustomUserAgent is null, "Legacy fields were not cleared.");
Require(migrated.CardStyle == ServiceCardStyle.Banner, "Existing settings did not migrate to Banner card style by default.");
Require(migrated.TileSize == TileSize.Medium, "Existing settings did not retain the Medium default size.");
Require(migrated.Theme == AppTheme.System && migrated.Accent == AppAccent.System, "Existing settings did not migrate to System theme/accent defaults.");
Require(migrated.Services[0].BannerPath is null, "Existing service unexpectedly gained banner artwork.");
Require(migrated.Services[0].BrowserStorageMode == BrowserStorageMode.UnifiedSharedProfile, "Build #2 migration did not select the unified browser profile.");
Require(migrated.Services[0].FutureSettings.ContainsKey("Build1ProfilePath"), "Build #1 named profile was not retained for safe migration.");

var tv = new ServiceDefinition { Name = "TV", Url = "https://www.youtube.com/tv", Experience = ServiceExperience.TV };
Require(BrowserIdentity.ResolveUserAgent(tv) == BrowserIdentity.TvUserAgent, "TV Experience did not resolve the built-in TV UA.");
tv.CustomUserAgentOverride = "Debug-UA";
Require(BrowserIdentity.ResolveUserAgent(tv) == "Debug-UA", "Custom override did not take precedence over TV Experience.");
tv.CustomUserAgentOverride = null;

migrated.Services.Add(tv);
store.Save(migrated);
var reloaded = store.Load();
var restoredTv = reloaded.Services.Single(s => s.Name == "TV");
Require(restoredTv.Experience == ServiceExperience.TV, "TV Experience did not persist across reload.");
Require(BrowserIdentity.ResolveUserAgent(restoredTv) == BrowserIdentity.TvUserAgent, "Reloaded TV Experience did not restore its UA.");
Require(store.Load().Services.All(service => service.BrowserStorageMode == BrowserStorageMode.UnifiedSharedProfile), "New services did not persist the unified browser profile mode.");
Require(StorageService.MeasureServices(store.Load().Services).Count == 0, "Shared browser storage was incorrectly attributed to services.");

foreach (var style in Enum.GetValues<ServiceCardStyle>())
foreach (var size in Enum.GetValues<TileSize>())
{
    reloaded.CardStyle = style;
    reloaded.TileSize = size;
    store.Save(reloaded);
    var appearance = store.Load();
    Require(appearance.CardStyle == style, $"{style} card style did not persist.");
    Require(appearance.TileSize == size, $"{size} card size did not persist.");
}

foreach (var theme in Enum.GetValues<AppTheme>())
foreach (var accent in Enum.GetValues<AppAccent>())
{
    reloaded.Theme = theme; reloaded.Accent = accent;
    reloaded.WindowLeft = 135; reloaded.WindowTop = 95; reloaded.WindowWidth = 1080; reloaded.WindowHeight = 700; reloaded.WindowMaximized = accent == AppAccent.Red;
    store.Save(reloaded);
    var appearance = store.Load();
    Require(appearance.Theme == theme && appearance.Accent == accent, "Theme/accent did not persist.");
    Require(appearance.WindowLeft == 135 && appearance.WindowWidth == 1080, "Window bounds did not persist.");
}

Require(ExternalNavigationPolicy.TryHttpUri("https://example.test/path", out _), "Safe HTTPS URL was rejected.");
Require(!ExternalNavigationPolicy.TryHttpUri("file:///private.txt", out _), "Non-web popup URL was accepted.");
Require(ExternalNavigationPolicy.IsRelatedOrAuthentication(new Uri("https://video.example.test"), new Uri("https://auth.example.test/login")), "Related authentication popup was not recognized.");

var sourceBanner = Path.Combine(PortablePaths.CacheDirectory, "manual-banner.png");
var bannerBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
File.WriteAllBytes(sourceBanner, bannerBytes);
var importedBanner = PortableAssetImporter.ImportBanner(sourceBanner, legacyId);
Require(!Path.IsPathRooted(importedBanner), "Imported banner path was not portable/relative.");
Require(importedBanner.StartsWith(Path.Combine("Data", "Assets", "Banners"), StringComparison.OrdinalIgnoreCase), "Imported banner was not stored below Data/Assets/Banners.");
var importedBannerFile = Path.Combine(PortablePaths.AppRoot, importedBanner);
Require(File.Exists(importedBannerFile), "Imported banner file is missing.");
Require(Path.GetExtension(importedBannerFile).Equals(".jpg", StringComparison.OrdinalIgnoreCase), "Imported banner was not stored efficiently as JPEG.");
Require(new FileInfo(importedBannerFile).Length > 0, "Imported banner is empty.");
Require(PortableAssetImporter.ImportBanner(sourceBanner, Guid.NewGuid()) == importedBanner, "Identical banners were not deduplicated.");

var oversizedBmp = Path.Combine(PortablePaths.CacheDirectory, "oversized.bmp");
const int bmpWidth = 2000, bmpHeight = 1200, bmpStride = bmpWidth * 3, bmpPayload = bmpStride * bmpHeight;
using (var stream = File.Create(oversizedBmp))
using (var writer = new BinaryWriter(stream))
{
    writer.Write((byte)'B'); writer.Write((byte)'M'); writer.Write(54 + bmpPayload); writer.Write(0); writer.Write(54);
    writer.Write(40); writer.Write(bmpWidth); writer.Write(bmpHeight); writer.Write((short)1); writer.Write((short)24);
    writer.Write(0); writer.Write(bmpPayload); writer.Write(2835); writer.Write(2835); writer.Write(0); writer.Write(0);
    writer.Write(new byte[bmpPayload]);
}
var optimizedPath = Path.Combine(PortablePaths.AppRoot, PortableAssetImporter.ImportBanner(oversizedBmp, Guid.NewGuid()));
using (var stream = File.OpenRead(optimizedPath))
{
    var optimized = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
    Require(optimized.PixelWidth <= 1600 && optimized.PixelHeight <= 900, "Oversized banner was not downscaled.");
}
Require(new FileInfo(optimizedPath).Length < new FileInfo(oversizedBmp).Length, "Optimized banner was not compressed.");

migrated.Services[0].BannerPath = importedBanner;
store.Save(migrated);
Require(store.Load().Services[0].BannerPath == importedBanner, "Service banner path did not persist.");

var serialized = File.ReadAllText(PortablePaths.SettingsFile);
Require(!serialized.Contains("UserAgentMode", StringComparison.Ordinal), "Migrated legacy mode was written back to settings.");
Require(!serialized.Contains("CustomUserAgent\"", StringComparison.Ordinal), "Migrated legacy custom field was written back to settings.");

restoredTv.BannerFit = BannerFit.Fit;
restoredTv.BannerPath = "builtin:general/youtube-tv";
store.Save(reloaded);
Require(store.Load().Services.Single(s => s.Id == restoredTv.Id).BannerFit == BannerFit.Fit, "Banner Fit did not persist.");
Require(store.Load().Services.Single(s => s.Id == restoredTv.Id).BannerPath == "builtin:general/youtube-tv", "Built-in artwork selection did not persist.");
Require(PrereleaseVersion.Compare("v0.0.2-alpha", "v0.0.1-alpha") > 0, "Prerelease version comparison failed.");
Require(PrereleaseVersion.Compare("v1.0.0", "v1.0.0-beta") > 0, "Stable version should sort after its prerelease.");

var sensitiveProfile = PortablePaths.ProfileFor(restoredTv.Id);
File.WriteAllText(Path.Combine(sensitiveProfile, "cookie-secret.test"), "must-not-export");
var backupFile = Path.Combine(PortablePaths.CacheDirectory, "smoke.mtvbackup");
BackupService.Export(backupFile, reloaded);
using (var archive = ZipFile.OpenRead(backupFile))
{
    Require(archive.GetEntry("settings.json") is not null, "Backup omitted settings.");
    Require(archive.Entries.All(e => !e.FullName.Contains("Profiles", StringComparison.OrdinalIgnoreCase)), "Backup exported browser-profile data.");
}
var preview = BackupService.Validate(backupFile);
Require(preview.Settings.Services.Count == reloaded.Services.Count, "Backup validation lost services.");
Require(preview.Settings.Services.Single(s => s.Id == restoredTv.Id).BannerPath == "builtin:general/youtube-tv", "Backup validation lost a built-in artwork reference.");
var importedSettings = BackupService.Import(preview, store);
Require(importedSettings.Services.Count == reloaded.Services.Count, "Backup import lost services.");
Require(File.Exists(Path.Combine(sensitiveProfile, "cookie-secret.test")), "Configuration import unexpectedly modified browser profile data.");
var invalidBackup = Path.Combine(PortablePaths.CacheDirectory, "invalid.mtvbackup");
File.WriteAllText(invalidBackup, "not a backup");
var invalidRejected = false;
try { BackupService.Validate(invalidBackup); } catch { invalidRejected = true; }
Require(invalidRejected, "Malformed backup was not rejected.");

var disposableCache = Path.Combine(sensitiveProfile, "Cache");
Directory.CreateDirectory(disposableCache);
File.WriteAllText(Path.Combine(disposableCache, "disposable.test"), "remove");
StorageService.ClearTemporaryData(restoredTv.Id);
Require(!Directory.Exists(disposableCache), "Disposable service cache was not removed.");
Require(File.Exists(Path.Combine(sensitiveProfile, "cookie-secret.test")), "Temporary cleanup removed persistent service data.");

File.WriteAllText(PortablePaths.SessionMarkerFile, "{\"Active\":true}");
Require(SessionMarker.Begin().Unclean, "Unclean-session marker was not detected.");
SessionMarker.MarkClean();
Require(!File.Exists(PortablePaths.SessionMarkerFile), "Clean-session marker was not removed.");

var storageProfile = PortablePaths.LegacyProfileFor(Guid.NewGuid());
var temporaryFolder = Path.Combine(storageProfile, "EBWebView", "Default", "DawnWebGPUCache");
var persistentFolder = Path.Combine(storageProfile, "EBWebView", "Default", "Local Storage");
Directory.CreateDirectory(temporaryFolder); Directory.CreateDirectory(persistentFolder);
File.WriteAllBytes(Path.Combine(temporaryFolder, "shader.bin"), new byte[96 * 1024]);
File.WriteAllText(Path.Combine(persistentFolder, "keep.test"), "site-state");
Require(StorageService.GetSnapshot().BrowserTemporaryBytes >= 96 * 1024, "Temporary browser cache was not classified.");
File.WriteAllBytes(Path.Combine(temporaryFolder, "over-soft-budget.bin"), new byte[StorageService.TemporaryCacheBudgetBytes + 1]);
Require(StorageService.ShouldClearActiveDiskCache(), "Small active cache budget was not enforced.");
StorageService.ClearAllTemporaryData();
Require(!Directory.Exists(temporaryFolder), "Known temporary browser cache was not removed.");
Require(File.Exists(Path.Combine(persistentFolder, "keep.test")), "Temporary cleanup removed persistent DOM storage.");

var queuedSharedProfile = Path.Combine(PortablePaths.SharedBrowserUserDataDirectory, "synthetic-profile");
Directory.CreateDirectory(queuedSharedProfile); File.WriteAllText(Path.Combine(queuedSharedProfile, "state.test"), "remove");
StorageService.QueueSharedProfileDeletion(Path.GetRelativePath(PortablePaths.AppRoot, queuedSharedProfile));
StorageService.CleanupQueuedSharedProfileDeletes();
Require(!Directory.Exists(queuedSharedProfile), "Queued removed shared profile was not cleaned at a safe point.");

for (var i = 0; i < 500; i++) AppLog.Write("smoke", new string('x', 1200));
var retainedLogBytes = Directory.Exists(PortablePaths.LogsDirectory)
    ? Directory.EnumerateFiles(PortablePaths.LogsDirectory, "monkey.log*").Sum(f => new FileInfo(f).Length) : 0;
Require(retainedLogBytes <= AppLog.MaximumRetainedBytes + 4096, "Log retention exceeded its bound.");

Console.WriteLine("PASS: migration, Experience, appearance, TV UA, banner fit/dedup, backup privacy/import, cache classification/cleanup, recovery marker, prerelease versions, and bounded logs.");
