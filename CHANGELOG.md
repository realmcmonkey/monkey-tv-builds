# Changelog

## v0.0.3-alpha — Build #1

- Added a fast Fluent-inspired startup splash and kept startup provider-free: no WebView, service navigation, or provider contact occurs until a service is selected.
- Added persisted normal/maximized window placement with disconnected-monitor recovery and improved smaller-window Settings/Home layout.
- Added System, Light, and Dark themes plus restrained accent choices across Monkey TV UI; replaced the shared ComboBox template to fix unreadable closed and open dropdown text.
- Polished service launch/error/shutdown surfaces for the selected theme and restored a visible `Shutting down Monkey TV…` state during asynchronous cleanup.
- Added controlled, same-profile temporary WebView2 windows for user-initiated related/auth popups; unrelated user-initiated popup links open in the Windows default browser rather than becoming tabs.
- Added conservative session-only camera, microphone, location, and clipboard permission prompts; notifications and unrelated privileged requests are denied, while autoplay remains available for streaming.
- Added explicit website download handling through the Windows Save dialog outside portable `Data`, and prevented persistent accidental browser zoom.
- Preserved the single service WebView/profile, same-service resume, different-service transition, cache lifecycle, fullscreen, and portable storage architecture.

Known alpha limitations: real provider authentication/popup behavior, protected playback, website fullscreen, and permission requirements still need manual account testing. Domain/auth routing is intentionally conservative and may send an unusual legitimate external flow to the default browser.

## v0.0.2-alpha — Build #3

- Added a native launch transition when switching services so the previous website is hidden immediately while the shared WebView navigates; failed or stuck launches flow into Monkey TV's existing Retry/Home error state.
- Fixed the auto-hiding service overlay and strengthened its Home control for mouse and accessibility activation.
- Home now immediately mutes and hides the active service. Standard on-demand HTML media is paused and marked for automatic same-service resume; clearly detectable live media stays active but muted so its provider can maintain live position.
- Preserved same-service page state and cache when returning from Home. Switching to a different service clears supported disposable disk cache before navigating the shared WebView.
- Changed temporary-browser-data control to a 100 MiB soft target checked every five minutes. Checks read file metadata only; cleanup uses WebView2's supported disk-cache API and preserves cookies, site storage, logins, and protected provider state.
- Expanded lifecycle test coverage and documentation for launch transitions, Home suspension, cache behavior, and limitations of generic live-player detection.

## v0.0.2-alpha — Build #2

- Changed service viewing to one reusable WebView2 and one shared persistent browser profile for all services; same-service Home resumes the existing page, while a different service reuses that WebView and navigates to its destination.
- Conservatively migrated Build #1 profile settings without copying cookies, credentials, or DRM state. Older profile folders remain on disk for recovery and may require a one-time provider login in the unified profile.
- Ensured per-service Default, TV, and advanced custom user agents are applied before each destination navigation, including restoring the captured default identity after TV Experience.
- Fixed the service controls overlay so it repeatedly hides after inactivity without requiring a click; removed Home drag-and-drop ordering.
- Updated Storage to report shared browser data honestly rather than fabricated per-service totals.

## v0.0.2-alpha — Build #1

- Reworked Home with responsive Banner and Square service cards, improved artwork handling, and drag-and-drop ordering.
- Fixed service lifecycle behavior: returning Home preserves only the current service for same-service resume; switching services disposes the previous WebView before loading the next one.
- Added Default Experience and TV Experience, including a centralized smart-TV user agent and optional advanced custom override.
- Added true application fullscreen, coordinated website fullscreen handling, auto-hiding service controls, and cursor hiding during inactive service viewing.
- Added friendly offline/navigation failure screens, keyboard shortcuts, service context actions, and expanded service/banner management.
- Added configuration backup/restore, recovery messaging, Storage and About pages, bounded diagnostics, and update architecture groundwork.
- Reduced portable browser-data growth with shared named WebView2 profiles for new services, safe cache cleanup, a 192 MiB soft temporary-cache budget, and storage-management controls.

## v0.0.1-alpha

- Initial proof-of-concept release: portable service setup, Home launcher, WebView2 service viewing, per-service profiles, and basic settings.
