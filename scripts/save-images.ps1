# Offline deployment (SPEC §12): build images and export them (with third-party base images) to a tar archive.
# Usage: .\scripts\save-images.ps1 [-Version 1.0.0] [-Output offline-images]
param([string]$Version = "1.0.0", [string]$Output = "offline-images")
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$env:BLUEMEAL_VERSION = $Version
docker compose --env-file deploy\.env -f deploy\docker-compose.yml -f deploy\docker-compose.prod.yml build
if ($LASTEXITCODE -ne 0) { throw "build failed" }
$images = @("bluemeal/api:$Version", "bluemeal/web:$Version", "bluemeal/backup:$Version", "postgres:16-alpine", "redis:7.2-alpine")
foreach ($i in $images | Where-Object { $_ -notlike "bluemeal/*" }) { docker pull $i | Out-Null }
New-Item -ItemType Directory -Force $Output | Out-Null
$tar = Join-Path $Output "bluemeal-images-$Version.tar"
docker save -o $tar @images
if ($LASTEXITCODE -ne 0) { throw "docker save failed" }
Copy-Item deploy -Destination (Join-Path $Output "deploy") -Recurse -Force -Exclude ".env"
Copy-Item scripts -Destination (Join-Path $Output "scripts") -Recurse -Force
$hash = (Get-FileHash $tar -Algorithm SHA256).Hash.ToLower()
"$hash  $(Split-Path $tar -Leaf)" | Set-Content "$tar.sha256"
Write-Host "Saved $tar ($([math]::Round((Get-Item $tar).Length / 1MB)) MB). Copy the '$Output' folder to the offline server and run scripts/load-images.ps1 (or .sh)." -ForegroundColor Green
