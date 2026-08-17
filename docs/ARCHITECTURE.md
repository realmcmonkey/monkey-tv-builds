# Architecture

Monkey TV v0.0.2-alpha uses .NET 8 WPF for the shell and Microsoft Edge WebView2 for websites. This gives a native Windows desktop lifecycle, a modern custom shell, the browser engine already supported on Windows 11, persistent cookies, and access to the runtime's legitimate media capabilities.

## Main components

- `App`: reads assembly informational version, creates portable paths, sequences warning → setup → Home.
- `SettingsStore`: atomic JSON persistence at `Data/Settings/settings.json`; malformed input has one bounded replacement backup.
- `ServiceDefinition`: provider-neutral ID, name, URL, icon, optional banner artwork, per-service Experience, optional advanced custom-UA override, profile configuration, legacy migration fields, and future settings.
- `IconService`: only runs while the user adds a service. It inspects icon link metadata and falls back to `/favicon.ico`; failures are non-fatal.
- `ArtworkService`: only runs while a service is saved without banner artwork. It inspects social-image metadata, accepts only reasonably sized landscape images, and caches the result below portable `Data`; failures are non-fatal.
- `PortableAssetImporter`: imports banners as content-hashed JPEGs, limits them to 1600×900, uses quality 85, and deduplicates identical artwork.
- `MainWindow`: renders a lightweight responsive Banner/Square Home grid, coordinates app/document fullscreen as separate states, and creates one WebView2 only after a service-card click.
- `ServiceControlsWindow`: a small owned, transparent flyout above the HWND-hosted WebView. It is the only overlay hit-test surface, so the rest of the website remains unobstructed.
- `SettingsWindow` / `ServiceEditorWindow`: CRUD, ordering, appearance, backup/restore, on-demand storage, About/diagnostics, banner controls, Experience selection, and advanced custom-UA/site-data controls.
- `BackupService`: validates and atomically imports a compact ZIP-based `.mtvbackup`; only configuration and referenced portable artwork are included.
- `StorageService`: measures categories on demand, removes verified temporary cache only after WebView2 is closed, enforces a 192 MiB temporary-data soft budget, prunes orphaned app-owned artwork, and safely queues deleted shared-profile folders until no UDF is active.
- `AppLog` / `SessionMarker`: bounded diagnostic logging and a tiny unclean-session marker. No telemetry or remote reporting exists.
- `IUpdateProvider` / `UpdateService`: provider-neutral update metadata and prerelease-version comparison foundation. No server is contacted and no updater runs in this alpha.

## Browser lifecycle

```text
Initial Home (no WebView)
  -> click service A
  -> create WebView2 + Data/Browser/Profiles/<service-id>
  -> apply profile, Experience/custom UA, and browser settings
  -> explicitly navigate to service A's configured URL
  -> Home: try to suspend and hide the sole WebView
     -> service A again: resume the same WebView without navigation
     -> service B: remove and dispose A, create B, apply B settings, navigate to B
```

Only one service WebView may exist. Generation guards prevent late events from a disposed browser from changing the current UI, and teardown steps are isolated so one cleanup failure cannot leave an orphaned browser in the visual tree. Profiles are intentionally retained so legitimate cookies and login sessions can survive reopening. Removing a service does not delete its profile in this alpha, preventing accidental session-data loss.

`BrowserIdentity` centralizes the built-in TV identity. A non-empty custom override has highest precedence; otherwise TV Experience uses the built-in identity and Default Experience leaves WebView2's normal identity unchanged. The identity is assigned after WebView initialization but before the first navigation.

## Browser controls and DRM choice

Standard WPF WebView2 is an HWND and visually covers same-window WPF overlays. WebView2's composition alternative solves layering but Microsoft's documentation states DRM-protected content can fail because that control uses a graphics-capture session. Playback compatibility is more important, so this alpha uses standard WebView2 plus a tiny owned flyout window for Back, Reload, Home, and application fullscreen. Its footprint is limited to the visible controls; there is no invisible surface intercepting website input.

The flyout appears briefly at service launch, hides after three seconds of inactivity, and reappears when the pointer reaches the top 24 physical pixels of the client area. A 125 ms lightweight timer only reads cursor position; it does not poll page state. Cursor hiding is likewise based on service-view inactivity, not claims about media playback.

Application fullscreen and WebView2 document fullscreen are tracked independently. F11 toggles only Monkey TV fullscreen. WebView2's supported `ContainsFullScreenElementChanged` event controls the document-fullscreen layer and suppresses Monkey TV overlays. This separation lets the first Escape leave website fullscreen while a second Escape can leave Monkey TV fullscreen. Physical monitor bounds come from the monitor containing the window, and the prior normal/maximized state and normal bounds are restored afterward.

No DRM code exists in Monkey TV. Authentication, authorization, playback, subtitles, and protected media remain entirely provider/WebView2 responsibilities.

## Portable storage

All explicit writable paths are based on `AppContext.BaseDirectory`:

```text
Data/
  Assets/
    Banners/
  Browser/
    Shared/                         # one UDF for new named service profiles
    Profiles/<service-id>/           # preserved legacy isolated UDFs
  Cache/
  Logs/
  Settings/settings.json
```

WebView2 profile files may contain machine-bound tokens or DRM state. Copying the folder can preserve ordinary configuration, but providers may require login again on another PC.

Configuration backup deliberately excludes `Browser`, `Cache`, `Logs`, cookies, credentials, DRM state, IndexedDB, and service-worker data. Imported artwork must resolve below portable `Data/Assets`. Logs rotate at 128 KiB with two retained backups (384 KiB maximum).

New services use WebView2 named profiles under one shared UDF. This preserves profile-local cookies/site data while sharing WebView2 runtime data such as compiled shaders and security/component resources; it avoids the repeated UDF-level overhead of the original one-folder-per-service layout. Existing service UDFs are deliberately not copied into the shared layout because manual profile-file migration can invalidate protected/session data. They remain usable until explicitly cleared.

Automatic cleanup never targets cookies, Network state, Local Storage, IndexedDB, Cache Storage, service workers, or Widevine/provider state. It clears disk/code/GPU/shader/media cache, browser metrics, crash artifacts, and app temporary files after the browser is disposed. Replaceable runtime component caches are retained for normal performance but are cleared when aggregate temporary browser data exceeds 192 MiB; explicit **Clear Temporary Data** also clears them. The supported `CoreWebView2Profile.ClearBrowsingDataAsync(DiskCache)` API is used for an active profile. No undocumented Chromium cache-size switch is used.

Every process launch follows warning (when applicable) → setup (when required) → Home. Crash recovery never restores the prior service.
