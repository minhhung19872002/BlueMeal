#!/bin/sh
# Apply database migrations (as bluemeal_owner) without starting the API. Usage: ./scripts/migrate.sh [--prod]
set -eu
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
OVERLAY=deploy/docker-compose.dev.yml; [ "${1:-}" = "--prod" ] && OVERLAY=deploy/docker-compose.prod.yml
docker compose --env-file deploy/.env -f deploy/docker-compose.yml -f "$OVERLAY" run --rm --no-deps -e Database__Seed=false api --migrate-only
echo "Migrations applied."
