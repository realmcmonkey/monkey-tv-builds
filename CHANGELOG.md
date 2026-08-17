# Changelog

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
