param(
    [string]$ServerName = 'invalid.monkeytv.invalid',
    [string]$ProfileName,
    [switch]$KeepProfile,
    [switch]$RemoveOnly
)
$ErrorActionPreference = 'Stop'
$taskName = 'MonkeyTV-v2-VPNv2-Spike-' + [guid]::NewGuid().ToString('N')
$scriptPath = Join-Path $PSScriptRoot 'Invoke-Vpnv2ProvisioningSystem.ps1'
$gatewayPath = Join-Path $PSScriptRoot 'bin\Release\net8.0-windows\win-x64\MonkeyTV.RouteGateway.exe'
$resultPath = Join-Path $PSScriptRoot 'vpnv2-provisioning-result.json'
$userSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
if ([string]::IsNullOrWhiteSpace($ProfileName)) { $ProfileName = 'MonkeyTV-v2-Spike-' + [guid]::NewGuid().ToString('N') }

if (-not (Test-Path -LiteralPath $gatewayPath -PathType Leaf)) { throw "Gateway proof executable missing: $gatewayPath" }
if (Test-Path -LiteralPath $resultPath) { Remove-Item -LiteralPath $resultPath -Force }

$quotedArguments = "-NoProfile -ExecutionPolicy Bypass -File `"$scriptPath`" -GatewayPath `"$gatewayPath`" -ResultPath `"$resultPath`" -UserSid `"$userSid`" -ProfileName `"$ProfileName`" -ServerName `"$ServerName`""
if ($KeepProfile) { $quotedArguments += ' -KeepProfile' }
if ($RemoveOnly) { $quotedArguments += ' -RemoveOnly' }
$action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument $quotedArguments
$principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Minutes 3) -DeleteExpiredTaskAfter (New-TimeSpan -Minutes 5)
$trigger = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1)
$trigger.EndBoundary = (Get-Date).AddMinutes(10).ToString('s')

try {
    Register-ScheduledTask -TaskName $taskName -Action $action -Principal $principal -Settings $settings -Trigger $trigger | Out-Null
    Start-ScheduledTask -TaskName $taskName
    $deadline = (Get-Date).AddMinutes(3)
    do {
        Start-Sleep -Milliseconds 500
        $task = Get-ScheduledTask -TaskName $taskName -ErrorAction Stop
    } while ($task.State -eq 'Running' -and (Get-Date) -lt $deadline)
    if ($task.State -eq 'Running') { Stop-ScheduledTask -TaskName $taskName; throw 'LocalSystem provisioning task timed out.' }
    $info = Get-ScheduledTaskInfo -TaskName $taskName
    if (-not (Test-Path -LiteralPath $resultPath)) { throw "LocalSystem proof produced no result; task result: $($info.LastTaskResult)" }
    Get-Content -LiteralPath $resultPath
}
finally {
    if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) {
        Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
    }
}
