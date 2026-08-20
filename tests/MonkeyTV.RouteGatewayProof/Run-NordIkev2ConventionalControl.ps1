param([Parameter(Mandatory)][string]$ServerName)

$ErrorActionPreference = 'Stop'
$proofRoot = $PSScriptRoot
$probe = Join-Path (Split-Path $proofRoot -Parent) 'MonkeyTV.RasDialProof\bin\Release\net8.0-windows\win-x64\MonkeyTV.RasDialProof.exe'
$profile = 'MonkeyTV-v2-Control-' + [guid]::NewGuid().ToString('N')
$resultPath = Join-Path $proofRoot 'nord-conventional-control-result.json'
$nativeLogPath = Join-Path $proofRoot 'nord-conventional-control-native.txt'
$certPath = Join-Path $env:TEMP ('monkeytv-nord-control-' + [guid]::NewGuid().ToString('N') + '.der')
$certThumbprint = 'B0A21991007734F5E80C977DD295FFEFB5AD6229'
$certInstalled = $false
$profileCreated = $false
$report = [ordered]@{ StartedUtc=[DateTime]::UtcNow.ToString('o'); Profile=$profile; Server=$ServerName; CertificateInstalled=$false; ProfileCreated=$false; Projection=$null; NativeExitCode=$null; DialPassed=$false; Error=$null; ProfileRemoved=$false; CertificateRemoved=$false; CleanupVerified=$false }

try {
    if ($ServerName -notmatch '^[a-z0-9-]+\.nordvpn\.com$') { throw 'Not a NordVPN hostname.' }
    Invoke-WebRequest -UseBasicParsing -Uri 'https://downloads.nordcdn.com/certificates/root.der' -OutFile $certPath
    $certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($certPath)
    if ($certificate.Thumbprint -ne $certThumbprint -or $certificate.Subject -notlike '*NordVPN Root CA*') { throw 'Nord root certificate validation failed.' }
    if (-not (Get-ChildItem Cert:\LocalMachine\Root | Where-Object Thumbprint -eq $certThumbprint)) {
        Import-Certificate -FilePath $certPath -CertStoreLocation Cert:\LocalMachine\Root | Out-Null
        $certInstalled = $true
        $report.CertificateInstalled = $true
    }

    [xml]$eap = @'
<EapHostConfig xmlns="http://www.microsoft.com/provisioning/EapHostConfig"><EapMethod><Type xmlns="http://www.microsoft.com/provisioning/EapCommon">26</Type><VendorId xmlns="http://www.microsoft.com/provisioning/EapCommon">0</VendorId><VendorType xmlns="http://www.microsoft.com/provisioning/EapCommon">0</VendorType><AuthorId xmlns="http://www.microsoft.com/provisioning/EapCommon">0</AuthorId></EapMethod><Config xmlns="http://www.microsoft.com/provisioning/EapHostConfig"><Eap xmlns="http://www.microsoft.com/provisioning/BaseEapConnectionPropertiesV1"><Type>26</Type><EapType xmlns="http://www.microsoft.com/provisioning/MsChapV2ConnectionPropertiesV1"><UseWinLogonCredentials>false</UseWinLogonCredentials></EapType></Eap></Config></EapHostConfig>
'@
    Add-VpnConnection -Name $profile -ServerAddress $ServerName -TunnelType Ikev2 -EncryptionLevel Required -AuthenticationMethod Eap -EapConfigXmlStream $eap -SplitTunneling -RememberCredential:$false -Force | Out-Null
    $profileCreated = $true
    $report.ProfileCreated = $true
    $vpn = Get-VpnConnection -Name $profile
    $report.Projection = [ordered]@{ Name=$vpn.Name; ProfileType=[string]$vpn.ProfileType; AllUserConnection=[bool]$vpn.AllUserConnection; TunnelType=[string]$vpn.TunnelType; AuthenticationMethod=@($vpn.AuthenticationMethod | ForEach-Object {[string]$_}); EncryptionLevel=[string]$vpn.EncryptionLevel; ServerAddress=$vpn.ServerAddress; SplitTunneling=[bool]$vpn.SplitTunneling; IsAutoTriggerEnabled=[bool]$vpn.IsAutoTriggerEnabled }

    Write-Host 'Enter Nord service credentials in the secure Windows prompt. They are not saved or logged.'
    $nativeOutput = & $probe $profile 2>&1
    $nativeExitCode = $LASTEXITCODE
    $nativeOutput | Set-Content -LiteralPath $nativeLogPath -Encoding utf8
    $nativeOutput | Out-Host
    $report.NativeExitCode = $nativeExitCode
    $report.DialPassed = $nativeExitCode -eq 0 -and (($nativeOutput -join "`n") -match 'AUTHENTICATION_AND_DIAL=PASS')
    if (-not $report.DialPassed) { throw "Conventional native RAS control failed with exit code $nativeExitCode." }
}
catch { $report.Error = $_.Exception.ToString() }
finally {
    if ($profileCreated) {
        Remove-VpnConnection -Name $profile -Force -ErrorAction SilentlyContinue
        $report.ProfileRemoved = -not [bool](Get-VpnConnection -Name $profile -ErrorAction SilentlyContinue)
    } else { $report.ProfileRemoved = $true }
    if ($certInstalled) {
        Get-ChildItem Cert:\LocalMachine\Root | Where-Object Thumbprint -eq $certThumbprint | Remove-Item -Force
        $report.CertificateRemoved = -not [bool](Get-ChildItem Cert:\LocalMachine\Root | Where-Object Thumbprint -eq $certThumbprint)
    } else { $report.CertificateRemoved = $true }
    if (Test-Path -LiteralPath $certPath) { Remove-Item -LiteralPath $certPath -Force }
    $report.CleanupVerified = $report.ProfileRemoved -and $report.CertificateRemoved -and -not (Test-Path -LiteralPath $certPath)
    $report.CompletedUtc = [DateTime]::UtcNow.ToString('o')
    $report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $resultPath -Encoding utf8
}

if ($report.DialPassed -and $report.CleanupVerified) { exit 0 }
exit 1
