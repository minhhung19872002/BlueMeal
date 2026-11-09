# Creates deploy/.env from deploy/.env.example with freshly generated random secrets (never overwrites).
# Usage: .\scripts\gen-secrets.ps1 [-Dev]
param([switch]$Dev, [switch]$Force)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$envFile = Join-Path $root "deploy\.env"
if ((Test-Path $envFile) -and -not $Force) { Write-Host "deploy/.env already exists - kept." -ForegroundColor Yellow; exit 0 }

function New-Secret([int]$bytes = 32) {
    $b = New-Object byte[] $bytes
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($b)
    return [Convert]::ToBase64String($b)
}
function New-Password([int]$len = 24) {
    $chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789"
    $b = New-Object byte[] $len
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($b)
    return -join ($b | ForEach-Object { $chars[$_ % $chars.Length] })
}

$content = Get-Content (Join-Path $root "deploy\.env.example") -Raw
$map = @{
    "POSTGRES_PASSWORD=change-me-postgres"                         = "POSTGRES_PASSWORD=$(New-Password)"
    "BLUEMEAL_OWNER_PASSWORD=change-me-owner"                      = "BLUEMEAL_OWNER_PASSWORD=$(New-Password)"
    "BLUEMEAL_APP_PASSWORD=change-me-app"                          = "BLUEMEAL_APP_PASSWORD=$(New-Password)"
    "REDIS_PASSWORD=change-me-redis"                               = "REDIS_PASSWORD=$(New-Password)"
    "BLUEMEAL_JWT_KEY=change-me-to-a-long-random-string-of-64-chars" = "BLUEMEAL_JWT_KEY=$(New-Password 64)"
    "BLUEMEAL_ENC_KEYS=k1:REPLACE_WITH_BASE64_32_BYTES"            = "BLUEMEAL_ENC_KEYS=k1:$(New-Secret)"
    "BLUEMEAL_HMAC_KEY=REPLACE_WITH_BASE64_32_BYTES"               = "BLUEMEAL_HMAC_KEY=$(New-Secret)"
    "BACKUP_PASSPHRASE=change-me-backup-passphrase"                = "BACKUP_PASSPHRASE=$(New-Password 40)"
}
foreach ($k in $map.Keys) { $content = $content.Replace($k, $map[$k]) }
if ($Dev) {
    $content = $content.Replace("ASPNETCORE_ENVIRONMENT=Production", "ASPNETCORE_ENVIRONMENT=Development")
    $content = $content.Replace("BLUEMEAL_PUBLIC_URL=https://bluemeal.example.vn", "BLUEMEAL_PUBLIC_URL=http://localhost:8080")
    $content = $content.Replace("TLS_REDIRECT=on", "TLS_REDIRECT=off")
    $content = $content.Replace("AUTH_COOKIE_SECURE=true", "AUTH_COOKIE_SECURE=false")
    $content = $content.Replace("BLUEMEAL_ADMIN_PASSWORD=ChangeMe@2026!", "BLUEMEAL_ADMIN_PASSWORD=BlueMeal@2026!")
} else {
    # production: never keep the published default — random password meeting the policy (upper, lower, digit, special)
    $adminPassword = "Bm@" + (New-Password 16) + "9a"
    $content = $content.Replace("BLUEMEAL_ADMIN_PASSWORD=ChangeMe@2026!", "BLUEMEAL_ADMIN_PASSWORD=$adminPassword")
}
[System.IO.File]::WriteAllText($envFile, $content.Replace("`r`n", "`n"))
Write-Host "Created deploy/.env with random secrets." -ForegroundColor Green
if (-not $Dev) { Write-Host "Initial admin password (change it and enable MFA at first login): $adminPassword" -ForegroundColor Yellow }
