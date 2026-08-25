# Monkey TV user-space OpenVPN helper source

This is the maintained source-provenance tree for the native helper shipped beneath
`Runtime/VpnV2`. It uses OpenVPN 3 Core's external-tunnel API with an in-process lwIP stack,
allowing the helper to exchange IPv4 packets without a Windows tunnel adapter, VPN profile,
route, driver, or website-destination socket.

The production CMake target is `MonkeyTV.UserSpaceOpenVpnHelper`. Its output remains
`MonkeyTV.UserSpaceOpenVpnProof.exe` solely because the current application and runtime pack
resolve that filename. Do not rename the shipped executable until production compatibility is
updated in a dedicated change.

Pinned upstream inputs:

- OpenVPN 3 Core 3.11.7 (`18edfae7e7fd8051c93bd4746ec69be91eb02dbb`), MPL-2.0 path.
- lwIP (`3d896ba0a37ff3ce73270ca5e230707fe47f60e3`), BSD-3-Clause-style license.

No credentials or Nord configuration belong in source control. Live inputs must be selected
at runtime and credentials must come from a local secure Windows prompt.

## Rebuild on Windows

Required only when changing the native helper:

- Visual Studio 2022 Build Tools with the **Desktop development with C++** workload and x64 tools.
- CMake 3.25 or newer.
- Ninja.
- A bootstrapped vcpkg checkout. Its manifest restores Asio, OpenSSL, LZ4, xxHash, and the
  other declared build inputs.

Set `VCPKG_ROOT` to the vcpkg checkout and run the portable launcher:

```powershell
$env:VCPKG_ROOT = 'D:\Development\vcpkg'
.\native\MonkeyTV.UserSpaceOpenVpnHelper\build-local.cmd
```

Alternatively, pass the vcpkg root without setting a persistent environment variable:

```powershell
.\native\MonkeyTV.UserSpaceOpenVpnHelper\build-local.cmd D:\Development\vcpkg
```

The launcher uses an existing x64 Native Tools environment or locates a standard Visual
Studio/Build Tools installation with `vswhere`. It finds CMake and Ninja on `PATH`, with the
copies installed by Visual Studio accepted as a fallback. It does not install tools or modify
the machine. Missing prerequisites produce an actionable error before compilation.

Build output is generated beneath this directory's ignored `out/ninja` folder. Rebuilding the
source does not replace the checked-in shipped runtime under `Runtime/VpnV2`; promoting a newly
built helper is a separate, explicitly validated operation.
