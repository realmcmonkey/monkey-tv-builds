# Known limitations

## Streaming providers

- Provider login, catalogue availability, playback, subtitles, protected media, and fullscreen remain provider-controlled and need hands-on testing with the user’s own accounts.
- A provider can reject an embedded browser even when it works in normal Edge. Monkey TV will not bypass that decision.
- Website popups, permissions, and downloads are intentionally conservative. Related/auth popups are limited; unrelated links can open in the Windows default browser.

## VPN Location

- VPN Location changes only Monkey TV’s browser traffic. It is not a system-wide VPN and does not protect other Windows applications.
- Direct is always the startup state. A selected location is never reconnected automatically at launch.
- Location availability, exit-country detection, provider geo-catalog behavior, and playback must be confirmed manually. A location result does not guarantee a provider will offer different content.
- Connection failures should be treated as failures; Monkey TV must not claim a selected location is ready until its route verification succeeds.

## Browser and storage

- One WebView2 profile is shared between services. Monkey TV cannot safely promise complete origin-by-origin storage cleanup without risking other services’ data.
- Home mutes and hides the active service and uses WebView2’s best-effort suspension. Some provider pages can still pause, reload, or change playback according to their own rules.
- Portable browser profiles can contain machine-bound sign-in and DRM data; copying a build to another PC can require signing in again.
- Automatic artwork and icon discovery are best effort. Manual artwork selection remains available.

## Platform

- Monkey TV targets Windows 11 x64.
- The Fixed Version WebView2 runtime used by the current VPN Location-enabled test build is large and must remain a complete matched set. Do not remove individual files to reduce size.
- .NET single-file support can use operating-system extraction paths internally; Monkey TV’s own writable data remains under `Data`.
