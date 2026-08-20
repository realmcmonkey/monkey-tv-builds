param([Parameter(Mandatory)][string]$ServerName)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$repo = Split-Path -Parent (Split-Path -Parent $root)
$runner = Join-Path $root 'Run-Vpnv2ProvisioningSpike.ps1'
$gateway = Join-Path $root 'bin\Release\net8.0-windows\win-x64\MonkeyTV.RouteGateway.exe'
$dial = Join-Path (Split-Path $root -Parent) 'MonkeyTV.RasDialProof\bin\Release\net8.0-windows\win-x64\MonkeyTV.RasDialProof.exe'
$web = Join-Path (Split-Path $root -Parent) 'MonkeyTV.VpnProxyProof\bin\Release\net8.0-windows10.0.17763.0\win-x64\MonkeyTV.VpnProxyProof.exe'
$fixedRuntime = Join-Path $repo 'Runtime\WebView2Fixed'
$profile = 'MonkeyTV-v2-E2E-' + [guid]::NewGuid().ToString('N')
$prefix = 'Local\MonkeyTV-v2-E2E-' + [guid]::NewGuid().ToString('N')
$runId = [guid]::NewGuid().ToString('N')
$dialLog = Join-Path $root "e2e-dial-$runId.txt"
$gatewayLog = Join-Path $root "e2e-gateway-$runId.txt"
$resultPath = Join-Path $root 'end-to-end-result.json'
$webResult = Join-Path (Split-Path $web -Parent) 'Data\ProxyProof\end-to-end-results.txt'
$certPath = Join-Path $env:TEMP "monkeytv-nord-e2e-$runId.der"
$thumb = 'B0A21991007734F5E80C977DD295FFEFB5AD6229'
$certInstalled = $false; $profileCreated = $false
$dialProcess = $null; $gatewayProcess = $null; $webProcess = $null
$report = [ordered]@{ StartedUtc=[DateTime]::UtcNow.ToString('o'); Profile=$profile; Server=$ServerName; NordVpnConnection=$false; RouteGatewayThroughNord=$false; FixedWebViewThroughGateway=$false; CanadianExit=$false; UnrelatedDotNetDirect=$false; EvergreenDirect=$false; Containment=$false; TunnelLossFailClosed=$false; Reconnect=$false; HomeSameLocationReuse=$false; Cleanup=$false; Error=$null }

function Wait-Event([string]$Name, [int]$Seconds) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    do {
        try { $event = [Threading.EventWaitHandle]::OpenExisting($Name); try { if ($event.WaitOne(500)) { return $true } } finally { $event.Dispose() } } catch [Threading.WaitHandleCannotBeOpenedException] {}
        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $deadline)
    return $false
}
function Set-Event([string]$Name) {
    $deadline = (Get-Date).AddSeconds(10)
    do { try { $event=[Threading.EventWaitHandle]::OpenExisting($Name); try { $event.Set() | Out-Null; return } finally { $event.Dispose() } } catch [Threading.WaitHandleCannotBeOpenedException] { Start-Sleep -Milliseconds 200 } } while ((Get-Date) -lt $deadline)
    throw "Named event not found: $Name"
}
function Wait-EitherEvent([string]$SuccessName, [string]$FailureName, [int]$Seconds) {
    $deadline=(Get-Date).AddSeconds($Seconds)
    do {
        foreach($item in @(@($SuccessName,'success'),@($FailureName,'failure'))) {
            try { $event=[Threading.EventWaitHandle]::OpenExisting($item[0]); try { if($event.WaitOne(0)){ return $item[1] } } finally { $event.Dispose() } } catch [Threading.WaitHandleCannotBeOpenedException] {}
        }
        Start-Sleep -Milliseconds 250
    } while((Get-Date)-lt $deadline)
    return 'timeout'
}
function Wait-Log([string]$Path, [string]$Pattern, [int]$Seconds) {
    $deadline=(Get-Date).AddSeconds($Seconds)
    do { if (Test-Path $Path) { $text=Get-Content -Raw $Path; if ($text -match $Pattern) { return $Matches } }; Start-Sleep -Milliseconds 250 } while ((Get-Date) -lt $deadline)
    throw "Timed out waiting for $Pattern in $Path"
}

try {
    if ((Get-ItemPropertyValue 'HKLM:\SYSTEM\CurrentControlSet\Services\RasMan\Parameters' -Name NegotiateDH2048_AES256) -ne 2) { throw 'Required Nord IKEv2 policy is not active.' }
    foreach ($path in @($gateway,$dial,$web,(Join-Path $fixedRuntime 'msedgewebview2.exe'))) { if (-not (Test-Path $path -PathType Leaf)) { throw "Missing proof dependency: $path" } }
    Invoke-WebRequest -UseBasicParsing -Uri 'https://downloads.nordcdn.com/certificates/root.der' -OutFile $certPath
    $certificate=[Security.Cryptography.X509Certificates.X509Certificate2]::new($certPath)
    if ($certificate.Thumbprint -ne $thumb -or $certificate.Subject -notlike '*NordVPN Root CA*') { throw 'Nord certificate validation failed.' }
    if (-not (Get-ChildItem Cert:\LocalMachine\Root | Where-Object Thumbprint -eq $thumb)) { Import-Certificate $certPath -CertStoreLocation Cert:\LocalMachine\Root | Out-Null; $certInstalled=$true }

    & $runner -ServerName $ServerName -ProfileName $profile -KeepProfile | Out-Host
    $created=Get-Content (Join-Path $root 'vpnv2-provisioning-result.json') -Raw | ConvertFrom-Json
    if (-not ($created.Created -and $created.Inspected -and $created.AppPathPresent -and $created.TrafficFilterPresent)) { throw 'VPNv2 profile creation failed.' }
    $profileCreated=$true

    $dialProcess=Start-Process $dial -ArgumentList @($profile,"--cycle-prefix=$prefix") -RedirectStandardOutput $dialLog -RedirectStandardError ($dialLog+'.err') -PassThru
    if (-not (Wait-Event ($prefix+'-Connected1') 180)) { throw 'Initial Nord connection did not complete.' }
    $report.NordVpnConnection=$true
    $vpnIpv4 = @(Get-NetIPAddress -InterfaceAlias $profile -AddressFamily IPv4 -ErrorAction Stop | Where-Object { $_.IPAddress -notlike '169.254.*' } | Select-Object -ExpandProperty IPAddress -First 1)
    if ($vpnIpv4.Count -ne 1) { throw 'Could not identify the active Nord VPN interface IPv4 address.' }

    $gatewayProcess=Start-Process $gateway -ArgumentList @('--proxy',"--finish-event=$prefix-GatewayFinish") -RedirectStandardOutput $gatewayLog -RedirectStandardError ($gatewayLog+'.err') -PassThru
    $portMatch=Wait-Log $gatewayLog 'GATEWAY_PORT=(\d+)' 30
    $port=[int]$portMatch[1]

    $env:MONKEYTV_E2E_PROXY_PORT=[string]$port
    $env:MONKEYTV_E2E_EVENT_PREFIX=$prefix
    $env:MONKEYTV_FIXED_RUNTIME_PATH=$fixedRuntime
    $env:MONKEYTV_E2E_GATEWAY_PATH=$gateway
    $webProcess=Start-Process $web -PassThru
    $webStage=Wait-EitherEvent ($prefix+'-ConnectedTestsDone') ($prefix+'-HarnessFailed') 180
    if($webStage -eq 'failure'){throw 'Fixed WebView2 connected-stage harness failed.'}
    if($webStage -ne 'success'){throw 'Timed out waiting for connected-stage WebView2 evidence.'}
    $connectedText=Get-Content -Raw $webResult
    $gatewayText=Get-Content -Raw $gatewayLog
    $report.RouteGatewayThroughNord=$gatewayText -match ('GATEWAY_UPSTREAM local=' + [regex]::Escape($vpnIpv4[0]) + '\s') -and $gatewayText -notmatch 'GATEWAY_UPSTREAM local=[0-9a-f]*:'
    $report.FixedWebViewThroughGateway=$connectedText -match 'PASS: Fixed WebView2 observes a Canadian exit through RouteGateway'
    $report.CanadianExit=$report.FixedWebViewThroughGateway
    $report.UnrelatedDotNetDirect=$connectedText -match 'PASS: Unrelated \.NET client remains on a different direct public route'
    $report.EvergreenDirect=$connectedText -match 'PASS: Shared Evergreen WebView2 remains on the normal direct public route'
    $report.Containment=($connectedText -match 'PASS: Direct Fixed WebView2 IPv4 fails') -and ($connectedText -match 'PASS: Direct Fixed WebView2 IPv6 fails') -and ($connectedText -match 'PASS: Direct hostname navigation from Fixed WebView2 fails') -and -not ($connectedText -match 'FAILED: WebRTC emitted')

    Set-Event ($prefix+'-Disconnect')
    if (-not (Wait-Event ($prefix+'-Disconnected') 30)) { throw 'Controlled tunnel disconnect did not complete.' }
    Set-Event ($prefix+'-TunnelDown')
    if (-not (Wait-Event ($prefix+'-FailClosedDone') 90)) { throw 'Fail-closed stage did not complete.' }
    $downText=Get-Content -Raw $webResult -ErrorAction SilentlyContinue
    # The final result file is written on harness exit; current evidence is visible in the process UI only.

    Set-Event ($prefix+'-Reconnect')
    if (-not (Wait-Event ($prefix+'-Connected2') 180)) {
        if (Wait-Event ($prefix+'-ReconnectFailed') 1) { throw 'Nord reconnect failed.' }
        throw 'Nord reconnect timed out.'
    }
    $report.Reconnect=$true
    Set-Event ($prefix+'-TunnelUp')
    if (-not (Wait-Event ($prefix+'-AllDone') 180)) { throw 'Post-reconnect WebView2 stage did not complete.' }
    if ($webProcess) { $webProcess.WaitForExit(30000) | Out-Null }
    $finalText=Get-Content -Raw $webResult
    $report.TunnelLossFailClosed=$finalText -match 'PASS: RouteGateway stops forwarding after tunnel loss'
    $report.HomeSameLocationReuse=$finalText -match 'PASS: Nord to Home to same Nord location reuses the active route'
    $report.FixedWebViewThroughGateway=$report.FixedWebViewThroughGateway -and ($finalText -match 'PASS: Forwarding resumes through Canada only after reconnect verification')
}
catch { $report.Error=$_.Exception.ToString() }
finally {
    foreach ($name in @($prefix+'-Finish',$prefix+'-GatewayFinish',$prefix+'-TunnelDown',$prefix+'-TunnelUp')) { try { Set-Event $name } catch {} }
    foreach ($process in @($webProcess,$gatewayProcess,$dialProcess)) { if ($process -and -not $process.HasExited) { if (-not $process.WaitForExit(15000)) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue } } }
    if ($profileCreated) { try { & $runner -ProfileName $profile -RemoveOnly | Out-Host } catch { $report.Error=(($report.Error,$_.Exception.ToString()) -ne $null)-join "`nCLEANUP: " } }
    if ($certInstalled) { Get-ChildItem Cert:\LocalMachine\Root | Where-Object Thumbprint -eq $thumb | Remove-Item -Force }
    if (Test-Path $certPath) { Remove-Item $certPath -Force }
    Get-ScheduledTask -TaskName 'MonkeyTV-v2-VPNv2-Spike-*' -ErrorAction SilentlyContinue | Unregister-ScheduledTask -Confirm:$false
    $profileGone=-not [bool](Get-VpnConnection -Name $profile -ErrorAction SilentlyContinue)
    $certGone=if($certInstalled){-not [bool](Get-ChildItem Cert:\LocalMachine\Root | Where-Object Thumbprint -eq $thumb)}else{$true}
    $report.Cleanup=$profileGone -and $certGone -and -not [bool](Get-ScheduledTask -TaskName 'MonkeyTV-v2-VPNv2-Spike-*' -ErrorAction SilentlyContinue)
    $report.CompletedUtc=[DateTime]::UtcNow.ToString('o')
    $report | ConvertTo-Json -Depth 6 | Set-Content $resultPath -Encoding utf8
}

$required=@('NordVpnConnection','RouteGatewayThroughNord','FixedWebViewThroughGateway','CanadianExit','UnrelatedDotNetDirect','EvergreenDirect','Containment','TunnelLossFailClosed','Reconnect','HomeSameLocationReuse','Cleanup')
if (@($required | Where-Object {-not $report[$_]}).Count -eq 0) { exit 0 }
exit 1
