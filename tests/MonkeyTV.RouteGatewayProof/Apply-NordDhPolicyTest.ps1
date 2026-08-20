$ErrorActionPreference = 'Stop'
$path = 'HKLM:\SYSTEM\CurrentControlSet\Services\RasMan\Parameters'
New-ItemProperty -LiteralPath $path -Name 'NegotiateDH2048_AES256' -PropertyType DWord -Value 2 -Force | Out-Null
$key = Get-Item -LiteralPath $path
$value = Get-ItemPropertyValue -LiteralPath $path -Name 'NegotiateDH2048_AES256'
if ($key.GetValueKind('NegotiateDH2048_AES256') -ne [Microsoft.Win32.RegistryValueKind]::DWord -or $value -ne 2) {
    throw 'Registry verification failed.'
}
