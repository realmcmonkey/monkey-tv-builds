# Current Location v2 experimental integration

Current Location v1 remains the default SOCKS5 implementation. V2 is local,
unreleased, disabled by default, and visible in the location picker only after
the user enables **Settings → Experiments → VPN Location V2**, imports the exact
supported official Nord configuration, and saves separate service credentials.

## Fixed Runtime Pack

Install Microsoft’s downloaded **Fixed Version WebView2 Runtime CAB or ZIP** only with
`tools/Install-FixedRuntimePack.ps1`. The installer validates the extracted
`msedgewebview2.exe` Authenticode signer and writes a compact manifest before
atomically placing the pack at `Runtime/WebView2Fixed` next to Monkey TV. It
does not download a package itself and must be run elevated when the application
directory is protected. Replacing or removing an installed runtime requires the
same elevated installer/uninstaller path after all WebView processes exit.

The application selects this runtime only when it validates successfully. Missing
or invalid packs fall back to Evergreen for ordinary browsing and v1 only;
they never enable v2.

## Selected user-space architecture

`WebView2 → v1 loopback boundary → V2 loopback helper → lwIP → OpenVPN 3 Core → Nord`

The helper creates no TAP/Wintun/DCO adapter, Windows VPN profile, route, service,
driver, WFP policy, or system proxy. It accepts HTTP CONNECT only on IPv4
loopback. Destination DNS and TCP are created inside lwIP; the only non-loopback
Windows socket is OpenVPN's outer connection. IPv6 destinations are unsupported
and fail closed. Helper exit immediately returns the managed boundary to Deny.

The first build accepts only the exact `ca1968.nordvpn.com.udp_2.6.ovpn` profile
used by the successful Canada proof. The source file remains untouched; the host
sets OpenVPN 3's documented `disableClientCert` option and supplies an in-memory
`ignore-unknown-option ping-timer-rem` compatibility directive.

The generic helper has passed one real HTTPS CONNECT test with a Canadian exit.
That does not establish provider compatibility. ITVX/UK is not available because
no UK configuration has been imported or proven. Streaming, DRM, authentication,
service workers, popups, downloads, sustained concurrent traffic, tunnel loss,
and browser UDP/WebRTC behavior require manual packet-level testing.
