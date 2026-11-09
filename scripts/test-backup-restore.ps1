# BlueMeal — automated restore drill (SPEC §10.7): backup -> restore into a temporary database -> compare row counts.
# Usage: scripts\test-backup-restore.ps1 [-Prod]
param([switch]$Prod)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$overlay = if ($Prod) { 'deploy/docker-compose.prod.yml' } else { 'deploy/docker-compose.dev.yml' }
$compose = @('compose', '--env-file', 'deploy/.env', '-f', 'deploy/docker-compose.yml', '-f', $overlay)
$id = 'drill-' + (Get-Date -Format 'yyyyMMddHHmmss')
$target = 'bluemeal_restore_test'
$report = 'docs/test-reports/restore-drill.txt'
New-Item -ItemType Directory -Force docs/test-reports | Out-Null

function Exec([string]$cmd) {
  $out = & docker @compose exec -T backup su-exec postgres sh -c ". /tmp/backup.env && $cmd"
  if ($LASTEXITCODE -ne 0) { throw "command failed: $cmd" }
  return $out
}

Write-Host "==> 1/4 backup ($id)"
Exec "/usr/local/bin/backup.sh Manual $id" | Out-Host
$manifest = (& docker @compose exec -T backup cat "/backups/manifests/$id.json") -join ''
$file = ($manifest | ConvertFrom-Json).file
if (-not $file) { throw 'backup manifest has no file' }
Write-Host "    archive: $file"

Write-Host "==> 2/4 restore into $target"
Exec "psql -h `$PGHOST -U `$PGUSER -d postgres -c 'DROP DATABASE IF EXISTS $target' >/dev/null && /usr/local/bin/restore.sh $file $target" | Out-Host

Write-Host '==> 3/4 compare row counts'
$tables = 'app.food app.food_nutrient app.ingredient app.dish app.recipe app.recipe_item app.menu app.menu_item app.stock_ledger app.stock_balance app.app_user app.audit_log'.Split(' ')
$sql = (($tables | ForEach-Object { "SELECT '$_', count(*) FROM $_" }) -join ' UNION ALL ') + ' ORDER BY 1;'
$src = Exec "psql -h `$PGHOST -U `$PGUSER -d `$PGDATABASE -tA -F' ' -c `"$sql`""
$dst = Exec "psql -h `$PGHOST -U `$PGUSER -d $target -tA -F' ' -c `"$sql`""
$dstMap = @{}; foreach ($l in $dst) { $p = $l.Split(' '); if ($p.Length -eq 2) { $dstMap[$p[0]] = $p[1] } }
$lines = @("BlueMeal restore drill $(Get-Date -Format o) - archive $file", 'table source restored')
$mismatch = 0
foreach ($l in $src) {
  $p = $l.Split(' '); if ($p.Length -ne 2) { continue }
  $lines += "$($p[0]) $($p[1]) $($dstMap[$p[0]])"
  if ($p[0] -ne 'app.audit_log' -and $p[1] -ne $dstMap[$p[0]]) { $mismatch++ }
}
$lines | Set-Content -Encoding utf8 $report
$lines | Out-Host

Write-Host '==> 4/4 cleanup'
Exec "psql -h `$PGHOST -U `$PGUSER -d postgres -c 'DROP DATABASE IF EXISTS $target'" | Out-Null
if ($mismatch -ne 0) { throw "RESTORE DRILL FAILED: $mismatch table(s) differ" }
Write-Host "RESTORE DRILL PASSED (report: $report)"
