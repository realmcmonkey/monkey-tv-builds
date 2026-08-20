$ErrorActionPreference = 'Stop'
$thumbprint = 'B0A21991007734F5E80C977DD295FFEFB5AD6229'
Get-ChildItem Cert:\LocalMachine\Root | Where-Object Thumbprint -eq $thumbprint | Remove-Item -Force
$tempFile = 'C:\Users\jones\AppData\Local\Temp\monkeytv-nord-e2e-eb40092f195f45988faa7100e7344e9d.der'
if (Test-Path -LiteralPath $tempFile) { Remove-Item -LiteralPath $tempFile -Force }
foreach ($processId in 24916,36916) {
    Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
}
