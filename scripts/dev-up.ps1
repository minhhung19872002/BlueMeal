# BlueMeal — start the full development stack on Windows (Docker Desktop).
# build -> up -> migrate + seed (automatic at API start-up) -> print URLs and the default admin account.
param([switch]$NoBuild)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

& "$PSScriptRoot\gen-secrets.ps1" -Dev

$env:GIT_COMMIT = (git rev-parse --short HEAD 2>$null)
if (-not $env:GIT_COMMIT) { $env:GIT_COMMIT = "dev" }
$env:BUILD_TIME = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")

$compose = @("--env-file", "deploy\.env", "-f", "deploy\docker-compose.yml", "-f", "deploy\docker-compose.dev.yml")
$upArgs = @("up", "-d")
if (-not $NoBuild) { $upArgs += "--build" }
Write-Host "==> docker compose $($upArgs -join ' ')" -ForegroundColor Cyan
docker compose @compose @upArgs
if ($LASTEXITCODE -ne 0) { throw "docker compose up failed" }

Write-Host "==> Waiting for API readiness (migrations + seed run at start-up)..." -ForegroundColor Cyan
$ok = $false
for ($i = 0; $i -lt 90; $i++) {
    try {
        $r = Invoke-WebRequest -Uri "http://localhost:8080/health/ready" -UseBasicParsing -TimeoutSec 5
        if ($r.StatusCode -eq 200) { $ok = $true; break }
    } catch { }
    Start-Sleep -Seconds 3
}
if (-not $ok) { docker compose @compose logs --tail 80 api; throw "API did not become ready in time" }

$adminPwd = (Select-String -Path "deploy\.env" -Pattern "^BLUEMEAL_ADMIN_PASSWORD=(.*)$").Matches[0].Groups[1].Value
Write-Host ""
Write-Host "BlueMeal is running" -ForegroundColor Green
Write-Host "  Web (WAF + SPA) : http://localhost:8080   (https://localhost:8443, self-signed)"
Write-Host "  API             : http://localhost:5080/swagger"
Write-Host "  Mailpit (email) : http://localhost:8025"
Write-Host "  Admin account   : admin / $adminPwd"
Write-Host "  -> you must change the password and enable MFA (authenticator app) at first login." -ForegroundColor Yellow
