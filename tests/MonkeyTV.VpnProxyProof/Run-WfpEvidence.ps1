$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$proofRoot = Join-Path $PSScriptRoot 'bin\Release\net8.0-windows10.0.17763.0\win-x64'
$proofExe = Join-Path $proofRoot 'MonkeyTV.VpnProxyProof.exe'
$fixedRuntime = Join-Path $projectRoot 'Runtime\WebView2Fixed'
$evidenceRoot = Join-Path $proofRoot 'Data\WfpEvidence'
$capturePrefix = Join-Path $evidenceRoot ('wfp-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))

New-Item -ItemType Directory -Force -Path $evidenceRoot | Out-Null
$captureStarted = $false
try {
    & netsh.exe wfp capture start cab=off traceonly=off keywords=none file=$capturePrefix
    if ($LASTEXITCODE -ne 0) { throw "netsh wfp capture start failed with exit code $LASTEXITCODE" }
    $captureStarted = $true
    $proofProcess = Start-Process -FilePath $proofExe -ArgumentList @("`"--fixed-runtime=$fixedRuntime`"", '--autoclose') -Wait -PassThru
    if ($proofProcess.ExitCode -ne 0) { throw "Proof harness failed with exit code $($proofProcess.ExitCode)" }
}
finally {
    if ($captureStarted) { & netsh.exe wfp capture stop }
}
