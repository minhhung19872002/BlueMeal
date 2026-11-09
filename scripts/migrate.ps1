# Apply database migrations (as bluemeal_owner) without starting the API.
# Usage: .\scripts\migrate.ps1 [-Prod]
param([switch]$Prod)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$overlay = if ($Prod) { "deploy\docker-compose.prod.yml" } else { "deploy\docker-compose.dev.yml" }
docker compose --env-file deploy\.env -f deploy\docker-compose.yml -f $overlay run --rm --no-deps -e Database__Seed=false api --migrate-only
if ($LASTEXITCODE -ne 0) { throw "Migration failed" }
Write-Host "Migrations applied." -ForegroundColor Green
