# Monkey TV

Monkey TV is a portable Windows streaming-service launcher. It opens one configured website at a time in one reusable Microsoft Edge WebView2. Home immediately mutes and hides the active service, then suspends it so the same service can resume where it was left; choosing a different service hides that WebView behind a launch transition and navigates the same instance to the new destination. It does not host, download, proxy, capture, decrypt, or modify streaming content.

Current version: **v0.0.2-alpha — Build #2**

## Try the packaged alpha

Extract the release ZIP to a writable folder on a Windows 11 x64 PC, then run `MonkeyTV.exe`. Keep the EXE and `Data` folder together. The app creates its portable configuration, icons, cache, and one shared browser profile inside `Data`.

The Microsoft Edge WebView2 Evergreen Runtime must be installed. It is normally present on Windows 11. Streaming-service login and DRM/playback must be tested by the user with their own accounts; no provider has been certified by this project.

## What works in this alpha

- Version-aware alpha warning and per-version dismissal
- Three-screen first-time setup
- Generic services using name + URL
- Best-effort website icon discovery and manual icon replacement
- Configurable tile size, names, and clock
- Responsive Banner/Square Home cards with Small/Medium/Large sizing
- Subtle expandable configured-service name search
- Cached landscape artwork discovery and portable manual banner overrides
- Add, edit, remove, and reorder services
- Settings and modern per-card Open/Edit/Move/Banner/Cleanup/Remove actions
- Per-service Default Experience or TV Experience, with an optional Advanced custom-UA override
- Per-service banner Fill/Fit, optimized portable banner imports, and safe site-data clearing
- On-demand WebView2 creation; no service preloading
- One reusable WebView2 and one shared portable browser profile for every configured service
- Same-service Home resume; different services reuse the same WebView and navigate it to the new destination
- Local-artwork launch transition hides the previous website until a different service finishes its initial navigation
- Friendly navigation/process-failure screens with Retry and Home
- Portable configuration export/import that excludes logins and browser profiles
- On-demand Settings storage reporting, bounded logs, unclean-shutdown recovery, and a diagnostic About page
- Storage management with safe temporary-data cleanup, a low-storage-oriented 100 MiB soft cache target, destructive browser-data reset, and full Monkey TV reset
- F11, Alt+Left, Ctrl+R, Ctrl+H, and Ctrl+, shortcuts
- Auto-hiding Back, Reload, Home, and fullscreen flyout with top-edge reveal
- True borderless F11 fullscreen with normal/maximized state restoration and coordinated website fullscreen Escape behavior
- Self-contained x64 single-file publishing

Development, architecture, limitations, and test details are in [`docs`](docs/ARCHITECTURE.md).

## Development quick start

Requirements: Windows 11 x64, .NET 8 SDK or newer, internet access for NuGet restore, and the WebView2 Evergreen Runtime.

```powershell
dotnet restore .\MonkeyTV\MonkeyTV.csproj
dotnet build .\MonkeyTV\MonkeyTV.csproj -c Release
dotnet run --project .\MonkeyTV\MonkeyTV.csproj -c Release
```

See [Build and packaging](docs/BUILD.md) for the reproducible publish command.

Monkey TV always launches to Home. It never automatically reopens the service from a prior session, including after crash recovery.
