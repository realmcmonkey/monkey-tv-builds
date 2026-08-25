# Architecture

## Application shell

Monkey TV is a .NET 8 WPF application. Its primary in-window navigation is **Home**, **Services**, and **Settings**. The shell is presentation-only: moving between those views does not recreate the browser or reconnect VPN Location.

- **Home** is the artwork-first service launcher.
- **Services** is the full searchable library and organization surface.
- **Settings** contains focused application preferences and setup.
- **VPN Location** is a persistent top-bar quick selector rather than a Home dashboard.

## Browser lifecycle

Monkey TV creates no WebView2 browser while Home is visible. After the first service click it creates exactly one standard WPF WebView2 instance and one shared profile beneath:

```text
Data/Browser/Shared/
```

The same WebView is reused for every service:

```text
Home (no browser at startup)
  → select a service
  → create the shared WebView/profile once
  → navigate to that service
  → Home: mute and hide the same browser; then use best-effort suspension
  → reopen the same service: resume it
  → select a different service: reuse the same browser and navigate it while muted
```

This approach preserves normal cookies, provider logins, service workers, and supported DRM/provider state while avoiding multiple service browsers.

## Service model

Services are generic, data-driven records containing a name, URL, artwork, user-agent preference, and display/organization settings. Artwork, favorite state, visibility, and ordering are portable settings; provider-specific logic is deliberately avoided.

Each service can use Default Experience, TV Experience, or an explicitly configured advanced custom user agent.

## Data and safety boundaries

All app-controlled writable data is beneath `Data` next to the executable:

```text
Data/
  Assets/
  Browser/Shared/
  Cache/
  Logs/
  Settings/
  VpnV2/
  Requirements/
```

Configuration exports intentionally exclude browser profiles, cookies, credentials, caches, logs, and protected provider/DRM state. Logs are bounded and must never include passwords, tokens, or browser profile data.

## Browser UI and protected playback

Monkey TV uses standard WPF WebView2 instead of the composition control because protected playback may not work through the latter. A small owned flyout provides browser controls without placing an invisible WPF layer over provider content.

Monkey TV provides no DRM, content capture, recording, downloading, or provider-protection bypass behavior.

## VPN Location

VPN Location is optional and Direct is always the startup state. The user must explicitly choose a location; returning Home does not silently change an active location.

- United States uses the supported browser-level routing path and existing city choices.
- Canada and United Kingdom use the existing managed VPN Location path when configured.
- The app must verify a requested route before it reports it ready or exposes a service through it.
- Route loss and failed switching must fail closed rather than silently claim the requested location is active.

The maintained native source for the Canada/United Kingdom helper is
[`native/MonkeyTV.UserSpaceOpenVpnHelper`](../native/MonkeyTV.UserSpaceOpenVpnHelper). The
helper and its native dependencies are shipped separately from that source under
`Runtime/VpnV2`, then copied into `Data/Requirements/VpnV2` for a VPN-enabled test build.
The retained `MonkeyTV.UserSpaceOpenVpnProof.exe` filename is a compatibility contract, not a
description of the maintained source tree.

See [VPN Location notes](VPN_LOCATION.md) and [known limitations](KNOWN_LIMITATIONS.md) for the testing boundaries.
