# Historical VPN proof archive

This directory records historical research context only. It is not part of Monkey TV's current
production build, runtime, packaging, or native-helper rebuild path.

The following disposable Windows VPN feasibility projects were retired after their relevant
production paths were migrated or replaced:

- `MonkeyTV.RasDialProof` — native RAS/IKEv2 dialing investigation.
- `MonkeyTV.RouteGatewayProof` — VPNv2 route-gateway and end-to-end routing investigation.
- `MonkeyTV.VpnProxyProof` — Fixed WebView2/WFP containment investigation.

Their historical conclusions remain represented in the dated `CHANGELOG.md` entries and the
current routing safety requirements. They are not maintained source provenance for the shipped
user-space helper.

The maintained rebuild/provenance source for that helper is
[`native/MonkeyTV.UserSpaceOpenVpnHelper`](../native/MonkeyTV.UserSpaceOpenVpnHelper/).
