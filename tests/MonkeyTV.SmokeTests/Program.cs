using MonkeyTV;
using System.IO.Compression;
using System.Reflection;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

PortablePaths.EnsureCreated();
Require(App.DisplayVersion == "0.06-alpha", "Release version metadata is incorrect.");
Require(App.BuildNumber == "1", "Build #1 metadata is incorrect.");
Require(App.AlphaWarningKey == "0.06-alpha-build-1", "Alpha warning is not keyed to v0.06 Build #1.");
Require(NordLocationCatalog.All.Count >= 3, "Nord location catalog is missing documented presets.");
var absentRuntime = WebViewRuntimeResolver.InspectFixedRuntime(Path.Combine(PortablePaths.CacheDirectory, "missing-fixed-runtime"));
Require(absentRuntime.Kind == WebViewRuntimeKind.FixedUnavailable && !absentRuntime.IsFixed, "A missing Fixed Version runtime was not rejected.");
Require(!WebViewRuntimeResolver.TryRequireFixedForCurrentLocationV2(out var selectedRuntime, out var fixedRuntimeRequirement) || selectedRuntime.IsFixed, "Current Location v2 accepted an unvalidated runtime.");
if (!selectedRuntime.IsFixed) Require(fixedRuntimeRequirement.Contains("Fixed Version", StringComparison.Ordinal), "Current Location v2 prerequisite message is unclear.");
Require(NordLocationCatalog.Get(NordLocationId.UnitedStates).Host == "us.socks.nordhold.net", "Documented Nord US SOCKS host changed unexpectedly.");
Require(NordLocationCatalog.PickerCountries.SequenceEqual(new[] { "United States", "Canada", "United Kingdom" }), "Current Location picker countries changed unexpectedly.");
Require(NordLocationCatalog.ForPickerCountry("United States").Select(location => location.City ?? "Automatic").SequenceEqual(new[] { "Automatic", "Atlanta", "Chicago", "Dallas", "Los Angeles", "New York", "Phoenix", "San Francisco" }), "U.S. documented SOCKS5 locations are incomplete or not alphabetical.");
Require(!NordLocationCatalog.ForPickerCountry("Canada").Any() && !NordLocationCatalog.ForPickerCountry("United Kingdom").Any(), "Unsupported Nord SOCKS5 countries must not be fabricated.");
var locationSettings = new MonkeySettings { LastUsedNordLocation = NordLocationId.NewYork };
var locationStore = new SettingsStore(); locationStore.Save(locationSettings);
Require(locationStore.Load().LastUsedNordLocation == NordLocationId.NewYork, "Last used Nord location did not persist as a convenience setting.");
Require(locationStore.Load().LastUsedVpnLocation?.Socks5Location == NordLocationId.NewYork, "Legacy SOCKS5 last used location did not migrate to unified VPN Location.");
locationSettings.CurrentLocationEnabled = true; locationStore.Save(locationSettings);
Require(locationStore.Load().CurrentLocationEnabled, "Current Location Home visibility preference did not persist.");
Require(VpnV2LocationCatalog.All.Select(item => item.Country).SequenceEqual(new[] { "Canada", "United Kingdom" }), "VPN Location download catalog changed unexpectedly.");
if (args.Contains("--vpn-v2-download-test", StringComparer.OrdinalIgnoreCase))
{
    var result = await VpnV2LocationCatalog.DownloadCountryAsync(VpnV2LocationCatalog.All.Single(item => item.Country == "Canada"));
    var downloaded = VpnV2ConfigStore.Load().Where(item => item.Country == "Canada").ToList();
    Require(result.InstalledCount > 1 && downloaded.Count == result.InstalledCount && downloaded.All(item => File.Exists(item.FullPath)),
        "VPN Location did not install all matching official Nord country configurations.");
    foreach (var item in downloaded) VpnV2ConfigStore.Remove(item.Id);
}
locationSettings = new MonkeySettings { VpnLocationV2ExperimentEnabled = true }; locationStore.Save(locationSettings);
Require(locationStore.Load().CurrentLocationEnabled && !locationStore.Load().VpnLocationV2ExperimentEnabled, "Legacy VPN Location experiment setting did not migrate.");
var v2ImportRoot = Path.Combine(Path.GetTempPath(), "MonkeyTV-v2-config-test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(v2ImportRoot);
var v2ImportPath = Path.Combine(v2ImportRoot, "ca1968.nordvpn.com.udp_2.6.ovpn");
File.WriteAllText(v2ImportPath, """
client
dev tun
proto udp
remote 187.13.240.179 53
remote-cert-tls server
auth-user-pass
<ca>
test-public-ca
</ca>
<tls-auth>
test-public-tls-auth
</tls-auth>
""");
var importedV2 = VpnV2ConfigStore.Import(v2ImportPath);
Require(importedV2.Country == "Canada" && importedV2.ServerName == "ca1968.nordvpn.com" && File.Exists(importedV2.FullPath), "The Canada configuration was not imported safely.");
VpnV2ConfigStore.Remove(importedV2.Id);
Directory.Delete(v2ImportRoot, true);
await using (var directRouter = new CurrentLocationRouter())
{
    Require(directRouter.State == CurrentLocationState.Direct && directRouter.ActiveLocation is null, "Current Location does not begin Direct.");
    Require(directRouter.EnsureGateway() > 0, "Loopback gateway did not receive a local port.");
    Require(await directRouter.SelectDirectAsync(), "Direct route verification failed.");
    Require(directRouter.State == CurrentLocationState.Direct, "Direct selection did not restore Direct state.");
}
Require(BuiltInArtworkCatalog.All.Count == 46, "The built-in artwork catalog is incomplete.");
Require(BuiltInServiceCatalog.All.Count >= BuiltInArtworkCatalog.All.Count, "Manage Services is missing bundled artwork choices.");
Require(BuiltInServiceCatalog.All.All(service => BuiltInArtworkCatalog.TryGet(service.ArtworkReference) is not null), "A built-in service has no bundled artwork.");

var builtInYouTubeDesktop = BuiltInServiceCatalog.Find("general/youtube") ?? throw new InvalidOperationException("YouTube Desktop built-in definition is missing.");
var builtInYouTubeSmartTv = BuiltInServiceCatalog.Find("general/youtube-smart-tv") ?? throw new InvalidOperationException("YouTube Smart TV built-in definition is missing.");
Require(builtInYouTubeDesktop.Url == "https://www.youtube.com/" && builtInYouTubeDesktop.Experience == ServiceExperience.Default, "YouTube Desktop defaults are incorrect.");
Require(builtInYouTubeSmartTv.Url == "https://www.youtube.com/tv" && builtInYouTubeSmartTv.Experience == ServiceExperience.TV, "YouTube Smart TV defaults are incorrect.");
Require(builtInYouTubeDesktop.Id != builtInYouTubeSmartTv.Id, "YouTube built-ins must have separate stable IDs.");

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
Require(migrated.Services[0].PreferredVpnLocation == PreferredVpnLocation.None, "Legacy service unexpectedly gained a preferred VPN location.");

var tv = new ServiceDefinition { Name = "TV", Url = "https://www.youtube.com/tv", Experience = ServiceExperience.TV };
var organizedService = new ServiceDefinition { Name = "Favorite", Url = "https://example.com", IsFavorite = true, IsHiddenFromHome = true, PreferredVpnLocation = PreferredVpnLocation.UnitedKingdom };
var editorCloneMethod = typeof(ServiceEditorWindow).GetMethod("Clone", BindingFlags.NonPublic | BindingFlags.Static);
var editorClone = editorCloneMethod?.Invoke(null, new object[] { organizedService }) as ServiceDefinition;
Require(editorClone is { IsFavorite: true, IsHiddenFromHome: true }, "Edit Service lost favorite or Home visibility state.");
Require(editorClone?.PreferredVpnLocation == PreferredVpnLocation.UnitedKingdom, "Edit Service lost the preferred VPN location.");
Require(PreferredVpnLocationPolicy.Matches(PreferredVpnLocation.None, CurrentLocationState.Direct, null, null), "None should never request a route change.");
Require(PreferredVpnLocationPolicy.Matches(PreferredVpnLocation.Direct, CurrentLocationState.Direct, null, null), "Direct preference did not accept Direct.");
Require(!PreferredVpnLocationPolicy.Matches(PreferredVpnLocation.Direct, CurrentLocationState.Connected, NordLocationCatalog.Get(NordLocationId.Atlanta), null), "Direct preference incorrectly accepted an active VPN location.");
Require(PreferredVpnLocationPolicy.Matches(PreferredVpnLocation.USA, CurrentLocationState.Connected, NordLocationCatalog.Get(NordLocationId.Atlanta), null), "A USA city route did not satisfy a USA preference.");
Require(!PreferredVpnLocationPolicy.Matches(PreferredVpnLocation.Canada, CurrentLocationState.Direct, null, null), "Direct incorrectly satisfied a Canada preference.");
Require(PreferredVpnLocationPolicy.Matches(PreferredVpnLocation.Canada, CurrentLocationState.Connected, null,
    new VpnV2ImportedConfig(Guid.NewGuid(), "Canada", "test", "udp", "test.ovpn")), "Canada route did not satisfy a Canada preference.");
Require(ServiceLaunchProgress.ForRoute(new LocationRouteProgress(LocationRouteProgressStage.Connecting, "Canada")) == "Connecting to Canada…", "Launch progress did not expose the real Canada connection stage.");
Require(ServiceLaunchProgress.ForRoute(new LocationRouteProgress(LocationRouteProgressStage.TryingAnotherConnection, "United Kingdom")) == "Trying another United Kingdom connection…", "Launch progress did not expose a real fallback stage.");
Require(ServiceLaunchProgress.ForExperience(new ServiceDefinition { Experience = ServiceExperience.TV }) == "Applying TV Experience…", "Launch progress did not identify TV Experience.");
Require(ServiceLaunchProgress.ForExperience(new ServiceDefinition { CustomUserAgentOverride = "test" }) == "Applying custom browser identity…", "Launch progress did not identify a custom browser identity.");

Require(SessionPermissionStore.TryCreateKey("https://www.paramountplus.com/shows/example", CoreWebView2PermissionKind.Geolocation, out var paramountLocation), "Permission origin was not recognized.");
Require(SessionPermissionStore.TryCreateKey("https://www.paramountplus.com/account?step=2", CoreWebView2PermissionKind.Geolocation, out var paramountLocationAgain) && paramountLocation == paramountLocationAgain,
    "Same-origin navigation produced a different permission identity.");
Require(SessionPermissionStore.TryCreateKey("https://www.paramountplus.com/", CoreWebView2PermissionKind.Camera, out var paramountCamera) && paramountCamera != paramountLocation,
    "Different permission types were not isolated.");
Require(SessionPermissionStore.TryCreateKey("https://auth.paramountplus.com/", CoreWebView2PermissionKind.Geolocation, out var paramountAuth) && paramountAuth != paramountLocation,
    "Different origins were not isolated.");
var permissionTestRoot = Path.Combine(Path.GetTempPath(), "MonkeyTV-permissions-" + Guid.NewGuid().ToString("N"));
var permissionFile = Path.Combine(permissionTestRoot, "site-permissions.json");
var permissionStore = new SessionPermissionStore(permissionFile);
var permissionPromptCount = 0;
var firstPermission = permissionStore.GetOrPromptAsync(paramountLocation, async () => { permissionPromptCount++; await Task.Delay(20); return CoreWebView2PermissionState.Allow; });
var duplicatePermission = permissionStore.GetOrPromptAsync(paramountLocationAgain, () => { permissionPromptCount++; return Task.FromResult(CoreWebView2PermissionState.Deny); });
Require(await firstPermission == CoreWebView2PermissionState.Allow && await duplicatePermission == CoreWebView2PermissionState.Allow && permissionPromptCount == 1,
    "Simultaneous same-origin permission requests were not coalesced.");
Require(await permissionStore.GetOrPromptAsync(paramountLocation, () => { permissionPromptCount++; return Task.FromResult(CoreWebView2PermissionState.Deny); }) == CoreWebView2PermissionState.Allow && permissionPromptCount == 1,
    "A saved permission decision was not reused during the page session.");
Require(File.Exists(permissionFile), "The portable permission decision file was not created.");

var restartedPermissionStore = new SessionPermissionStore(permissionFile);
Require(await restartedPermissionStore.GetOrPromptAsync(paramountLocationAgain, () => { permissionPromptCount++; return Task.FromResult(CoreWebView2PermissionState.Deny); }) == CoreWebView2PermissionState.Allow && permissionPromptCount == 1,
    "An explicit permission decision did not survive a simulated Monkey TV restart.");
Require(await restartedPermissionStore.GetOrPromptAsync(paramountCamera, () => { permissionPromptCount++; return Task.FromResult(CoreWebView2PermissionState.Deny); }) == CoreWebView2PermissionState.Deny && permissionPromptCount == 2,
    "An unrecorded permission type was automatically granted.");
var restartedAfterDeny = new SessionPermissionStore(permissionFile);
Require(await restartedAfterDeny.GetOrPromptAsync(paramountCamera, () => { permissionPromptCount++; return Task.FromResult(CoreWebView2PermissionState.Allow); }) == CoreWebView2PermissionState.Deny && permissionPromptCount == 2,
    "An explicit Deny decision did not survive a simulated Monkey TV restart.");

restartedAfterDeny.ClearForHosts(["paramountplus.com"]);
var afterServiceReset = new SessionPermissionStore(permissionFile);
Require(await afterServiceReset.GetOrPromptAsync(paramountLocation, () => { permissionPromptCount++; return Task.FromResult(CoreWebView2PermissionState.Deny); }) == CoreWebView2PermissionState.Deny && permissionPromptCount == 3,
    "Reset Service did not clear the site's persistent permission decisions.");
afterServiceReset.ClearAll();
Require(!File.Exists(permissionFile), "Reset All Website Data did not remove the persistent permission store.");
var afterFullReset = new SessionPermissionStore(permissionFile);
Require(await afterFullReset.GetOrPromptAsync(paramountLocation, () => { permissionPromptCount++; return Task.FromResult(CoreWebView2PermissionState.Allow); }) == CoreWebView2PermissionState.Allow && permissionPromptCount == 4,
    "Reset All Website Data did not make the origin ask again.");
afterFullReset.ClearAll();
if (Directory.Exists(permissionTestRoot)) Directory.Delete(permissionTestRoot, true);
Require(BrowserIdentity.ResolveUserAgent(tv) == BrowserIdentity.TvUserAgent, "TV Experience did not resolve the built-in TV UA.");
tv.CustomUserAgentOverride = "Debug-UA";
Require(BrowserIdentity.ResolveUserAgent(tv) == "Debug-UA", "Custom override did not take precedence over TV Experience.");
tv.CustomUserAgentOverride = null;

migrated.Services.Add(tv);
store.Save(migrated);
var reloaded = store.Load();
var restoredTv = reloaded.Services.Single(s => s.Name == "TV");
Require(restoredTv.Experience == ServiceExperience.TV, "TV Experience did not persist across reload.");
Require(restoredTv.PreferredVpnLocation == PreferredVpnLocation.None, "New service did not retain the default None VPN preference.");
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
Require(ExternalNavigationPolicy.IsAmbiguousAuthenticationRedirect(new Uri("https://www.paramountplus.com/"), null, new Uri("https://www.paramountplus.com/account/flow/f-upsell/action/login/")), "Provider login redirect was not classified as ambiguous.");
Require(!ExternalNavigationPolicy.IsAmbiguousAuthenticationRedirect(new Uri("https://www.paramountplus.com/"), null, new Uri("https://unrelated.example.test/")), "Unrelated navigation was incorrectly classified as authentication.");

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
var sensitiveProfile = PortablePaths.ProfileFor(restoredTv.Id);
File.WriteAllText(Path.Combine(sensitiveProfile, "cookie-secret.test"), "must-not-export");
var backupFile = Path.Combine(PortablePaths.CacheDirectory, "smoke.mtvbackup");
BackupService.Export(backupFile, reloaded);
using (var archive = ZipFile.OpenRead(backupFile))
{
    Require(archive.GetEntry("settings.json") is not null, "Backup omitted settings.");
    Require(archive.Entries.All(e => !e.FullName.Contains("Profiles", StringComparison.OrdinalIgnoreCase)), "Backup exported browser-profile data.");
    Require(archive.Entries.All(e => !e.FullName.Contains("permission", StringComparison.OrdinalIgnoreCase)), "Backup exported saved website permissions.");
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

var shutdownPermissionStore = new SessionPermissionStore();
shutdownPermissionStore.ClearAll();
Require(SessionPermissionStore.TryCreateKey("https://shutdown-permission.example/watch", CoreWebView2PermissionKind.Geolocation, out var shutdownPermissionKey),
    "Shutdown permission origin was not recognized.");
var shutdownPermissionPrompts = 0;
Require(await shutdownPermissionStore.GetOrPromptAsync(shutdownPermissionKey, () =>
{
    shutdownPermissionPrompts++;
    return Task.FromResult(CoreWebView2PermissionState.Allow);
}) == CoreWebView2PermissionState.Allow, "Shutdown permission setup failed.");
var shutdownPermissionFile = Path.Combine(PortablePaths.SettingsDirectory, "site-permissions.json");
Require(File.Exists(shutdownPermissionFile), "Persistent permission data was not written before shutdown cleanup.");

var shutdownProfileRoot = Path.Combine(PortablePaths.SharedBrowserUserDataDirectory, "ShutdownCleanupProof");
var shutdownDisposableFolders = new[] { "Cache", "Code Cache", "GPUCache", "DawnWebGPUCache", "BrowserMetrics", "Crashpad", "PKIMetadata" }
    .Select(name => Path.Combine(shutdownProfileRoot, name)).ToArray();
foreach (var folder in shutdownDisposableFolders)
{
    Directory.CreateDirectory(folder);
    File.WriteAllText(Path.Combine(folder, "temporary.test"), "remove");
}
var shutdownPersistentFolders = new[] { "Local Storage", "IndexedDB", Path.Combine("Service Worker", "CacheStorage"), "Network", "MediaFoundationCdm" }
    .Select(name => Path.Combine(shutdownProfileRoot, name)).ToArray();
foreach (var folder in shutdownPersistentFolders)
{
    Directory.CreateDirectory(folder);
    File.WriteAllText(Path.Combine(folder, "persistent.test"), "keep");
}
var monkeyTemporaryFile = Path.Combine(PortablePaths.CacheDirectory, "shutdown-temporary.test");
File.WriteAllText(monkeyTemporaryFile, "remove");

StorageService.ClearShutdownTemporaryData();
Require(shutdownDisposableFolders.All(folder => !Directory.Exists(folder)), "Shutdown cleanup retained disposable browser/runtime artifacts.");
Require(shutdownPersistentFolders.All(folder => File.Exists(Path.Combine(folder, "persistent.test"))), "Shutdown cleanup removed persistent provider/browser state.");
Require(!File.Exists(monkeyTemporaryFile), "Shutdown cleanup retained Monkey TV temporary data.");
Require(File.Exists(shutdownPermissionFile), "Shutdown cleanup deleted persistent website permissions.");
var reopenedPermissionStore = new SessionPermissionStore();
Require(await reopenedPermissionStore.GetOrPromptAsync(shutdownPermissionKey, () =>
{
    shutdownPermissionPrompts++;
    return Task.FromResult(CoreWebView2PermissionState.Deny);
}) == CoreWebView2PermissionState.Allow && shutdownPermissionPrompts == 1,
    "Shutdown cleanup lost the persisted permission decision.");
reopenedPermissionStore.ClearForHosts(["shutdown-permission.example"]);
var afterShutdownServiceReset = new SessionPermissionStore();
Require(await afterShutdownServiceReset.GetOrPromptAsync(shutdownPermissionKey, () =>
{
    shutdownPermissionPrompts++;
    return Task.FromResult(CoreWebView2PermissionState.Deny);
}) == CoreWebView2PermissionState.Deny && shutdownPermissionPrompts == 2,
    "Reset Service did not clear a permission preserved through shutdown.");
afterShutdownServiceReset.ClearAll();

var storageAccountingRoot = Path.Combine(Path.GetTempPath(), "MonkeyTV-storage-accounting-" + Guid.NewGuid().ToString("N"));
var storageAccountingRequirements = Path.Combine(storageAccountingRoot, "Requirements", "WebView2Fixed");
var storageAccountingUserData = Path.Combine(storageAccountingRoot, "Browser", "Shared");
Directory.CreateDirectory(storageAccountingRequirements); Directory.CreateDirectory(storageAccountingUserData);
File.WriteAllBytes(Path.Combine(storageAccountingUserData, "user-data.bin"), new byte[4096]);
var userDataSizeBeforeRequirementsGrowth = StorageService.UserDataSize(storageAccountingRoot);
File.WriteAllBytes(Path.Combine(storageAccountingRequirements, "bundled-runtime.bin"), new byte[2 * 1024 * 1024]);
Require(StorageService.UserDataSize(storageAccountingRoot) == userDataSizeBeforeRequirementsGrowth && userDataSizeBeforeRequirementsGrowth == 4096,
    "Bundled Data\\Requirements files changed the user-facing storage total.");
Directory.Delete(storageAccountingRoot, true);

var queuedSharedProfile = Path.Combine(PortablePaths.SharedBrowserUserDataDirectory, "synthetic-profile");
Directory.CreateDirectory(queuedSharedProfile); File.WriteAllText(Path.Combine(queuedSharedProfile, "state.test"), "remove");
StorageService.QueueSharedProfileDeletion(Path.GetRelativePath(PortablePaths.AppRoot, queuedSharedProfile));
StorageService.CleanupQueuedSharedProfileDeletes();
Require(!Directory.Exists(queuedSharedProfile), "Queued removed shared profile was not cleaned at a safe point.");

for (var i = 0; i < 500; i++) AppLog.Write("smoke", new string('x', 1200));
var retainedLogBytes = Directory.Exists(PortablePaths.LogsDirectory)
    ? Directory.EnumerateFiles(PortablePaths.LogsDirectory, "monkey.log*").Sum(f => new FileInfo(f).Length) : 0;
Require(retainedLogBytes <= AppLog.MaximumRetainedBytes + 4096, "Log retention exceeded its bound.");

Console.WriteLine("PASS: migration, Experience, appearance, TV UA, banner fit/dedup, backup privacy/import, cache classification/cleanup, recovery marker, and bounded logs.");
