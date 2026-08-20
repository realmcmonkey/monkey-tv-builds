param([Parameter(Mandatory)][string]$ServerName)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$control = Join-Path $root 'Run-NordIkev2ConventionalControl.ps1'
$output = Join-Path $root 'nord-traced-control-events.json'
$channels = @(
    'Microsoft-Windows-RasAgileVpn/Operational',
    'Microsoft-Windows-RRAS/Operational',
    'Microsoft-Windows-IKEDBG/Debug'
)
$original = @{}
$started = Get-Date
$controlExit = $null
$errorText = $null

function Get-ChannelEnabled([string]$Name) {
    $line = (& wevtutil.exe gl $Name 2>$null | Where-Object { $_ -match '^enabled:' } | Select-Object -First 1)
    return $line -match 'true'
}

try {
    foreach ($channel in $channels) {
        $original[$channel] = Get-ChannelEnabled $channel
        if (-not $original[$channel]) { & wevtutil.exe sl $channel /e:true /q:true }
    }
    & $control -ServerName $ServerName
    $controlExit = $LASTEXITCODE
}
catch { $errorText = $_.Exception.ToString() }
finally {
    Start-Sleep -Seconds 2
    $logs = @('Application','Microsoft-Windows-IKE/Operational','Microsoft-Windows-EapHost/Operational','Microsoft-Windows-EapMethods-RasChap/Operational') + $channels
    $events = foreach ($log in $logs | Select-Object -Unique) {
        try {
            Get-WinEvent -FilterHashtable @{LogName=$log; StartTime=$started} -ErrorAction Stop | ForEach-Object {
                $message = [string]$_.Message
                $message = $message -replace '(?im)(Dial-in User\s*=\s*)[^\r\n]+','$1[redacted]'
                $message = $message -replace '(?im)((?:UserName|Username|Identity)\s*[=:]\s*)[^\r\n]+','$1[redacted]'
                $message = $message -replace '(?im)(Authentication succeeded for user\s+)[^,\r\n]+','$1[redacted]'
                [ordered]@{ Log=$log; TimeCreated=$_.TimeCreated.ToUniversalTime().ToString('o'); Provider=$_.ProviderName; Id=$_.Id; Level=$_.LevelDisplayName; Message=$message }
            }
        } catch {}
    }
    foreach ($channel in $channels) {
        if ($original.ContainsKey($channel) -and -not $original[$channel]) { & wevtutil.exe sl $channel /e:false /q:true }
    }
    [ordered]@{ StartedUtc=$started.ToUniversalTime().ToString('o'); Server=$ServerName; ControlExitCode=$controlExit; Error=$errorText; OriginalChannelState=$original; ChannelStateRestored=(@($channels | Where-Object { (Get-ChannelEnabled $_) -ne $original[$_] }).Count -eq 0); Events=$events } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $output -Encoding utf8
}

if ($controlExit -eq 0 -and -not $errorText) { exit 0 }
exit 1
