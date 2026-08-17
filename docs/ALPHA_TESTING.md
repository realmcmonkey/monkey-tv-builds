# Alpha testing checklist

Test with a copy of the package and non-critical accounts. Back up `Data` before testing upgrades.

## Shell and setup

- [ ] Fresh launch shows `Monkey TV v0.0.2-alpha` before setup.
- [ ] Continue works; per-version suppression works after restart.
- [ ] Setup adds one service, Add More works repeatedly, and Done advances.
- [ ] Tile size, names, and clock settings appear correctly.
- [ ] Setup does not return after restart.
- [ ] Every configured launch and every crash-recovery launch returns to Home; no service is reopened automatically.
- [ ] F11 enters true borderless app fullscreen with no title/header strip; Escape exits it; the prior normal/maximized bounds restore.
- [ ] F11 works from Home, Settings, and a focused service without triggering a website command.

## Services and portability

- [ ] Automatic icon succeeds where the site exposes one; manual replacement works.
- [ ] Home search only filters configured service names.
- [ ] Add, edit, remove, reorder, tile size, names, and clock work in Settings.
- [ ] Home cards open normally; service ordering is changed only through Settings or the card context menu and persists after restart.
- [ ] Right-click Open, Edit, Move, Change Banner, Clear Temporary Data, and confirmed Remove work.
- [ ] Banner and Square card modes render correctly at Small, Medium, and Large sizes and wrap as the window width changes.
- [ ] Show service names only controls the separate label; generated fallback branding remains usable when labels are hidden.
- [ ] Hover slightly elevates/scales a card without causing a large layout jump.
- [ ] Search remains collapsed until requested and filters only configured service names.
- [ ] A manually chosen banner is copied below `Data/Assets/Banners` and still works after moving the Monkey TV folder.
- [ ] Banner Reset and Fill/Fit work; oversized imports are reduced to at most 1600×900 and repeated identical imports do not create duplicates.
- [ ] Automatic landscape artwork is cached when suitable metadata exists; square favicons are not stretched across banners.
- [ ] Default Experience uses WebView2's normal identity.
- [ ] TV Experience persists after restart, is restored while editing, and applies the built-in TV identity before first navigation.
- [ ] The inline TV compatibility warning appears; an Advanced custom-UA override takes precedence over the selected Experience.
- [ ] Configuration and profiles exist only below the package `Data` folder where supported.
- [ ] Copying the whole Monkey TV folder to another Windows 11 x64 PC preserves configuration.

## Browser lifecycle

- [ ] No `msedgewebview2` service page is created while sitting on Home.
- [ ] Clicking a service creates and navigates the WebView.
- [ ] Site interaction, links, Back, and Reload work.
- [ ] Service controls appear briefly, hide after inactivity, and reliably reveal at the top edge without blocking normal page clicks.
- [ ] Cursor hides after service-view inactivity, reappears on movement, and never remains hidden on Home, Settings, or dialogs.
- [ ] Home from a fullscreen service leaves Monkey TV fullscreen; opening another service from fullscreen Home also stays fullscreen.
- [ ] Service A -> Home -> service A resumes the same WebView and page state without navigating again.
- [ ] Clicking the flyout Home button and pressing Ctrl+H both render Home immediately, silence audio immediately, and suspend the hidden WebView without an invalid-visible-controller error.
- [ ] Reopening the same service resumes and unmutes it; a late suspension completion cannot leave the visible service silent or asleep.
- [ ] Clicking Back/Reload/Home/fullscreen does not make the flyout vanish merely because its owned window received the click; inactivity still hides it repeatedly.
- [ ] Service A -> Home -> service B immediately hides A, shows `Launching service B…`, and reveals the same WebView only after B's destination navigation completes.
- [ ] Service A -> Home -> service A shows no launch transition and does not reload.
- [ ] Rapidly superseding one launch with another does not surface the canceled destination's error against the new service.
- [ ] An invalid destination changes from the launch transition to the clean Retry/Home error state; the transition cannot remain indefinitely.
- [ ] The transition and its Home button work in normal and F11 fullscreen modes.
- [ ] Disney+ -> Home -> YouTube TV, YouTube TV -> Home -> Paramount+, and Paramount+ -> Home -> Disney+ each load the selected URL.
- [ ] Restart retains services and appearance.

## Reliability, backup, and diagnostics

- [ ] With networking unavailable, Home remains complete and responsive using local settings/artwork only.
- [ ] Opening an unreachable service shows the Monkey TV error state; Retry works after connectivity returns; Home remains available.
- [ ] F11, Alt+Left, Ctrl+R, Ctrl+H, and Ctrl+, work only in their relevant contexts.
- [ ] Clean close removes the session marker and does not leave a Monkey TV process; a simulated stale marker produces one calm recovery notice and then Home.
- [ ] Export creates one small `.mtvbackup` containing settings and referenced artwork only—no profiles, cookies, DRM state, caches, or logs.
- [ ] Import previews and confirms replacement; corrupt, oversized, duplicated-ID, invalid-URL, and unsafe-path backups do not replace current settings.
- [ ] Settings → Storage measures profiles only when opened/refreshed and updates after cleanup.
- [ ] About shows version/runtime/architecture/Data location and copies a concise diagnostic summary without service URLs or private browser data.
- [ ] Repeated errors keep logs below the documented rotation cap.

## Storage and portability

- [ ] Settings → Storage reports Settings & services, Artwork, Persistent browser data, Temporary browser/cache data, logs, and total only when opened/refreshed.
- [ ] Clear Temporary Data reduces known browser/cache data without intentionally signing the user out; after closing/restarting, any locked cache files finish clearing.
- [ ] Clear Browser Data requires confirmation, removes browser profiles/logins, and preserves Monkey TV services/artwork.
- [ ] Reset Monkey TV requires confirmation and removes services, settings, artwork, profiles, and logins; setup returns on the next launch.
- [ ] A new service uses `Data/Browser/Shared`; an existing legacy service still opens from `Data/Browser/Profiles` with its legitimate session preserved.
- [ ] Removing an isolated legacy service deletes its now-abandoned profile. Removing a shared service queues only its exact recorded shared-profile folder for deletion at a safe startup/shutdown point.
- [ ] After several service opens/switches/restarts, ordinary temporary data stays near the 100 MiB soft target or is cleaned on Home/the next safe cleanup point.

## Manual provider/media testing

- [ ] Login and logout
- [ ] Session retention after Home/reopen and app restart
- [ ] Video and audio
- [ ] Subtitles
- [ ] Website keyboard controls
- [ ] Website video fullscreen and exit
- [ ] While Monkey TV fullscreen is also active, the first Escape exits website fullscreen only and the next Escape exits Monkey TV fullscreen.
- [ ] Legitimate protected-media/DRM playback
- [ ] Provider behavior after copying the portable folder to another PC

Record the provider, Windows build, WebView2 Runtime version, exact symptom, and whether the same account works in normal Edge. Do not treat a provider refusal as authorization to bypass its controls.
