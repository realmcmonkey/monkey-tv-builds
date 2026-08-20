param([Parameter(Mandatory)][string]$ServerName)

$ErrorActionPreference = 'Stop'
$proofRoot = $PSScriptRoot
$runner = Join-Path $proofRoot 'Run-Vpnv2ProvisioningSpike.ps1'
$probe = Join-Path (Split-Path $proofRoot -Parent) 'MonkeyTV.RasDialProof\bin\Release\net8.0-windows\win-x64\MonkeyTV.RasDialProof.exe'
$profile = 'MonkeyTV-v2-Dial-' + [guid]::NewGuid().ToString('N')
$resultPath = Join-Path $proofRoot 'nord-dial-result.json'
$nativeLogPath = Join-Path $proofRoot 'nord-dial-native-result.txt'
$certPath = Join-Path $env:TEMP ('monkeytv-nord-root-' + [guid]::NewGuid().ToString('N') + '.der')
$certThumbprint = 'B0A21991007734F5E80C977DD295FFEFB5AD6229'
$certInstalled = $false
$profileCreated = $false
$report = [ordered]@{
    StartedUtc = [DateTime]::UtcNow.ToString('o')
    Profile = $profile
    Server = $ServerName
    CertificatePreexisting = $false
    CertificateInstalled = $false
    ProfileCreated = $false
    RasProjection = $null
    NativeExitCode = $null
    DialPassed = $false
    Error = $null
    ProfileRemoved = $false
    CertificateRemoved = $false
    TasksRemoved = $false
    CleanupVerified = $false
}

try {
    if ($ServerName -notmatch '^[a-z0-9-]+\.nordvpn\.com$') { throw 'The supplied endpoint is not a NordVPN hostname.' }
    if (-not (Test-Path -LiteralPath $probe -PathType Leaf)) { throw "Native RAS probe is missing: $probe" }

    Invoke-WebRequest -UseBasicParsing -Uri 'https://downloads.nordcdn.com/certificates/root.der' -OutFile $certPath
    $certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($certPath)
    if ($certificate.Thumbprint -ne $certThumbprint -or $certificate.Subject -notlike '*NordVPN Root CA*') {
        throw 'Nord root certificate validation failed.'
    }
    # Nord's official Windows IKEv2 procedure requires the CA in the Local Machine
    # Trusted Root store; the RAS/IKE service negotiates outside the caller's user store.
    $existingCertificate = Get-ChildItem Cert:\LocalMachine\Root | Where-Object Thumbprint -eq $certThumbprint
    $report.CertificatePreexisting = $null -ne $existingCertificate
    if ($null -eq $existingCertificate) {
        Import-Certificate -FilePath $certPath -CertStoreLocation Cert:\LocalMachine\Root | Out-Null
        $certInstalled = $true
        $report.CertificateInstalled = $true
    }

    & $runner -ServerName $ServerName -ProfileName $profile -KeepProfile | Out-Host
    $provision = Get-Content (Join-Path $proofRoot 'vpnv2-provisioning-result.json') -Raw | ConvertFrom-Json
    if (-not ($provision.Created -and $provision.Inspected -and $provision.AppPathPresent -and $provision.TrafficFilterPresent)) {
        throw 'VPNv2 profile provisioning did not pass.'
    }
    $profileCreated = $true
    $report.ProfileCreated = $true

    $projection = Get-VpnConnection -Name $profile -ErrorAction Stop
    $report.RasProjection = [ordered]@{
        Name = $projection.Name
        ProfileType = [string]$projection.ProfileType
        AllUserConnection = [bool]$projection.AllUserConnection
        TunnelType = [string]$projection.TunnelType
        AuthenticationMethod = @($projection.AuthenticationMethod | ForEach-Object { [string]$_ })
        ServerAddress = $projection.ServerAddress
        SplitTunneling = [bool]$projection.SplitTunneling
        IsAutoTriggerEnabled = [bool]$projection.IsAutoTriggerEnabled
    }

    Write-Host 'A secure Windows credential prompt will open now. Enter Nord service credentials; they are not saved or logged.'
    $nativeOutput = & $probe $profile 2>&1
    $nativeExitCode = $LASTEXITCODE
    $nativeOutput | Set-Content -LiteralPath $nativeLogPath -Encoding utf8
    $nativeOutput | Out-Host
    $report.NativeExitCode = $nativeExitCode
    $report.DialPassed = $nativeExitCode -eq 0 -and (($nativeOutput -join "`n") -match 'AUTHENTICATION_AND_DIAL=PASS')
    if (-not $report.DialPassed) { throw "Native RAS dial proof failed with exit code $nativeExitCode." }
}
catch {
    $report.Error = $_.Exception.ToString()
}
finally {
    if ($profileCreated) {
        try {
            & $runner -ProfileName $profile -RemoveOnly | Out-Host
            $report.ProfileRemoved = -not [bool](Get-VpnConnection -Name $profile -ErrorAction SilentlyContinue)
        } catch { $report.Error = (($report.Error, $_.Exception.ToString()) -ne $null) -join "`nCLEANUP: " }
    }
    if ($certInstalled) {
        Get-ChildItem Cert:\LocalMachine\Root | Where-Object Thumbprint -eq $certThumbprint | Remove-Item -Force
        $report.CertificateRemoved = -not [bool](Get-ChildItem Cert:\LocalMachine\Root | Where-Object Thumbprint -eq $certThumbprint)
    } else { $report.CertificateRemoved = $true }
    if (Test-Path -LiteralPath $certPath) { Remove-Item -LiteralPath $certPath -Force }
    $remainingTasks = @(Get-ScheduledTask -TaskName 'MonkeyTV-v2-VPNv2-Spike-*' -ErrorAction SilentlyContinue)
    foreach ($task in $remainingTasks) { Unregister-ScheduledTask -TaskName $task.TaskName -Confirm:$false }
    $report.TasksRemoved = -not [bool](Get-ScheduledTask -TaskName 'MonkeyTV-v2-VPNv2-Spike-*' -ErrorAction SilentlyContinue)
    $report.CleanupVerified = $report.ProfileRemoved -and $report.CertificateRemoved -and $report.TasksRemoved -and -not (Test-Path -LiteralPath $certPath)
    $report.CompletedUtc = [DateTime]::UtcNow.ToString('o')
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $resultPath -Encoding utf8
}

if ($report.DialPassed -and $report.CleanupVerified) { exit 0 }
exit 1
