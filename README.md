# Monkey TV

Monkey TV is a personal Windows streaming-service launcher. It brings your configured streaming websites together in one desktop application while keeping provider accounts, subscriptions, playback, DRM, and availability under each provider’s control.

Monkey TV does **not** host, record, capture, decrypt, or bypass protected streaming content or provider protections.

## Run the current local test build

1. Open [`builds/Test Build`](builds/Test%20Build).
2. Run `MonkeyTV.exe`.
3. Keep `MonkeyTV.exe` and its adjacent `Data` folder together.

`Data` contains Monkey TV’s portable settings, artwork, browser profile, logs, and downloaded location setup. Its `Data\\Requirements` subfolder contains required bundled runtime components for this test build.

Do not delete or move `Data\\Requirements` by itself. If you want to reset browser sign-ins or Monkey TV data, use the in-app reset controls rather than deleting the whole folder.

Do not copy another person’s `Data` folder into your build: it can contain Windows-protected credentials, browser sign-ins, and provider state.

## What Monkey TV includes

- Integrated **Home**, **Services**, and **Settings** navigation.
- An artwork-first service launcher with Favorites and a personal service library.
- Service editing with artwork preview, Fit/Fill presentation, browser-experience settings, and safe test actions.
- One shared browser profile that preserves normal provider sign-ins and website state.
- Per-service Default Experience, TV Experience, or advanced custom browser identity settings.
- Optional Preferred VPN Location suggestions for individual services.
- **VPN Location** choices for Direct, USA, Canada, and United Kingdom.
- Real service-launch status using actual route and browser lifecycle progress.
- Persistent browser-style camera, microphone, location, and clipboard permission decisions.
- System, Light, and Dark themes, accessibility-focused keyboard behavior, and Reduced Motion support.
- Portable Monkey TV data beside the application.

## Important behavior

- New installations start at Home. General settings can optionally reopen the last service after Home is ready.
- VPN Location always starts **Direct**. Monkey TV never automatically reconnects a previous location at startup.
- Home does not initialize a browser or contact a streaming provider until you select a service.
- Monkey TV uses one shared browser/profile for configured services, so normal provider sign-ins and website state can persist.
- Reset Service and Reset All Website Data can clear website state and remembered permission decisions when needed.
- Configuration exports intentionally exclude browser profiles, cookies, sign-ins, credentials, caches, logs, and protected provider state.
- Streaming-provider compatibility, protected playback, and regional availability must be tested manually with your own accounts. Monkey TV does not bypass provider decisions or protections.

## Documents

| Document | Purpose |
| --- | --- |
| [Build guide](docs/BUILD.md) | Build or refresh a local test copy. |
| [Architecture](docs/ARCHITECTURE.md) | Core application lifecycle and boundaries. |
| [VPN Location](docs/VPN_LOCATION.md) | Current routing design and safety boundaries. |
| [Native helper source](native/MonkeyTV.UserSpaceOpenVpnHelper/README.md) | Maintained source, pins, and rebuild provenance for the shipped user-space VPN helper. |
| [VPN proof archive](research/VPN_PROOF_ARCHIVE.md) | Historical proof context; not a current build or runtime dependency. |
| [Known limitations](docs/KNOWN_LIMITATIONS.md) | Important manual-testing and provider limitations. |
| [Alpha testing checklist](docs/ALPHA_TESTING.md) | Hands-on validation before a release. |
| [Changelog](CHANGELOG.md) | Detailed development history. |
| [Third-party notices](THIRD-PARTY-NOTICES.md) | Dependency and license information. |

## Development quick start

Requirements: Windows 11 x64, Git, the .NET 8 SDK, and NuGet access. The current VPN Location test build also needs the runtime dependencies under `Runtime`; the ignored Fixed WebView2 runtime must be copied or installed separately on a new development PC.

```powershell
dotnet restore .\MonkeyTV\MonkeyTV.csproj
dotnet build .\MonkeyTV\MonkeyTV.csproj -c Release --no-restore
```

See the [Build guide](docs/BUILD.md) for the publish command, cleanup rules, native-helper prerequisites, and the main-PC transfer checklist.
