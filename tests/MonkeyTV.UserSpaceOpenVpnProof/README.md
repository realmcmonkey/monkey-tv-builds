# Monkey TV user-space OpenVPN feasibility proof

Disposable proof only. This directory is intentionally separate from production Monkey TV.

The proof tests whether OpenVPN 3 Core's external-tunnel API can exchange IPv4 packets with
an in-process lwIP stack, allowing a single HTTPS TCP flow without a Windows tunnel adapter,
VPN profile, route, driver, or destination socket.

Pinned upstream inputs:

- OpenVPN 3 Core 3.11.7 (`18edfae7e7fd8051c93bd4746ec69be91eb02dbb`), MPL-2.0 path.
- lwIP (`3d896ba0a37ff3ce73270ca5e230707fe47f60e3`), BSD-3-Clause-style license.

No credentials or Nord configuration belong in source control. Live inputs must be selected
at runtime and credentials must come from a local secure Windows prompt.
