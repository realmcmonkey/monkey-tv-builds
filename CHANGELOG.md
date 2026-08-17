# Changelog

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
