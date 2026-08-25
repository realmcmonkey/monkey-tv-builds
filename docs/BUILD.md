# Build guide

## Requirements

- Windows 11 x64
- Git
- .NET 8 SDK
- NuGet access for the first restore
- The repository `Runtime` folder for the current VPN Location-enabled build:
  - `Runtime/VpnV2`
  - `Runtime/WebView2Fixed`

The runtime files are intentionally separate from source code. They are copied into the local
test build only when publishing with VPN Location dependencies enabled:

```text
Runtime/VpnV2/       shipped user-space helper and required native DLLs
Runtime/WebView2Fixed/ app-owned Fixed Version WebView2 runtime
```

The maintained rebuild source for the shipped user-space helper is
[`native/MonkeyTV.UserSpaceOpenVpnHelper`](../native/MonkeyTV.UserSpaceOpenVpnHelper).
It contains Monkey TV's native helper source, pinned OpenVPN 3 Core and lwIP vendor inputs,
CMake configuration, and licensing provenance. Its production CMake target deliberately emits
the compatibility filename `MonkeyTV.UserSpaceOpenVpnProof.exe`; that file, together with its
native dependencies, is the shipped runtime contract under `Runtime/VpnV2`.

Historical VPN proof projects are documented under [`research`](../research/), not used for
current production builds or helper rebuilds.

Normal WPF/.NET development does not require a native compiler, CMake, Ninja, or vcpkg. Those
tools are needed only when rebuilding the shipped user-space VPN helper. The canonical native
prerequisites and portable vcpkg setup are documented in the
[`native/MonkeyTV.UserSpaceOpenVpnHelper` README](../native/MonkeyTV.UserSpaceOpenVpnHelper/README.md).

## Build

```powershell
dotnet restore .\MonkeyTV\MonkeyTV.csproj
dotnet build .\MonkeyTV\MonkeyTV.csproj -c Release --no-restore
```

## Publish a local test copy

Publish to a temporary folder first, then replace only the executable in the existing test build. This preserves the user’s local `Data` folder.

```powershell
$publish = Join-Path $env:TEMP 'MonkeyTV-publish'
dotnet publish .\MonkeyTV\MonkeyTV.csproj -c Release --no-restore -o $publish
Copy-Item "$publish\MonkeyTV.exe" ".\builds\Test Build\MonkeyTV.exe" -Force
Remove-Item $publish -Recurse -Force
```

Close Monkey TV before replacing the executable. Windows will refuse to overwrite a running application.

## Output layout

```text
builds/
  Test Build/
    MonkeyTV.exe
    Data/
      Requirements/
        VpnV2/
        WebView2Fixed/
```

`Data` is user data. Do not package or share an existing `Data` folder: it can contain settings, credentials protected for the current Windows user, browser profiles, and website sign-ins.

## Cleanup after a successful local build

Keep source, `Runtime`, documentation, tests, Git history, and the one current `builds/Test Build` folder. Remove only generated output when it is no longer needed:

```text
MonkeyTV/bin/
MonkeyTV/obj/
tests/**/bin/
tests/**/obj/
temporary publish folders
obsolete builds/
```

Never remove individual files from `Runtime/WebView2Fixed` or `Data/Requirements/WebView2Fixed`; WebView2 Fixed Version runtime files are a matched runtime set.

## Move development to another PC

1. Copy the complete current working tree, or commit the intended work and clone it. A fresh
   clone does not include uncommitted or ignored files.
2. Copy `Runtime/WebView2Fixed` separately, or reinstall the matching Fixed Runtime Pack with
   `tools/Install-FixedRuntimePack.ps1`. This ignored local dependency is not stored in Git.
3. Confirm the shipped helper and DLLs are present under `Runtime/VpnV2`.
4. Install Git and the .NET 8 SDK, then confirm NuGet access.
5. Run the restore and Release-build commands above.
6. Run the smoke suite:

   ```powershell
   dotnet run --project .\tests\MonkeyTV.SmokeTests\MonkeyTV.SmokeTests.csproj -c Release
   ```

7. Configure MSVC, CMake, Ninja, and vcpkg only if native-helper development is required.
8. Use a clean adjacent `Data` folder for development builds. Re-enter Nord credentials and
   provider sign-ins as needed; DPAPI secrets and browser/DRM state may be machine-bound.

Do not make development depend on transferring `builds/Test Build/Data`. `Data/Requirements`
is runtime infrastructure, while the rest of that test-build Data tree may contain personal
settings, credentials, cookies, sign-ins, and provider state.
