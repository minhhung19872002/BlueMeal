# OWASP ZAP baseline scan of the running stack (SPEC §10.5). Reports: docs/test-reports/zap/
param([string]$Target = 'http://web:8080', [string]$Network = 'bluemeal_frontend')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'docs/test-reports/zap'
New-Item -ItemType Directory -Force $out | Out-Null
Copy-Item (Join-Path $root 'deploy/zap/baseline-rules.tsv') (Join-Path $out 'baseline-rules.tsv') -Force
docker run --rm --network $Network -v "${out}:/zap/wrk:rw" -u root ghcr.io/zaproxy/zaproxy:stable zap-baseline.py -t $Target -c baseline-rules.tsv -r zap-baseline.html -J zap-baseline.json -I
$report = Get-Content (Join-Path $out 'zap-baseline.json') -Raw | ConvertFrom-Json
$bad = @($report.site | ForEach-Object { $_.alerts } | Where-Object { [int]$_.riskcode -ge 2 })
if ($bad.Count -gt 0) { $bad | ForEach-Object { Write-Host "$($_.riskdesc): $($_.name)" }; throw "ZAP baseline: $($bad.Count) High/Medium finding(s)" }
Write-Host 'ZAP baseline: no High/Medium findings'
