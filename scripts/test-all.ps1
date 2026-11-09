# BlueMeal CI script (Windows): build + unit/integration tests + dependency scan + frontend lint/i18n/tests.
# Results are written to docs/test-reports/. Exit code != 0 when any step fails.
# Usage: .\scripts\test-all.ps1 [-SkipIntegration] [-SkipFrontend]
param([switch]$SkipIntegration, [switch]$SkipFrontend)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$reports = Join-Path $root "docs\test-reports"
New-Item -ItemType Directory -Force $reports | Out-Null
$failed = @()

function Step([string]$name, [scriptblock]$body) {
    Write-Host "==> $name" -ForegroundColor Cyan
    & $body
    if ($LASTEXITCODE -ne 0) { Write-Host "FAILED: $name" -ForegroundColor Red; $script:failed += $name } else { Write-Host "OK: $name" -ForegroundColor Green }
}

Push-Location (Join-Path $root "backend")
try {
    Step "backend build" { dotnet build BlueMeal.sln -c Release -warnaserror:false }
    Step "backend unit tests" { dotnet test tests/BlueMeal.UnitTests -c Release --no-build --logger "trx;LogFileName=unit.trx" --results-directory "$reports\backend" --collect:"XPlat Code Coverage" }
    if (-not $SkipIntegration) {
        Step "backend integration tests (Testcontainers)" { dotnet test tests/BlueMeal.IntegrationTests -c Release --no-build --logger "trx;LogFileName=integration.trx" --results-directory "$reports\backend" }
    }
    Step "nuget vulnerability scan" {
        $out = dotnet list BlueMeal.sln package --vulnerable --include-transitive 2>&1 | Out-String
        $out | Set-Content "$reports\nuget-vulnerable.txt"
        if ($out -match "has the following vulnerable packages") { $global:LASTEXITCODE = 1 } else { $global:LASTEXITCODE = 0 }
    }
} finally { Pop-Location }

if (-not $SkipFrontend) {
    Push-Location (Join-Path $root "frontend")
    try {
        if (-not (Test-Path node_modules)) { Step "npm ci" { npm ci --no-audit --no-fund } }
        Step "frontend i18n key check" { npm run -s i18n:check }
        Step "frontend lint (no hard-coded strings)" { npx eslint . }
        Step "frontend typecheck" { npx tsc -b }
        Step "frontend unit tests" { npx vitest run --reporter=default --reporter=junit --outputFile="$reports\frontend-junit.xml" }
        Step "npm audit (production deps)" { npm audit --omit=dev --audit-level=moderate }
    } finally { Pop-Location }
}

if ($failed.Count -gt 0) { Write-Host "`n$($failed.Count) step(s) failed: $($failed -join ', ')" -ForegroundColor Red; exit 1 }
Write-Host "`nAll checks passed." -ForegroundColor Green
