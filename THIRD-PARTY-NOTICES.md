# Third-party notices

## Microsoft components

Monkey TV uses:

- .NET 8 / Windows Desktop Runtime
- Microsoft Edge WebView2 SDK
- Microsoft Edge WebView2 Runtime (Evergreen and the app-owned Fixed Version runtime used by the current test build)

Microsoft’s applicable terms and notices govern those components.

## User-space VPN helper components

The maintained source tree at `native/MonkeyTV.UserSpaceOpenVpnHelper` and the corresponding
shipped runtime files under `Runtime/VpnV2` use the following upstream components:

- OpenVPN 3 Core — MPL-2.0 path (or alternative upstream licensing where applicable)
- lwIP — BSD-style license
- OpenSSL and LZ4 — dynamically used native libraries

Any distributed build that includes these components must include their required license materials and comply with their distribution terms. This notice is not a substitute for the upstream license texts.
