# Restore a BlueMeal backup into a target database.
# Usage: .\restore.ps1 -Archive bluemeal-20260101-010000-scheduled.tar.enc [-TargetDb bluemeal] [-WithFiles]
param(
    [Parameter(Mandatory = $true)][string]$Archive,
    [string]$TargetDb = "bluemeal",
    [switch]$WithFiles
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$compose = @("-f", "$root\docker-compose.yml")
if (Test-Path "$root\docker-compose.prod.yml") { $compose += @("-f", "$root\docker-compose.prod.yml") }
$extra = if ($WithFiles) { "--with-files" } else { "" }
docker compose @compose exec backup /usr/local/bin/restore.sh $Archive $TargetDb $extra
if ($LASTEXITCODE -ne 0) { throw "Restore failed ($LASTEXITCODE)" }
