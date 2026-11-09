#!/bin/sh
# BlueMeal — automated restore drill (SPEC §10.7): backup → restore into a temporary database → compare row counts.
# Usage: scripts/test-backup-restore.sh [--prod]   (default: development compose files)
set -eu
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
OVERLAY="deploy/docker-compose.dev.yml"; [ "${1:-}" = "--prod" ] && OVERLAY="deploy/docker-compose.prod.yml"
COMPOSE="docker compose --env-file deploy/.env -f deploy/docker-compose.yml -f $OVERLAY"
ID="drill-$(date +%Y%m%d%H%M%S)"
TARGET="bluemeal_restore_test"
REPORT="docs/test-reports/restore-drill.txt"
mkdir -p docs/test-reports

echo "==> 1/4 backup ($ID)"
$COMPOSE exec -T backup su-exec postgres sh -c ". /tmp/backup.env && /usr/local/bin/backup.sh Manual $ID"
FILE=$($COMPOSE exec -T backup sh -c "sed -n 's/.*\"file\":\"\([^\"]*\)\".*/\1/p' /backups/manifests/$ID.json")
[ -n "$FILE" ] || { echo "backup manifest has no file" >&2; exit 1; }
echo "    archive: $FILE"

echo "==> 2/4 restore into $TARGET"
$COMPOSE exec -T backup su-exec postgres sh -c ". /tmp/backup.env && psql -h \$PGHOST -U \$PGUSER -d postgres -c 'DROP DATABASE IF EXISTS $TARGET' >/dev/null && /usr/local/bin/restore.sh $FILE $TARGET"

echo "==> 3/4 compare row counts"
TABLES="app.food app.food_nutrient app.ingredient app.dish app.recipe app.recipe_item app.menu app.menu_item app.stock_ledger app.stock_balance app.app_user app.audit_log"
COUNT_SQL=""
for t in $TABLES; do COUNT_SQL="$COUNT_SQL SELECT '$t', count(*) FROM $t UNION ALL"; done
COUNT_SQL="${COUNT_SQL% UNION ALL} ORDER BY 1;"
SRC=$($COMPOSE exec -T backup su-exec postgres sh -c ". /tmp/backup.env && psql -h \$PGHOST -U \$PGUSER -d \$PGDATABASE -tA -F' ' -c \"$COUNT_SQL\"")
DST=$($COMPOSE exec -T backup su-exec postgres sh -c ". /tmp/backup.env && psql -h \$PGHOST -U \$PGUSER -d $TARGET -tA -F' ' -c \"$COUNT_SQL\"")
{
  echo "BlueMeal restore drill $(date -Iseconds) — archive $FILE"
  echo "table source restored"
  echo "$SRC" | while read -r t n; do m=$(echo "$DST" | awk -v t="$t" '$1==t {print $2}'); echo "$t $n $m"; done
} | tee "$REPORT"

echo "==> 4/4 cleanup"
$COMPOSE exec -T backup su-exec postgres sh -c ". /tmp/backup.env && psql -h \$PGHOST -U \$PGUSER -d postgres -c 'DROP DATABASE IF EXISTS $TARGET'" >/dev/null

# audit_log keeps growing between the two counts only if the app writes during the drill; all other tables must match exactly
MISMATCH=$(awk 'NR>2 && $1!="app.audit_log" && $2!=$3' "$REPORT" | wc -l)
if [ "$MISMATCH" -ne 0 ]; then echo "RESTORE DRILL FAILED: $MISMATCH table(s) differ" >&2; exit 1; fi
echo "RESTORE DRILL PASSED (report: $REPORT)"
