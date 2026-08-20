param(
    [Parameter(Mandatory)][string]$GatewayPath,
    [Parameter(Mandatory)][string]$ResultPath,
    [Parameter(Mandatory)][string]$UserSid,
    [string]$ProfileName = ('MonkeyTV-v2-Spike-' + [guid]::NewGuid().ToString('N')),
    [string]$ServerName = 'invalid.monkeytv.invalid',
    [switch]$KeepProfile,
    [switch]$RemoveOnly
)
$ErrorActionPreference = 'Stop'
$namespace = 'root\cimv2\mdm\dmmap'
$className = 'MDM_VPNv2_01'
$created = $false
$session = $null
$options = $null
$keyInstance = $null
$report = [ordered]@{
    StartedUtc = [DateTime]::UtcNow.ToString('o')
    Identity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    IsSystem = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value -eq 'S-1-5-18'
    ProfileName = $profileName
    GatewayPath = $GatewayPath
    UserSid = $UserSid
    ClassVisible = $false
    Created = $false
    Inspected = $false
    AppPathPresent = $false
    TrafficFilterPresent = $false
    RasProfileVisible = $false
    Removed = $false
    CleanupVerified = $false
    Error = $null
}

try {
    $null = Get-CimClass -Namespace $namespace -ClassName $className
    $report.ClassVisible = $true
    $options = New-Object Microsoft.Management.Infrastructure.Options.CimOperationOptions
    $options.SetCustomOption('PolicyPlatformContext_PrincipalContext_Type', 'PolicyPlatform_UserContext', $false)
    $options.SetCustomOption('PolicyPlatformContext_PrincipalContext_Id', $UserSid, $false)
    $session = New-CimSession
    $keyInstance = New-Object Microsoft.Management.Infrastructure.CimInstance $className, $namespace
    $keyInstance.CimInstanceProperties.Add([Microsoft.Management.Infrastructure.CimProperty]::Create('ParentID', './Vendor/MSFT/VPNv2', 'String', 'Key'))
    $keyInstance.CimInstanceProperties.Add([Microsoft.Management.Infrastructure.CimProperty]::Create('InstanceID', $profileName, 'String', 'Key'))
    if ($RemoveOnly) { return }
    $xml = @"
<VPNProfile>
  <NativeProfile>
    <Servers>$([Security.SecurityElement]::Escape($ServerName))</Servers>
    <NativeProtocolType>IKEv2</NativeProtocolType>
    <Authentication>
      <UserMethod>Eap</UserMethod>
      <Eap>
        <Configuration>
          <EapHostConfig xmlns="http://www.microsoft.com/provisioning/EapHostConfig">
            <EapMethod>
              <Type xmlns="http://www.microsoft.com/provisioning/EapCommon">26</Type>
              <VendorId xmlns="http://www.microsoft.com/provisioning/EapCommon">0</VendorId>
              <VendorType xmlns="http://www.microsoft.com/provisioning/EapCommon">0</VendorType>
              <AuthorId xmlns="http://www.microsoft.com/provisioning/EapCommon">0</AuthorId>
            </EapMethod>
            <Config xmlns="http://www.microsoft.com/provisioning/EapHostConfig">
              <Eap xmlns="http://www.microsoft.com/provisioning/BaseEapConnectionPropertiesV1">
                <Type>26</Type>
                <EapType xmlns="http://www.microsoft.com/provisioning/MsChapV2ConnectionPropertiesV1">
                  <UseWinLogonCredentials>false</UseWinLogonCredentials>
                </EapType>
              </Eap>
            </Config>
          </EapHostConfig>
        </Configuration>
      </Eap>
    </Authentication>
    <RoutingPolicyType>SplitTunnel</RoutingPolicyType>
    <DisableClassBasedDefaultRoute>true</DisableClassBasedDefaultRoute>
  </NativeProfile>
  <AppTrigger><App><Id>$([Security.SecurityElement]::Escape($GatewayPath))</Id></App></AppTrigger>
  <TrafficFilter>
    <App><Id>$([Security.SecurityElement]::Escape($GatewayPath))</Id></App>
    <RoutingPolicyType>ForceTunnel</RoutingPolicyType>
  </TrafficFilter>
</VPNProfile>
"@
    $escapedXml = [Security.SecurityElement]::Escape($xml)
    $newInstance = New-Object Microsoft.Management.Infrastructure.CimInstance $className, $namespace
    $newInstance.CimInstanceProperties.Add([Microsoft.Management.Infrastructure.CimProperty]::Create('ParentID', './Vendor/MSFT/VPNv2', 'String', 'Key'))
    $newInstance.CimInstanceProperties.Add([Microsoft.Management.Infrastructure.CimProperty]::Create('InstanceID', $profileName, 'String', 'Key'))
    $newInstance.CimInstanceProperties.Add([Microsoft.Management.Infrastructure.CimProperty]::Create('ProfileXML', $escapedXml, 'String', 'Property'))
    $null = $session.CreateInstance($namespace, $newInstance, $options)
    $created = $true
    $report.Created = $true

    $instance = $session.GetInstance($namespace, $keyInstance, $options)
    $report.Inspected = $null -ne $instance
    $decoded = [Net.WebUtility]::HtmlDecode([string]$instance.ProfileXML)
    $report.AppPathPresent = $decoded.IndexOf($GatewayPath, [StringComparison]::OrdinalIgnoreCase) -ge 0
    $report.TrafficFilterPresent = $decoded.IndexOf('<TrafficFilter>', [StringComparison]::OrdinalIgnoreCase) -ge 0 -and $decoded.IndexOf('<RoutingPolicyType>ForceTunnel</RoutingPolicyType>', [StringComparison]::OrdinalIgnoreCase) -ge 0
    $report.RasProfileVisible = $true
}
catch {
    $report.Error = $_.Exception.ToString()
}
finally {
    if ($created -and -not $KeepProfile -or $RemoveOnly) {
        try {
            if ($null -ne $keyInstance) { $session.DeleteInstance($namespace, $keyInstance, $options) }
            $report.Removed = $true
        } catch { $report.Error = (($report.Error, $_.Exception.ToString()) -ne $null) -join "`nCLEANUP: " }
    }
    $remaining = $null
    if ($null -ne $keyInstance) { try { $remaining = $session.GetInstance($namespace, $keyInstance, $options) } catch {} }
    $report.CleanupVerified = $null -eq $remaining
    $report.CompletedUtc = [DateTime]::UtcNow.ToString('o')
    $report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $ResultPath -Encoding utf8
    if ($null -ne $session) { $session.Dispose() }
}

if ($report.Created -and $report.Inspected -and $report.AppPathPresent -and $report.TrafficFilterPresent -and $report.CleanupVerified) { exit 0 }
exit 1
