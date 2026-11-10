#!/bin/sh
# BlueMeal load test (SPEC §11): 2 API replicas, k6 ramp to VUS (default 1000) held for HOLD (default 10m).
# Usage: scripts/load-test.sh [VUS] [HOLD]      e.g. scripts/load-test.sh 50 1m  (smoke)
# Report: docs/test-reports/k6-load-test.html (+ k6-summary.json). Load-test accounts are removed at the end.
set -eu
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
VUS="${1:-1000}"; HOLD="${2:-10m}"; RAMP="${RAMP:-3m}"
MOUNT_ROOT="${MOUNT_ROOT:-$ROOT}"   # on Git-Bash for Windows pass MOUNT_ROOT="$(pwd -W)"
COMPOSE="docker compose --env-file deploy/.env -f deploy/docker-compose.yml -f deploy/docker-compose.dev.yml -f deploy/docker-compose.perf.yml"
PASSWORD="Lt-$(od -An -N12 -tx1 /dev/urandom | tr -d ' \n')!A9"
mkdir -p docs/test-reports

echo "==> 1/4 start stack with 2 API replicas (perf overlay)"
$COMPOSE up -d --no-build
i=0; until curl -fsS http://localhost:8080/health/ready >/dev/null 2>&1; do i=$((i+1)); [ $i -gt 60 ] && { echo "API not ready" >&2; exit 1; }; sleep 3; done

echo "==> 2/4 seed $VUS load-test accounts"
$COMPOSE run --rm --no-deps -e BLUEMEAL_LOADTEST_PASSWORD="$PASSWORD" api --seed-loadtest="$VUS"

cleanup() {
  echo "==> 4/4 remove load-test accounts and return to the normal dev stack"
  $COMPOSE run --rm --no-deps api --seed-loadtest=0 || true
  docker compose --env-file deploy/.env -f deploy/docker-compose.yml -f deploy/docker-compose.dev.yml up -d --no-build || true
}
trap cleanup EXIT

echo "==> 3/4 k6: $VUS VUs, ramp $RAMP, hold $HOLD"
docker run --rm --network bluemeal_frontend \
  -v "$MOUNT_ROOT/tools/k6:/scripts:ro" -v "$MOUNT_ROOT/docs/test-reports:/out" \
  -e BASE_URL=http://web:8080 -e LOADTEST_PASSWORD="$PASSWORD" -e USERS="$VUS" -e VUS="$VUS" -e HOLD="$HOLD" -e RAMP="$RAMP" \
  -e SUMMARY_JSON=/out/k6-summary.json -e K6_WEB_DASHBOARD=true -e K6_WEB_DASHBOARD_EXPORT=/out/k6-load-test.html \
  grafana/k6:latest run /scripts/load-test.js
