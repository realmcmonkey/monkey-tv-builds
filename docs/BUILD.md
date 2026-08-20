# Build and packaging

## Dependencies

- Windows 11 x64 development machine
- .NET 8 SDK or newer
- NuGet access for `Microsoft.Web.WebView2` 1.0.3537.50
- Microsoft Edge WebView2 Evergreen Runtime for ordinary direct/V1 development and testing
- For V2: a Microsoft Fixed Version WebView2 Runtime Pack at `Runtime/WebView2Fixed` and the tracked V2 helper files at `Runtime/VpnV2`

No Windows-specific Visual Studio workload is required for command-line builds. Paths in the project are relative, so the source directory can be moved between PCs.

## Build

```powershell
dotnet restore .\MonkeyTV\MonkeyTV.csproj
dotnet build .\MonkeyTV\MonkeyTV.csproj -c Release --no-restore
```

## Publish

```powershell
dotnet publish .\MonkeyTV\MonkeyTV.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\artifacts\publish
```

The project enables compressed single-file publishing and embeds managed/native app dependencies into `MonkeyTV.exe`. At runtime .NET may extract internal native support files into its operating-system-managed temporary extraction cache; these are not loose distribution files. The V2 release archive additionally contains the complete app-owned Fixed Version WebView2 Runtime beneath `Data/Requirements/WebView2Fixed` and the V2 helper beneath `Data/Requirements/VpnV2`.

## Size trade-off

The self-contained portable build intentionally includes the .NET 8 Windows Desktop runtime. The v0.0.3-alpha Build #1 local release measurement is 78,179,732 bytes (about 74.56 MiB); the majority is the .NET/WPF/Windows SDK runtime, not Monkey TV code or WebView2 (which relies on the installed Evergreen Runtime). WPF is not supported or recommended with .NET trimming, so trimming is not enabled.

A local framework-dependent single-file experiment measured about 24.4 MiB, but requires the matching .NET 8 Windows Desktop Runtime on the target PC. It is not the default portable build because that extra prerequisite weakens copy-to-another-PC reliability. Do not change the default distribution model without explicit user approval.

Create a fresh empty `Data` folder next to the published EXE, include README/testing notes, then ZIP that folder. Never copy a developer/test `Data` directory into a public package because profiles can contain cookies and tokens.

Expected user-facing layout:

```text
Monkey TV/
  MonkeyTV.exe
  Data/
    Requirements/
      WebView2Fixed/
      VpnV2/
```

Documentation files may accompany the alpha without affecting runtime portability.
