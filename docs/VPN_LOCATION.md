# VPN Location implementation notes

This document records the current local test architecture. It is not a claim that every provider, country, or streaming service has passed account-level testing.

## User-facing behavior

VPN Location is a simple top-bar selector:

- Direct
- United States
- Canada
- United Kingdom

Monkey TV starts Direct every time. Selecting a location begins connection automatically; selecting Direct removes the active managed route. The application must verify the selected route before loading or reloading a service through it.

## Routing paths

- **United States** retains the existing documented Nord SOCKS5 city/location choices.
- **Canada** and **United Kingdom** use official downloaded Nord UDP configuration inputs through the existing user-space helper path.

Credentials are Nord manual/service credentials, stored with Windows DPAPI. They must never be included in logs, source code, command lines, configuration exports, or packages.

## Safety requirements

- No Nord desktop application is required.
- No system-wide VPN route should be created for normal use.
- Downloaded country inputs are retained under `Data/VpnV2/Configs` only.
- Failed connection attempts try bounded alternate candidates and clean up before reporting failure.
- A disconnected or failed helper must fail closed; it must never silently advertise a requested route as active.
- Returning Home preserves an already active route, but startup never auto-connects.

## Maintained helper source and shipped runtime

The maintained production-native source tree is:

```text
native/MonkeyTV.UserSpaceOpenVpnHelper/
  src/       Monkey TV's helper implementation and lwIP port
  vendor/    pinned OpenVPN 3 Core and lwIP source inputs
  CMakeLists.txt, vcpkg.json, README.md
```

It is the source-provenance and rebuild tree for the user-space VPN helper. It is not a
disposable proof project.

The current test build carries the resulting runtime dependencies separately:

The current test build carries its required dependencies below:

```text
Data/Requirements/
  VpnV2/             MonkeyTV.UserSpaceOpenVpnProof.exe and required native DLLs
  WebView2Fixed/
```

The Fixed Version WebView2 runtime is an app-owned matched runtime set. Do not remove individual runtime files or substitute an unrelated system runtime for the current test configuration.

`Runtime/VpnV2` in the repository is the source for `Data/Requirements/VpnV2` when publishing.
The helper filename remains `MonkeyTV.UserSpaceOpenVpnProof.exe` only as an application/runtime
compatibility contract; it does not make the maintained helper source a proof project.

For retired historical VPN spike context, see the [research archive](../research/VPN_PROOF_ARCHIVE.md).

## Manual testing still required

Before any release, test login, protected playback, sustained streaming, WebSockets, service workers, popups, downloads, location switching, network loss, and route verification with real accounts. Provider availability may depend on account, rights, cookies, and signals beyond the observed exit country.
