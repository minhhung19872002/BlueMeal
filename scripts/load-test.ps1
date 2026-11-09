# BlueMeal load test (SPEC §11): 2 API replicas, k6 ramp to -Vus (default 1000) held for -Hold (default 10m).
# Usage: scripts\load-test.ps1 [-Vus 1000] [-Hold 10m] [-Ramp 3m]     smoke: scripts\load-test.ps1 -Vus 50 -Hold 1m
# Report: docs/test-reports/k6-load-test.html (+ k6-summary.json). Load-test accounts are removed at the end.
param([int]$Vus = 1000, [string]$Hold = '10m', [string]$Ramp = '3m')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$compose = @('compose', '--env-file', 'deploy/.env', '-f', 'deploy/docker-compose.yml', '-f', 'deploy/docker-compose.dev.yml', '-f', 'deploy/docker-compose.perf.yml')
$bytes = New-Object byte[] 12; [System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
$password = 'Lt-' + ([BitConverter]::ToString($bytes) -replace '-', '').ToLower() + '!A9'
New-Item -ItemType Directory -Force docs/test-reports | Out-Null

Write-Host '==> 1/4 start stack with 2 API replicas (perf overlay)'
docker @compose up -d --no-build
for ($i = 0; $i -lt 60; $i++) { try { Invoke-WebRequest -UseBasicParsing http://localhost:8080/health/ready | Out-Null; break } catch { Start-Sleep 3 } }

try {
  Write-Host "==> 2/4 seed $Vus load-test accounts"
  docker @compose run --rm --no-deps -e "BLUEMEAL_LOADTEST_PASSWORD=$password" api "--seed-loadtest=$Vus"
  if ($LASTEXITCODE -ne 0) { throw 'seeding failed' }

  Write-Host "==> 3/4 k6: $Vus VUs, ramp $Ramp, hold $Hold"
  docker run --rm --network bluemeal_frontend -v "${root}/tools/k6:/scripts:ro" -v "${root}/docs/test-reports:/out" `
    -e BASE_URL=http://web:8080 -e "LOADTEST_PASSWORD=$password" -e "USERS=$Vus" -e "VUS=$Vus" -e "HOLD=$Hold" -e "RAMP=$Ramp" `
    -e SUMMARY_JSON=/out/k6-summary.json -e K6_WEB_DASHBOARD=true -e K6_WEB_DASHBOARD_EXPORT=/out/k6-load-test.html `
    grafana/k6:latest run /scripts/load-test.js
  $k6Exit = $LASTEXITCODE
} finally {
  Write-Host '==> 4/4 remove load-test accounts and return to the normal dev stack'
  docker @compose run --rm --no-deps api --seed-loadtest=0
  docker compose --env-file deploy/.env -f deploy/docker-compose.yml -f deploy/docker-compose.dev.yml up -d --no-build
}
if ($k6Exit -ne 0) { throw "k6 thresholds failed (exit $k6Exit) — see docs/test-reports/k6-load-test.html" }
