# Monkey TV changelog

This is the detailed official release history. GitHub release pages contain a short summary and link here. Features described as requiring manual testing were not claimed as provider-verified.

## v0.06-alpha — Build #1

### Internal cleanup

- Made the maintained native VPN-helper build launcher portable across development PCs and documented runtime transfer plus optional native-tooling prerequisites.
- Removed an unused legacy Current Location popup and abandoned update-check abstraction, along with smoke assertions that covered only that unused abstraction. The current in-app VPN Location drawer and all active routing paths are unchanged.
- Removed the retired standalone Settings and Choose Services windows after their remaining entry points moved to the integrated main-window Settings and Services experiences.
- Moved the active user-mode WFP containment policy out of the historical proof project and into production source; the proof project retains its independent copy.
- Moved the maintained native OpenVPN/lwIP helper source, pinned vendor inputs, and rebuild provenance from `tests` to `native/MonkeyTV.UserSpaceOpenVpnHelper` while retaining the current runtime helper filename for compatibility.
- Removed the retired RasDial, RouteGateway, and WFP containment proof projects after their production paths and maintained source provenance had been migrated.

### Integrated Monkey TV experience

- Rebuilt Monkey TV around an integrated main-window experience with persistent **Home**, **Services**, and **Settings** navigation, while preserving the existing shared browser, session, fullscreen, and VPN lifecycles.
- Redesigned Home into an artwork-first streaming launcher with persistent Favorites, independently centered rows of up to four favorite services, and a distinct four-card **Other Services** browsing rail.
- Added persistent service organization fields for favorites and Home visibility. Hidden services remain configured and manageable from Services without appearing on Home.
- Improved the Services workspace into the complete service-library view for searching, opening, editing, favoriting, hiding, reordering, changing artwork, clearing a service’s temporary data, and removing services.
- Moved the built-in service catalog into the integrated Services workspace, with search, region filtering, artwork, add/remove/restore actions, duplicate protection, and bulk add/remove controls.
- Updated First-Time Setup to use the same staged built-in service catalog instead of opening the legacy service-management window; rerun setup still changes no existing data until Finish.
- Refined service-card presentation with true 16:9 artwork, stronger hover/pressed/keyboard-focus feedback, light-blue accent styling, correct Fit/Fill rendering, graceful artwork fallbacks, and automatic trimming of baked-in black side bars where detected.
- Made Other Services rail controls accurately dim and disable when there are no additional services in that direction, including after scrolling, resizing, or library changes.
- Kept the primary navigation centered independently of VPN controls, improved fullscreen presentation, and retained the top-bar VPN Location selector, clock, and fullscreen control.

### Service editing and launch behavior

- Added meaningful unsaved-change protection to Edit Service. Implicit dismissal now offers Save Changes, Discard Changes, or Keep Editing, while explicit Save and Cancel remain immediate and unchanged.
- Made the Edit Service and VPN Location drawers safely dismissible with Escape or a click on their dimmed backdrop. Edit Service retains its unsaved-change protection; VPN Location closes without altering the active route.
- Fixed Edit Service Escape/backdrop dismissal after changing a field: it now reliably opens the Save, Discard, or Keep Editing prompt without crashing or appearing unresponsive.
- Moved Edit Service into a scrollable in-app right-side drawer that opens over the originating Home or Services view, preserves that view when closed, dims the background, and uses reduced-motion-aware drawer transitions.
- Moved Add Custom Service into the same in-app editor drawer. New-service drafts remain unsaved until validation succeeds and Save is selected.
- Added a live service-card artwork preview to Edit Service using the same artwork, Fit/Fill, fallback, and name-rendering behavior as Home and Services cards.
- Added **Test URL** and **Test Configuration** actions for safely trying an edited, unsaved URL and browser experience without saving temporary changes or creating another browser profile.
- Improved the browser-experience editor with a clearer effective Default, TV Experience, or Custom user-agent summary, inline validation, loading feedback, and theme-safe dropdown text.
- Added an optional per-service Preferred VPN Location: **None**, **USA**, **Canada**, or **United Kingdom**. Preferences default to None and remain a launch suggestion only.
- Added **Direct** as a per-service Preferred VPN Location. Unlike None, it explicitly asks to return to the verified Direct route before opening a service when another location is active.
- When a service preference differs from the active location, Monkey TV now offers **Switch & Open**, **Open Anyway**, or **Cancel**. Switch & Open reuses the existing verified VPN routing flow and does not navigate until the requested route is ready.
- Fixed preferred-location service switching so the previous service is not reloaded or opened externally while Monkey TV changes routes for the newly selected service.
- Made verified VPN service launches reveal the provider at the real DOM-ready stage instead of waiting indefinitely for a delayed final navigation-completion event.
- Made Preferred VPN Location changes local metadata-only saves. Editing or saving a preference never connects, prepares, downloads, verifies, or initializes a VPN route.
- Unified Reset Service behind one core reset operation. Resetting from the browser still reloads the active service immediately; resetting from Edit Service stays in the editor and marks only that service for a fresh reload on its next activation.
- Rebuilt the service launch overlay around real lifecycle status, with configured artwork, clear route/browser preparation messages, verified VPN connection and fallback status where applicable, safe error recovery, and no fake percentages or artificial delays.
- Modernized the Preferred VPN Location confirmation and launch-progress surfaces to match the current Monkey TV theme, including restrained motion and Reduced Motion support.

### VPN Location

- Consolidated the user-facing VPN experience into one **VPN Location** feature with a top-bar drawer for **Direct**, **United States**, **Canada**, and **United Kingdom**.
- Redirected missing USA service-credential setup into the integrated **Settings → VPN Location** page instead of opening the retired standalone Settings window.
- Retained existing USA automatic and city choices, while keeping Canada and United Kingdom setup, server selection, fallback, verification, containment, and cleanup behind the simplified interface.
- Removed everyday V1, V2, experiment, configuration-file, and routing-engine terminology from normal VPN Location screens while preserving the established routing behavior internally.
- Kept VPN switching route-before-navigation, verification, safe failure handling, Direct behavior, and active-route preservation unchanged.

### Settings, accessibility, and visual polish

- Replaced the legacy black-backed Monkey TV branding with the new transparent logo throughout Monkey TV, including the application icon, and made the splash artwork a much more prominent part of its startup composition.
- Rebuilt Settings into an integrated desktop workspace with General, Appearance, Streaming, VPN Location, Storage & Data, and About categories.
- Improved Settings typography, control sizing, sidebar selection states, responsive content sizing, checkbox/dropdown readability, and content cards that size to their actual settings instead of leaving unnecessary empty space.
- Increased Home section-heading readability and aligned typography across Home, Services, Settings, drawers, dialogs, and controls.
- Added/refined lightweight WPF animations for cards, navigation, settings categories, menus, drawers, dialogs, pages, and existing loading/error states. Reduced Motion uses instant or minimal-fade behavior.
- Rebuilt the standalone Monkey TV experience around the current shell’s layout language: the provider-free splash, alpha welcome, first-run wizard, compact website-permission card, confirmations, and recovery surfaces now use purpose-built branded compositions rather than legacy utility-dialog layouts. Existing startup, alpha-dismissal, permission, and setup behavior is unchanged.
- Restyled context menus, tooltips, dropdowns, dialogs, confirmations, loading states, and errors to better match the integrated application rather than default utility-style WPF surfaces.

### Website permissions, storage, and cleanup

- Reused the existing safe **Clear Temporary Data** cleanup automatically when switching to a different service and after the shared browser is disposed during normal shutdown. Home/same-service resume remains untouched, and cleanup stays bounded.
- Added a safe **Run first-time setup again** action in Storage & Data. It opens the setup wizard without changing existing data; only finishing the wizard resets Monkey TV data while retaining `Data\Requirements` and then saves the newly selected setup.
- Persist explicit Allow/Deny decisions for camera, microphone, location, and clipboard-read requests by exact normalized origin and permission type.
- Reuse saved permission decisions across Monkey TV restarts and same-profile popup WebViews, coalesce duplicate simultaneous requests, and continue denying notifications and unsupported privileged permissions conservatively.
- Made Reset Service clear permission decisions associated with that service, and made Reset All Website Data clear all saved website permission decisions.
- Kept saved permission decisions out of configuration exports and backups.
- Improved normal shutdown cleanup using supported WebView2 cache clearing followed by bounded cleanup of verified disposable browser/runtime data such as disk cache, code/GPU/shader cache, metrics, crash artifacts, replaceable components, and temporary Monkey TV files.
- Preserved cookies, login sessions, Local Storage, IndexedDB, Cache Storage, service-worker state, legitimate provider/DRM state, preferences, VPN data, `Data\\Requirements`, and saved website permissions during normal shutdown cleanup.
- Corrected Storage & Data totals, About reporting, and copied diagnostics so bundled files beneath `Data\\Requirements` are excluded from normal user-data storage totals while remaining untouched.

### Documentation

- Updated the README, build guidance, architecture notes, VPN Location guidance, alpha-testing checklist, known limitations, and third-party notices to reflect the current integrated interface, portable test-build layout, service workflow, and VPN safety boundaries.

## v0.05-alpha — Build #1

- Optimized the local V2 test distributable without pruning any Microsoft Fixed Runtime component: native helper inputs now come from a stable runtime dependency folder, Release debug payload is disabled, and clean publishes exclude profiles, logs, credentials, downloaded configurations, and build/test intermediates.
- Added executable-grade compression for the extracted local test copy and a maximum-compression archive for local storage and transfer while preserving Current Location v1, V2 USA/Canada/UK, DRM components, fixed-runtime identity, and portable data behavior.
- Added an explicitly opt-in **Settings → Experiments → VPN Location V2** lane without changing Current Location v1 or enabling any route automatically.
- VPN Location V2 now presents United States/Canada/United Kingdom, downloads every matching official UDP configuration from Nord's documented all-server archive into portable `Data/VpnV2/Configs`, and shows those locations in Current Location on Home whenever the experiment is enabled.
- Corrected unreadable Experiments text in light themes by using the live Monkey TV foreground resources explicitly.
- V2 test builds now keep their app-owned, Microsoft-signed Fixed WebView2 Runtime Pack and native VPN helper beneath `Data/Requirements`; packaged V2 no longer searches a source checkout or developer folders for those dependencies. Requirements remain beside `MonkeyTV.exe`, are excluded from user-data relocation/backup, and survive Reset Monkey TV. Added bounded, sanitized `Data/Logs/vpn-v2.log` lifecycle diagnostics for helper startup, configuration evaluation, authentication/tunnel stages, DNS, and route-gateway failures.
- Strengthened experimental V2 readiness with three tunnel-contained country checks using a strict independent majority, removed high-volume per-packet diagnostic spam, and added cooled-down candidate fallback for Nord session teardown during Direct/country switching.
- Experimental V2 connection now fails over across up to five downloaded configurations from the selected country before reporting a fail-closed connection error.
- Integrated the proven Fixed WebView2 application-ID WFP containment rules into V2 through a minimal elevated, session-scoped broker. One UAC approval enables reusable containment for the app session; Direct disables it and performs a real direct TCP verification before reporting Direct.
- Added strict import of the one official Nord Canada OpenVPN profile proven during development (`ca1968.nordvpn.com`, UDP). No country, city, or endpoint is inferred.
- Integrated a separate native OpenVPN 3 Core + lwIP helper. Browser CONNECT destinations and DNS remain inside its user-space packet path; Windows sees the loopback proxy and the outer Nord transport, not website destination sockets.
- Added separate DPAPI-protected V2 credentials, fail-closed helper lifecycle, IPv4-only destination handling, and automatic return to Deny if the helper exits.
- No Windows VPN profile, route, adapter, driver, service, policy, or elevation is used by V2.
- This work is local and experimental. Provider logins, DRM, playback, popup flows, sustained parallel traffic, tunnel recovery, and WebRTC packet containment still require hands-on testing before release.

## v0.04-alpha — Build #5

### Manage Services and YouTube

- Split the previous generic YouTube template into two independent built-in services with stable IDs: **YouTube (Desktop)** (`https://www.youtube.com/`, Default Experience) and **YouTube (Smart TV Experience)** (`https://www.youtube.com/tv`, TV Experience).
- Kept both definitions on the existing bundled YouTube artwork rather than adding duplicate image files to portable data.
- Safely migrated only an untouched legacy built-in YouTube entry to the Desktop name. Manually created or edited YouTube services are not renamed, converted, or overwritten.
- Replaced the dated `Enabled` checkbox in Choose / Manage Services with a compact, right-aligned, keyboard-focusable On/Off switch. Status text now communicates only additional information such as customized or pending removal.
- Sorted the full built-in library and all search/region-filtered results alphabetically by service name.

### Service view and Home cards

- Fixed the service-view three-dot menu. It now explicitly opens its menu on mouse click or keyboard activation and exposes the existing **Restart Service** and **Reset Site Data** actions repeatedly.
- Strengthened Home-card hover and keyboard-focus feedback using a restrained accent border, slightly stronger shadow, existing 1.02 scale, and 2–3 px lift. Reduced Motion applies the state change without unnecessary animation.
- Restored correct Square card sizing. Home now uses 16:9 dimensions only for Banner cards and a square content area for Square cards.
- Preserved each service's Banner Fit setting: **Fit** keeps all artwork visible; **Fill** uses aspect-preserving crop to cover the card without distortion.

### Current Location

- Replaced the flat location menu with a simple two-step selector: Direct and Last used on the first view, then documented countries A–Z, then a selected country’s documented city/location entries A–Z.
- Selecting a country only opens its location choices. It does not connect until the user chooses a location and presses **Connect**. Direct and Last used remain immediate choices.
- Deliberately omitted a fake “Fastest Server” choice because Monkey TV does not use an undocumented Nord API to claim server recommendations.
- Fixed the gateway route lifecycle so Nord → Direct → the same Nord location cancels stale upstream connections and can reconnect without restarting Monkey TV or re-entering credentials.
- The fixed WebView2 loopback proxy boundary continues to apply to normal HTTP/HTTPS browser traffic; QUIC remains disabled and WebRTC uses the existing best-effort non-proxied-UDP restriction. This does not prove that any specific provider’s catalog is determined only by IP location.

### Navigation errors

- Added a narrow auth-redirect classification for known related/login/account URLs. A WebView2 `Unknown` completion during such a transitional provider login redirect no longer immediately replaces the provider flow with Monkey TV’s error screen.
- Confirmed failures, unrelated `Unknown` navigations, connection errors, security errors, explicit navigation timeouts, and process failures still use the existing error UI and technical details.

### Known alpha limitations

- Current Location is browser-proxy routing, not a full-device VPN or an absolute WebRTC/UDP leak-proof boundary.
- Nord service credential authentication, real endpoint availability, WebView external-IP observation, provider login redirects, geo-catalog availability, DRM playback, and streaming playback require hands-on testing. Provider availability may also depend on account, licensing, cookies, and other provider signals.

## v0.04-alpha — Build #4

- Fixed `ManageServicesWindow` crashing with `ArgumentNullException` when WPF raised `ComboBox.SelectionChanged` during constructor/XAML initialization before the service collection was available.
- Initialize the settings, store, and built-in service item collection before `InitializeComponent`, enable filter handling only once controls are ready, and guard Refresh against early/null controls.
- This makes Choose / Manage Services available during first-run setup and normal Home/Settings access without relying on WPF event timing.

## v0.04-alpha — Build #3

- Fixed the branded splash window staying above startup UI indefinitely when the alpha warning, first-run setup, recovery, or another startup dialog appeared.
- The splash now closes before startup dialogs are presented while retaining its minimum display behavior during normal Home initialization.

## v0.04-alpha — Build #2

### Escape and Home launcher

- Corrected Escape ownership while a service is active: Escape is forwarded to the provider/WebView instead of exiting Monkey TV application fullscreen.
- On Home, Escape returns to the suspended active service when appropriate, preserving the existing same-service resume behavior.
- Redesigned Home as a focused streaming-service launcher with responsive large 16:9 cards, stable incomplete rows, vertical scrolling, empty state actions, restrained hover/press feedback, and direct custom-order drag/drop support.

### Built-in service library

- Added the local-only Choose / Manage Services library, limited to services for which Monkey TV ships official bundled artwork.
- Added built-in service IDs, template URLs, regions, artwork references, default Experience values, duplicate warnings, enable/disable staging, Restore Defaults for customized built-in copies, and custom-service entry through the existing editor.
- Added Settings → Home sorting choices: Custom, A–Z, Z–A, newest first, and oldest first. Automatic modes do not overwrite the saved Custom order; returning to Custom restores it.

### Service/browser view

- Added Forward alongside Back and keeps Back/Forward disabled when the shared WebView history cannot perform that action.
- Added a subtle navigation progress indicator in the service overlay.
- Added Restart Service, which navigates the active service to its configured start URL without clearing provider cookies or website data.
- Added Reset Site Data with destructive confirmation and conservative service-domain handling, distinct from non-destructive temporary-cache cleanup.
- Expanded service error screens with human-readable classifications, Retry/Home actions, expandable technical details, and Copy Details without secrets, cookies, tokens, or credentials.

### Settings, storage, and recovery

- Organized Settings into General, Home, Appearance, Streaming, Storage, and About while keeping the control set intentionally small.
- Added/retained theme, accent, motion, service card shape/size, names, sorting, external-link behavior, new-custom-service Experience, permission-reset information, portable data access, logs, diagnostic copy, backup/restore, and destructive reset actions.
- Hardened Storage and Recovery with clear distinctions between Clear Temporary Data, Reset Current Site Data, Reset All Website Data, and Reset Monkey TV.
- Added bounded known-good configuration backups, configuration validation before use, recovery from compatible backups, preservation of damaged configuration, recovery UI, defensive migration backups, and conservative repeated-startup recovery mode.

### Known alpha limitations

- Provider-specific site-data clearing depends on supported WebView2 APIs and conservative domain metadata; unusual multi-domain authentication flows need manual testing.
- Home, drag/drop, popup/auth routing, fullscreen, provider logins, protected playback, and real streaming playback require hands-on Windows testing.

## v0.04-alpha — Build #1

### Current Location (Feature #0.01)

- Added optional **Current Location** setup using NordVPN service credentials for browser-level geographic routing. The NordVPN Windows app is not required.
- Added a fixed Monkey TV loopback gateway and configured Monkey TV’s one shared WebView2 environment to use that local routing boundary. Other Windows applications are not intentionally routed through it.
- Added globally selected Direct/Nord route state, documented Nord SOCKS5 locations, DPAPI-protected service credentials, route verification before service reload/navigation, fail-closed behavior for failed Nord selection, and clean gateway shutdown.
- Current Location always begins in **Direct** on every startup. Monkey TV never reconnects Nord, checks Nord endpoints, loads a service, or contacts a provider automatically at startup.
- Added optional Home visibility for the Current Location control. Hiding it safely returns an active Nord route to Direct first and does not delete saved credentials.
- Added first-run setup guidance that asks for Nord **service credentials**, not the normal Nord account password, and opens official Nord instructions in the Windows default browser.

### Known alpha limitations

- Current Location is a browser proxy, not a full system VPN and not an absolute zero-leak boundary.
- WebRTC protection is best-effort, QUIC is disabled only for Monkey TV’s WebView environment, and real Nord authentication, provider compatibility, DRM playback, and location behavior need manual testing.

## v0.0.3-alpha — Build #2

- Added 46 compact built-in service banners from the supplied Canada, General, UK, USA, and Web artwork collections.
- Added a searchable, categorized service-artwork picker that supports built-in banners and the existing portable file-import flow.
- Added local built-in artwork matching for newly configured services before website artwork discovery; no provider page is contacted merely to render the picker.
- Kept built-in artwork inside the single executable so selections add no files to portable `Data`, configuration backups remain small, and existing custom artwork continues to work.
- Added the supplied Monkey TV image as the executable/window icon, Home branding, alpha-warning logo, and startup splash artwork.
- Changed the splash to remain visible for at least three seconds and longer when Home is not ready, without initializing WebView2 or preloading a service.

Known alpha limitation: automatic artwork matching is intentionally conservative. Services with unusual names or domains may require choosing their built-in banner manually.

## v0.0.3-alpha — Build #1

- Added a Fluent-inspired startup splash and preserved provider-free startup: no WebView, service navigation, or provider contact occurs until a service is selected.
- Added persisted normal/maximized window placement with disconnected-monitor recovery, sensible minimum sizing, and responsive Home/Settings layouts.
- Added System, Light, and Dark themes plus restrained accent choices across Monkey TV UI; fixed shared ComboBox styling so closed and open dropdown text remains readable in all themes.
- Added startup/shutdown polish including visible launch, error, and clean shutdown states.
- Added controlled, same-profile temporary WebView2 windows for user-initiated related/auth popups; unrelated user-initiated links open in the Windows default browser instead of becoming tabs.
- Added conservative session-only camera, microphone, location, and clipboard prompts; notifications and unsupported privileged requests are denied, while autoplay remains available for streaming.
- Added explicit browser download handling through the Windows Save dialog outside portable `Data` and prevented persistent accidental browser zoom.

Known alpha limitations: protected playback, provider sign-in/popup flows, website fullscreen, and provider-specific permission requirements require manual account testing.

## v0.0.2-alpha — Build #3

- Added the native “Launching [Service]…” transition when switching services so the previous website is hidden before the shared WebView starts its new navigation.
- Added transition artwork/initial fallback, supported navigation-completion reveal, launch timeout recovery, and Retry/Home errors for failed or stuck launches.
- Fixed the auto-hiding service overlay and strengthened its Home action for mouse, keyboard, and accessibility activation.
- Home now immediately mutes and hides the active service. Standard finite HTML media is paused and recorded for same-service resume; clearly detectable live media remains running but muted so providers can preserve live position.
- Preserved same-service page state and cache. Switching to a different service clears supported disposable disk cache before the one shared WebView navigates.
- Set the portable temporary-cache soft target to 100 MiB with a lightweight metadata check every five minutes during active service use. Cleanup preserves cookies, site storage, logins, and protected provider state.

Known alpha limitation: live-media and custom-player detection is best effort; inaccessible provider players may only be muted and hidden.

## v0.0.2-alpha — Build #2

- Changed service viewing to one reusable WebView2 and one shared persistent browser profile for all services.
- Returning Home preserves and hides the current page for same-service resume. Choosing a different service reuses that same WebView and navigates it away; Monkey TV does not create another service WebView or profile.
- Conservatively migrated Build #1 named-profile settings without copying cookies, credentials, or DRM state. Old profile folders remain available for manual recovery and users may need to sign in once in the unified profile.
- Applied each service’s Default Experience, TV Experience, or advanced custom user agent before its destination navigation, and restores the captured normal WebView identity after TV Experience.
- Fixed overlay auto-hide so it repeatedly hides after inactivity without requiring a click. Top-edge reveal, cursor behavior, F11, Escape, and browser shortcuts were preserved.
- Updated Storage to report shared browser data honestly rather than fabricated per-service totals.

## v0.0.2-alpha — Build #1

- Added responsive Home Banner and Square service cards, artwork import/selection, manual banner override, fit behavior, and portable artwork storage.
- Added service lifecycle rules for Home/switching, Default Experience and TV Experience with one centralized Smart TV user agent, and an optional advanced per-service custom user-agent override.
- Added true application fullscreen, coordinated website fullscreen handling, auto-hiding service controls, cursor hiding, subtle browser overlay navigation, and F11/Escape handling.
- Added friendly offline/navigation failure screens, browser Back/Reload/Home controls, keyboard shortcuts, service context actions, and service add/edit/remove/reorder management.
- Added configuration export/import excluding browser-profile data; recovery messaging; Storage and About pages; bounded diagnostics; crash marker handling; and update architecture groundwork.
- Added portable-data controls, artwork optimization, bounded logs, cache cleanup, and storage reporting. This early build used named WebView2 profiles before Build #2 moved new launches to one shared profile.

## v0.0.1-alpha — Build #2

- Fixed the initial service lifecycle/switching implementation and prepared the early portable alpha for testing with multiple configured streaming services.
- Retained configured services and portable Data while correcting active service behavior from Home during the early proof-of-concept phase.

## v0.0.1-alpha — Build #1

- Initial portable proof of concept: alpha warning, first-run service setup, generic custom service configuration, Home launcher, WebView2 service viewing, basic settings, per-service browser identity options, and portable Data beside the executable.
- Established the project’s legitimate streaming-launcher scope: provider accounts, subscriptions, DRM, and playback remain owned by the provider; Monkey TV does not host, download, decrypt, capture, or bypass protected content.
