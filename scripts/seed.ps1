# Seed reference data (permissions, roles, catalogs) and optionally SAMPLE data, then exit.
# Usage: .\scripts\seed.ps1 [-NoSample] [-Prod]
param([switch]$NoSample, [switch]$Prod)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$overlay = if ($Prod) { "deploy\docker-compose.prod.yml" } else { "deploy\docker-compose.dev.yml" }
$sample = if ($NoSample) { "false" } else { "true" }
docker compose --env-file deploy\.env -f deploy\docker-compose.yml -f $overlay run --rm --no-deps -e Seed__SampleData=$sample api --seed-only
if ($LASTEXITCODE -ne 0) { throw "Seeding failed" }
Write-Host "Seed completed." -ForegroundColor Green
