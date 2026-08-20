param([Parameter(Mandatory)][string]$ServerName)
$ErrorActionPreference = 'Stop'
$proofRoot = $PSScriptRoot
$runner = Join-Path $proofRoot 'Run-Vpnv2ProvisioningSpike.ps1'
$gateway = Join-Path $proofRoot 'bin\Release\net8.0-windows\win-x64\MonkeyTV.RouteGateway.exe'
$profile = 'MonkeyTV-v2-Live-' + [guid]::NewGuid().ToString('N')
$result = [ordered]@{ Profile=$profile; Server=$ServerName; CertificateInstalled=$false; Provisioned=$false; Connected=$false; GatewayResult=$null; DirectResult=$null; Error=$null; Cleanup=$false }
$certPath = Join-Path $env:TEMP 'monkeytv-nord-root.der'
$certThumbprint = 'B0A21991007734F5E80C977DD295FFEFB5AD6229'
$certInstalled = $false
$rasHandle = [IntPtr]::Zero

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class MonkeyTvRasSpike {
  [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] public struct RASDIALPARAMS {
    public int dwSize;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst=257)] public string szEntryName;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst=129)] public string szPhoneNumber;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst=129)] public string szCallbackNumber;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst=257)] public string szUserName;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst=257)] public string szPassword;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst=16)] public string szDomain;
    public int dwSubEntry; public IntPtr dwCallbackId;
  }
  [DllImport("rasapi32.dll", CharSet=CharSet.Unicode)] public static extern uint RasDial(IntPtr ext, string phonebook, ref RASDIALPARAMS parameters, uint notifierType, IntPtr notifier, out IntPtr handle);
  [DllImport("rasapi32.dll")] public static extern uint RasHangUp(IntPtr handle);
}
'@

try {
    if ($ServerName -notmatch '^[a-z0-9-]+\.nordvpn\.com$') { throw 'The supplied endpoint is not a NordVPN hostname.' }
    if (-not (Test-Path -LiteralPath $certPath)) {
        Invoke-WebRequest -UseBasicParsing -Uri 'https://downloads.nordcdn.com/certificates/root.der' -OutFile $certPath
    }
    $certificate = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2 $certPath
    if ($certificate.Thumbprint -ne $certThumbprint -or $certificate.Subject -notlike '*NordVPN Root CA*') { throw 'Nord root certificate validation failed.' }
    if (-not (Get-ChildItem Cert:\CurrentUser\Root | Where-Object Thumbprint -eq $certThumbprint)) {
        Import-Certificate -FilePath $certPath -CertStoreLocation Cert:\CurrentUser\Root | Out-Null
        $certInstalled = $true; $result.CertificateInstalled = $true
    }

    & $runner -ServerName $ServerName -ProfileName $profile -KeepProfile
    $provision = Get-Content (Join-Path $proofRoot 'vpnv2-provisioning-result.json') -Raw | ConvertFrom-Json
    if (-not ($provision.Created -and $provision.Inspected -and $provision.AppPathPresent -and $provision.TrafficFilterPresent)) { throw 'VPNv2 live profile provisioning did not pass.' }
    $result.Provisioned = $true

    $credential = Get-Credential -Message 'Enter Nord service credentials for the temporary Monkey TV IKEv2 spike. They will not be saved.'
    if ($null -eq $credential) { throw 'Credential entry was cancelled.' }
    $networkCredential = $credential.GetNetworkCredential()
    $dialProcess = New-Object System.Diagnostics.Process
    $dialProcess.StartInfo.FileName = "$env:SystemRoot\System32\rasdial.exe"
    $dialProcess.StartInfo.Arguments = ('"{0}" "{1}" *' -f $profile, $networkCredential.UserName)
    $dialProcess.StartInfo.UseShellExecute = $false
    $dialProcess.StartInfo.RedirectStandardInput = $true
    $dialProcess.StartInfo.RedirectStandardOutput = $true
    $dialProcess.StartInfo.RedirectStandardError = $true
    $null = $dialProcess.Start()
    $dialProcess.StandardInput.WriteLine($networkCredential.Password)
    $dialProcess.StandardInput.Close()
    $dialProcess.WaitForExit()
    $dialOutput = $dialProcess.StandardOutput.ReadToEnd() + $dialProcess.StandardError.ReadToEnd()
    if ($dialProcess.ExitCode -ne 0) { throw "rasdial.exe failed with exit code $($dialProcess.ExitCode): $dialOutput" }
    $result.Connected = $true
    Start-Sleep -Seconds 3
    $result.GatewayResult = (& $gateway 'https://api.ipify.org').Trim()
    $result.DirectResult = (Invoke-RestMethod -Uri 'https://api.ipify.org').Trim()
    $result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $proofRoot 'nord-live-result.json') -Encoding utf8
}
catch { $result.Error = $_.Exception.ToString(); $result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $proofRoot 'nord-live-result.json') -Encoding utf8 }
finally {
    & "$env:SystemRoot\System32\rasdial.exe" $profile /disconnect | Out-Null
    Start-Sleep -Seconds 2
    try { & $runner -ProfileName $profile -RemoveOnly } catch { $result.Error = (($result.Error, $_.Exception.ToString()) -ne $null) -join "`nCLEANUP: " }
    if ($certInstalled) { Get-ChildItem Cert:\CurrentUser\Root | Where-Object Thumbprint -eq $certThumbprint | Remove-Item -Force }
    $result.Cleanup = $true
    $result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $proofRoot 'nord-live-result.json') -Encoding utf8
}
