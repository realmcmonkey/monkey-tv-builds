# AGENTS.md

## Purpose and precedence

These instructions describe the current Monkey TV product and architecture. Prefer durable product, privacy, security, lifecycle, and release invariants over historical build notes. Historical implementations and completed feasibility work belong in documentation and `CHANGELOG.md`, not here.

When changing behavior, inspect the current implementation first. Preserve working systems unless the user explicitly authorizes changing them. Keep changes scoped, backward-compatible, and testable.

## Product boundary

- Monkey TV is a personal Windows streaming-site launcher. It is not a content host, recorder, capture tool, DRM implementation, or provider-protection workaround.
- Do not add recording, stream ripping, capture, decryption, DRM bypass, provider-protection bypass, ad injection, advertising, telemetry, analytics, or service-usage tracking.
- Use WebView2's legitimate provider-controlled playback and DRM capabilities. Never claim provider, account, catalogue, protected-playback, or DRM compatibility without hands-on testing.
- Normal user-initiated website downloads may use WebView2's download flow, but every download requires an explicit native Windows Save destination outside Monkey TV's portable `Data`. Do not add a streaming-content downloader.
- Keep services generic and data-driven. Put exceptional provider behavior in explicit optional service settings rather than scattered provider-specific conditionals.

## Supported platform and application shell

- Target Windows 11 x64 with mouse and keyboard.
- Stack: .NET 8 WPF and standard Microsoft Edge WebView2. Do not replace it with `WebView2CompositionControl`; protected playback can fail through composition/screen-capture rendering.
- Release target framework is `net8.0-windows10.0.17763.0`, runtime `win-x64`.
- The main window owns the integrated top-level **Home**, **Services**, and **Settings** views. Normal navigation between them must not create extra taskbar windows, recreate the browser, or change the active VPN route.
- **Edit Service** and **VPN Location** are integrated right-side drawers. Opening or closing either drawer must preserve the originating top-level view. Respect Reduced Motion when animating drawers or other UI.
- Keep application fullscreen, website fullscreen, Escape/F11 behavior, the persistent top navigation, and the visible application-fullscreen exit control working together.
- Use native Windows file and folder pickers where the user chooses an external path.

## Startup and browser lifecycle

- Home is the default startup view. Startup and Home must remain provider-free: do not preload, ping, navigate, or create a service WebView merely to render Home.
- Show the lightweight branded splash promptly for at least three seconds and longer when startup is not ready. The splash itself must not initialize WebView2 or contact a provider.
- An explicitly saved **Last service** startup preference may launch that service only after the shell is ready. Never infer or silently create a startup service.
- VPN Location always starts **Direct** and never reconnects a prior location automatically, including when Last service is enabled.
- Create one service WebView2 after the first actual service launch, then reuse that exact WebView and the shared profile at `Data/Browser/Shared` for all services.
- Home, Services, and Settings hide the active browser without destroying its session. Home must mute and hide it immediately, render without awaiting browser work, and attempt suspension only after the controller is invisible.
- On Home, pause standard finite-duration HTML media and resume only media Monkey TV paused when the same service returns. If standard APIs clearly identify a live stream, keep it running but muted. For inaccessible/custom players, retain safe mute/hide and best-effort suspension; never claim perfect live-stream detection.
- Reopening the same service resumes the existing page when no forced reload is pending. Opening a different service reuses the same WebView, applies that service's browser identity, and navigates while hidden/muted until ready.
- Never create one persistent browser per service. Permit at most one temporary same-profile child WebView for a user-initiated related/auth popup. Route clearly unrelated user-initiated popup links to the Windows default browser.
- User-agent/experience settings are per service. Never globally spoof a provider user agent. Keep browser zoom at its normal default and do not add per-service zoom.

## Services and organization

- The top-level Services view is the complete searchable service library and management surface. Home is the artwork-first launcher.
- Preserve configured-service identity, order, sorting choices, Favorites, hidden-from-Home state, artwork, banner fit, Default/TV Experience, advanced custom user agent, and custom-service data.
- Favoriting a service does not remove it from the configured library. Hidden services remain configured and manageable in Services.
- Edit Service uses the integrated drawer and supports unsaved preview/testing without silently persisting draft values. Save must preserve unrelated organization fields; Cancel must discard the draft.
- `PreferredVpnLocation` is optional per-service metadata with values None, Direct, USA, Canada, or United Kingdom. Missing/legacy values deserialize as None. Never guess a service's preferred country.
- A preferred location is a launch-time suggestion only. Editing or saving it performs no connection, download, preparation, verification, containment, or other live network work.
- On actual service launch, if the preference does not match the active verified route, offer **Switch & Open**, **Open Anyway**, and **Cancel**. Switch & Open must use the existing verified VPN routing flow and must not navigate the service unless the requested route becomes ready. Failure leaves the service unopened and never silently falls back.
- Home, Services, Settings, and editing a service must not trigger preferred-location checks; only an actual service launch does.

## VPN Location

- Present one user-facing **VPN Location** feature through the persistent top-bar drawer: Direct, USA, Canada, and United Kingdom. Keep legacy backend names and technical routing terminology out of normal UI.
- Preserve the current unified routing implementation:
  - Direct uses the normal connection through Monkey TV's loopback gateway.
  - USA retains the documented Nord SOCKS5 automatic/city choices.
  - Canada and United Kingdom use official downloaded Nord OpenVPN configurations through the existing OpenVPN 3 Core/lwIP user-space helper.
  - Canada/UK containment requires Monkey TV's complete app-owned Fixed Version WebView2 runtime and the existing scoped user-mode WFP broker.
- Use only official/supplied Nord configurations and documented endpoints. Do not fabricate locations, infer cities from numbered hosts, use private APIs, reverse-engineer Nord software, or weaken TLS/certificate verification.
- Nord manual/service credentials are shared by the routing backends and protected with Windows DPAPI. Never log, export, place on command lines, or persist them as plaintext.
- Route changes must be serialized and fail closed. Block/hide active service traffic before connecting, switching, disconnecting, or retrying; verify the intended egress before reporting Connected/Direct or navigating/reloading a service.
- If a route, helper, containment policy, or verification fails or disappears, keep the gateway denied and report failure. Never silently fall back to Direct or an unintended country.
- Preserve automatic bounded candidate fallback and clean each failed attempt before trying another candidate.
- VPN Location affects only Monkey TV browser traffic. Do not create a system-wide VPN route, system VPN profile, TAP/Wintun/DCO adapter, custom kernel driver/callout, permanent service, or persistent organization-management policy. Unrelated applications and unrelated WebView2 runtimes must remain unaffected.
- Preserve the scoped WFP application identity and Fixed Runtime boundary for Canada/UK. Do not substitute shared Evergreen WebView2 for a route that requires proven per-application containment.
- Returning Home or navigating within the shell preserves an active verified route. Application shutdown must tear down helper processes, gateway state, and scoped containment cleanly.

## Website permissions and browser safety

- Camera, microphone, geolocation, and clipboard-read permissions behave like browser permissions keyed by exact normalized origin and permission type.
- The first supported request from an origin asks the user. Persist an explicit Allow or Deny decision across restarts and reuse it automatically for that exact origin/type. Never grant an unrecorded permission automatically.
- Coalesce duplicate simultaneous requests so one origin cannot create multiple identical prompts. Related same-profile popup WebViews reuse the same applicable decision.
- Store permission decisions only as bounded portable site-permission data under Monkey TV's active `Data` root. Do not include them in configuration exports.
- Reset Service or clearing applicable site data clears permission decisions for that service's known origins. Reset All Website Data clears every saved website permission decision.
- Notifications and unsupported privileged permissions remain denied unless a future explicitly approved feature provides a conservative implementation.
- Popup handling, permissions, and downloads must remain conservative. Do not inject Monkey TV themes or unrelated scripts into provider pages.

## Portable data, storage, and privacy

- Monkey TV's default writable root is `Data` beside the executable. A user may explicitly relocate ordinary user data through Settings; do not silently write app data to AppData, LocalAppData, Documents, the registry, or machine-specific source paths.
- Framework/OS-managed single-file extraction and Windows security facilities are not Monkey TV user-data locations.
- `Data/Requirements` beside the executable is application/runtime infrastructure, not ordinary user data. Preserve it, never relocate it with user data, exclude it recursively from normal user-facing storage totals, and never delete or modify it during cleanup/reset.
- Keep the app-owned Fixed WebView2 runtime as a complete matched set. Do not prune individual files from `Runtime/WebView2Fixed` or `Data/Requirements/WebView2Fixed` to reduce size.
- Keep persistent Data size-conscious and bounded. Logs, diagnostics, recovery files, metadata, caches, temporary downloads, and migration queues must not grow without a limit. Never log passwords, usernames, tokens, public IP addresses, private certificate material, credential-bearing configuration, or browser-profile secrets.
- Preserve cookies, Network state, Local Storage, IndexedDB, Cache Storage, service workers, legitimate provider/DRM state, and user preferences by default. Automatic cleanup may target only verified disposable browser/cache/runtime artifacts.
- The shared browser temporary-data soft target is 100 MiB. Use supported WebView2 disk-cache cleanup; do not use undocumented production Chromium cache flags.
- Configuration export/import remains bounded and path-safe. Exports exclude browser profiles, cookies, credentials, permissions, authentication/DRM state, caches, logs, and runtime Requirements by default.
- Preserve `FutureSettings`, legacy fields needed for safe migration, stable service IDs, and unknown forward-compatible configuration where applicable. Never copy/merge cookies, credentials, or DRM state between legacy profiles during migration.
- Removed legacy shared-profile data may be queued only in a small bounded file and deleted at a safe point when no active WebView uses that UDF.

## Reset behavior

- Reuse one underlying service-data reset implementation.
- Browser controls → Reset Service resets the active service and immediately reloads/reopens it.
- Edit Service → Reset Service resets the selected service while leaving the drawer and originating view in place. Mark only that service for a fresh reload the next time it becomes active.
- Resetting one service must not reload or disturb another service. Do not create WebView2 from Home merely to perform a reset; queue the safe work for the selected service's next activation when necessary.
- Reset All Website Data clears the shared browser profile and all saved website permissions, without deleting configured services, application settings, artwork, VPN credentials/configuration, or `Data/Requirements`.
- Reset Monkey TV removes ordinary Monkey TV user data but preserves `Data/Requirements`.

## Appearance and accessibility

- Support System, Light, and Dark themes and the current accent system across Monkey TV UI only. Never inject application appearance into provider pages.
- Maintain readable contrast, keyboard focus, mouse/keyboard operation, and consistent control states across Home, Services, Settings, drawers, dialogs, menus, and first-run/recovery UI.
- Animations must be subtle, fast, and lightweight. Respect the effective Reduced Motion preference: replace movement/scale with instant changes or very short fades without changing behavior.
- Persist only validated appearance and normal/maximized placement state. Do not persist transient drawer, fullscreen, loading, or VPN-connection state.

## Build, testing, and handoff

- Release builds are self-contained, single-file, x64. VPN-enabled test/final layouts also require the checked-in matched runtime dependencies copied beneath `Data/Requirements`.
- Before handing off a test build: build Release with zero errors, run the smoke tests and directly relevant regression tests, publish to a temporary directory, replace the intended test executable, and remove generated `bin`, `obj`, and temporary publish output.
- For meaningful browser/UI changes, test clean setup, startup, Home, Services, Settings, resizing, keyboard focus, a benign service, shared-profile resume/switching, popup/download/permission paths, F11/Escape, shutdown/restart, and portable paths as applicable.
- For VPN changes, test Direct and affected countries, route-before-navigation, verification, switching, route loss/fail-closed behavior, unrelated-process isolation, and cleanup. Do not claim a provider/DRM/location flow works without manual account testing.
- Keep `CHANGELOG.md`, current architecture/build documentation, known limitations, and third-party notices accurate for every handed-off build.
- Local test builds belong in `builds/Test Build`. Keep only deliberate milestone/final artifacts under top-level `builds`; do not accumulate intermediate outputs.
- Never place existing user Data, credentials, browser profiles, cookies, logs, downloaded private state, or test artifacts in a distributable. A clean distributable may contain an empty writable Data layout plus required runtime infrastructure beneath `Data/Requirements`.

## Publishing authorization

- Local editing, building, debugging, smoke testing, and updating the specifically requested local test executable are allowed during development.
- Committing, pushing, tagging, packaging a distributable, uploading, publishing, or creating/updating a release requires explicit user authorization for that exact current development state.
- Do not automatically release after development work. Never publish Monkey TV publicly.
- When a release is explicitly authorized, use the private GitHub repository `realmcmonkey/monkey-tv-builds`, create the requested private prerelease/tag, attach the final single-file `MonkeyTV.exe` directly, and optionally attach a clean portable ZIP. Return the authenticated asset URL and keep release notes concise with a link to the detailed changelog.
