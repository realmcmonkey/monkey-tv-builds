# AGENTS.md

## Project invariant

Monkey TV is a legitimate streaming-site launcher, never a content host or DRM workaround. Do not add downloading, recording, capture, decryption, proxying, ad injection, DRM bypass, or provider-protection bypass behavior.

## Product rules

- Target Windows 11 x64 with mouse and keyboard.
- Keep all app-controlled writable data below `Data` next to the executable. Do not intentionally use AppData, LocalAppData, Documents, the registry, or absolute machine-specific paths.
- Never preload, ping, or create browsers for configured services while on Home.
- Startup must show the lightweight branded splash promptly for at least three seconds and longer when Home is not ready. It must not initialize WebView2, navigate a service, or contact a provider.
- Create one WebView2 only after the first service click, then reuse that exact WebView for every service. Home must immediately mute and hide it, render Home without awaiting browser work, and call `TrySuspendAsync` only after the controller is invisible. Selecting the same service resumes and unmutes its page. Selecting a different service resumes the same WebView, applies the destination configuration, and navigates it away while muted until the new destination is ready. Never allow more than one service WebView.
- On Home, pause standard finite-duration HTML media and resume only media Monkey TV paused when the same service returns. If standard media APIs clearly identify a live stream, keep it running but muted and do not suspend it. For inaccessible/custom provider players, preserve the safe mute/hide and best-effort WebView2 suspension fallback; never claim perfect live-stream detection.
- Keep services generic and data-driven. Provider-specific behavior belongs in optional service settings, not scattered conditionals.
- User-agent mode is per service. Never globally spoof it.
- Keep System/Light/Dark and accent styling within Monkey TV UI; never inject themes into provider pages. Persist only tiny appearance and validated normal/maximized window-placement fields, never F11 state or a prior service.
- Permit at most one temporary same-profile child WebView for a user-initiated related/auth popup. Never add tabs or uncontrolled popup windows. Route clearly unrelated user-initiated popup links to the Windows default browser.
- Web permissions must be conservative and session-only: never auto-enable notifications or persist a Monkey TV permission database. Website downloads require an explicit Windows Save path outside portable `Data`. Keep browser zoom at its normal default and do not add a zoom setting.
- Use the WebView2 Runtime's legitimate media/DRM support. Never implement DRM.
- Read the displayed version from assembly metadata. Alpha-warning dismissal is keyed to that exact version.
- Monkey TV always starts at Home. Do not add Resume Last Service, a startup service, or automatic service reopening unless explicitly requested in the future.
- Do not add per-service zoom-level or mute-on-launch features unless explicitly requested in the future.
- Per-service storage information belongs only in Settings, never on Home cards or normal browsing UI.
- Configuration exports exclude WebView profiles, cookies, credentials, authentication/DRM state, caches, and logs by default.
- Keep persistent `Data` aggressively size-conscious. New logs, caches, metadata, backups, and migration artifacts must be small and bounded; do not introduce unbounded storage growth.
- Storage rule: preserve Cookies, Network state, Local Storage, IndexedDB, Cache Storage, service workers, legitimate DRM/provider state, and user preferences by default. Automatic cleanup may only target verified disposable disk cache, code/GPU/shader cache, runtime metrics, crash artifacts, and replaceable component resources when over budget.
- WebView2 storage rule: all new launches use the one default profile beneath `Data/Browser/Shared`. Build #1 named and isolated profile folders are retained without copying/merging cookies, credentials, or DRM state. Delete retained migration folders only when the user removes the attributable service or explicitly clears all browser data.
- The temporary browser-data soft target is 100 MiB. Check it every five minutes while a service is active and use WebView2's supported disk-cache cleanup when exceeded. Home only hides, mutes, and suspends the current service so same-service resume changes nothing. Selecting a different service clears supported disposable disk cache before navigating the shared WebView. Clear ordinary disposable data after browser disposal on startup/shutdown. This is a soft working target, not a Chromium quota. Do not use production WebView2 browser flags or undocumented Chromium cache command-line switches.
- Feature #0.01 exception: Monkey TV may use one fixed, loopback-only WebView2 proxy configuration as a browser-only Current Location boundary. The local gateway may route Direct or through NordVPN's officially documented SOCKS5 endpoints using DPAPI-protected service credentials. This is not permission for arbitrary browser flags or proxying elsewhere, a system-wide VPN, per-service location routing, undocumented Nord APIs, or controlling Nord's extension. Disable QUIC and apply best-effort non-proxied WebRTC UDP restriction only for this feature; document that browser-proxy routing is not an absolute WebRTC/UDP leak-proof boundary. Startup always begins Direct with no Nord/provider traffic; the last Nord location is an inactive flyout convenience only.
- Removed shared-profile data is queued in a tiny settings file and deleted only at a safe point with no active WebView2 UDF. Keep this queue bounded and remove it when empty.
- Do not add telemetry, analytics, advertising, or service-usage reporting.
- Only expose settings that function.
- Preserve the portable source tree: no machine-specific absolute paths.

## Architecture constraints

- Stack: .NET 8 WPF + Microsoft Edge WebView2.
- Use standard WPF `WebView2`, not `WebView2CompositionControl`: Microsoft's composition control uses screen capture and DRM-protected content can fail. Service controls use a small owned auto-hiding flyout above the HWND surface; never add a large invisible input layer over the website.
- Release target framework is `net8.0-windows10.0.17763.0`, runtime `win-x64`.
- Publish self-contained and single-file. The WebView2 Evergreen Runtime remains an external Windows prerequisite.
- Configuration schema must remain forward-compatible; preserve `FutureSettings` and profile configuration fields.
- Use one shared WebView2 profile; never invent per-service storage values when the profile is shared.

## Before handing off a build

1. Build Release with zero errors.
2. Publish self-contained single-file x64.
3. Test warning, clean setup, Home, search, Settings, a benign website, Back/Reload/Home, WebView disposal, restart, F11/Escape, and portable paths.
4. Package a fresh empty `Data` directory; never ship test profiles, credentials, cookies, or settings.
5. Update docs and known limitations. Do not claim a provider or DRM flow works without manual account testing.

## Publishing authorization

- Building, running, debugging, and testing locally is allowed during development.
- Publishing, pushing, uploading, packaging a distributable, or creating/updating a release requires explicit user instruction for that specific development state.
- Do not automatically publish after every development prompt. Multiple development prompts may be completed before the user requests a new test or release build.

## Final build location

- Put user-facing final build artifacts in the repository's top-level `builds/` folder.
- Keep only milestone/final distributables there, not intermediate build or publish output.
- Hand off every final distributable as a private, one-click downloadable file link in the user's chat/session.
- Do not publish Monkey TV builds publicly. Use a private host only if a future environment requires hosting.
- Use the final artifact in `builds/` as the source for the private download link.
- The chosen private host is the GitHub repository `realmcmonkey/monkey-tv-builds`. For every explicitly authorized Monkey TV release push, create one private GitHub prerelease whose tag contains the alpha version and build number (for example, `v0.0.2-alpha-build1`).
- Attach the final single-file `MonkeyTV.exe` directly to that GitHub Release so the user can download it from GitHub. A clean portable ZIP may be attached as an additional convenience artifact, but it must not replace the EXE attachment.
- Return the authenticated HTTPS asset-download URL as the primary link. The user must be signed in to the authorized GitHub account.
- Never upload intermediate/debug builds, test profiles, cookies, credentials, or non-final artifacts.
