# Offline deployment: load images exported by save-images.ps1 and verify the checksum.
# Usage: .\scripts\load-images.ps1 [-Archive offline-images\bluemeal-images-1.0.0.tar]
param([string]$Archive = "")
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (-not $Archive) { $Archive = (Get-ChildItem -Path $root -Recurse -Filter "bluemeal-images-*.tar" | Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName }
if (-not $Archive -or -not (Test-Path $Archive)) { throw "Image archive not found" }
if (Test-Path "$Archive.sha256") {
    $expected = (Get-Content "$Archive.sha256").Split(" ")[0]
    $actual = (Get-FileHash $Archive -Algorithm SHA256).Hash.ToLower()
    if ($expected -ne $actual) { throw "Checksum mismatch for $Archive" }
    Write-Host "Checksum OK" -ForegroundColor Green
}
docker load -i $Archive
if ($LASTEXITCODE -ne 0) { throw "docker load failed" }
Write-Host "Images loaded. Next: create deploy/.env (scripts/gen-secrets.ps1), then: docker compose --env-file deploy/.env -f deploy/docker-compose.yml -f deploy/docker-compose.prod.yml up -d" -ForegroundColor Green
