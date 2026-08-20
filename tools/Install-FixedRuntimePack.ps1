[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$PackagePath,

    [string]$ApplicationRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'

function Get-MicrosoftSignedFile([string]$Path) {
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Microsoft') {
        throw "The runtime executable is not validly signed by Microsoft: $Path"
    }
}

if (-not (Test-Path -LiteralPath $PackagePath -PathType Leaf)) {
    throw "Fixed Version runtime package was not found: $PackagePath"
}

$applicationRoot = [IO.Path]::GetFullPath($ApplicationRoot)
$rootDirectory = [IO.Path]::GetPathRoot($applicationRoot)
if ($applicationRoot -eq $rootDirectory) { throw 'ApplicationRoot must be a specific Monkey TV installation directory, not a drive root.' }
$runtimeRoot = Join-Path $applicationRoot 'Runtime\\WebView2Fixed'
$stageRoot = Join-Path ([IO.Path]::GetTempPath()) ('MonkeyTV-FixedRuntime-' + [Guid]::NewGuid().ToString('N'))

try {
    New-Item -ItemType Directory -Force -Path $stageRoot | Out-Null
    if ([IO.Path]::GetExtension($PackagePath) -ieq '.cab') {
        & "$env:SystemRoot\\System32\\expand.exe" $PackagePath '-F:*' $stageRoot | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Windows expand.exe could not extract the Fixed Version CAB.' }
    }
    else {
        Expand-Archive -LiteralPath $PackagePath -DestinationPath $stageRoot -Force
    }
    $runtimeExe = Get-ChildItem -LiteralPath $stageRoot -Filter 'msedgewebview2.exe' -Recurse | Select-Object -First 1
    if ($null -eq $runtimeExe) { throw 'The supplied archive is not a WebView2 Fixed Version runtime package.' }
    Get-MicrosoftSignedFile $runtimeExe.FullName

    $sourceRoot = $runtimeExe.Directory.FullName
    $manifest = [ordered]@{
        Schema = 1
        InstalledUtc = [DateTime]::UtcNow.ToString('O')
        RuntimeVersion = $runtimeExe.VersionInfo.ProductVersion
        ExecutableSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $runtimeExe.FullName).Hash
        SourceArchiveSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $PackagePath).Hash
    } | ConvertTo-Json
    Set-Content -LiteralPath (Join-Path $sourceRoot 'monkeytv-runtime.json') -Value $manifest -Encoding utf8

    if ($PSCmdlet.ShouldProcess($runtimeRoot, 'Install validated Monkey TV Fixed Version WebView2 Runtime Pack')) {
        $parent = Split-Path -Parent $runtimeRoot
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
        $replacement = "$runtimeRoot.new"
        if (Test-Path -LiteralPath $replacement) { Remove-Item -LiteralPath $replacement -Recurse -Force }
        Move-Item -LiteralPath $sourceRoot -Destination $replacement
        if (Test-Path -LiteralPath $runtimeRoot) { Remove-Item -LiteralPath $runtimeRoot -Recurse -Force }
        Move-Item -LiteralPath $replacement -Destination $runtimeRoot
        # Required for unpackaged Win32 Fixed Runtime v120+ on Windows 10.
        & "$env:SystemRoot\\System32\\icacls.exe" $runtimeRoot '/grant' '*S-1-15-2-2:(OI)(CI)(RX)' '*S-1-15-2-1:(OI)(CI)(RX)' | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Could not grant the required WebView2 AppContainer read/execute ACLs.' }
        Write-Output "Installed Fixed Version runtime at $runtimeRoot"
    }
}
finally {
    if (Test-Path -LiteralPath $stageRoot) { Remove-Item -LiteralPath $stageRoot -Recurse -Force }
}
