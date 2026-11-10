#!/bin/sh
# Seed reference data and optionally SAMPLE data, then exit. Usage: ./scripts/seed.sh [--no-sample] [--prod]
set -eu
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
OVERLAY=deploy/docker-compose.dev.yml; SAMPLE=true
for a in "$@"; do case "$a" in --prod) OVERLAY=deploy/docker-compose.prod.yml ;; --no-sample) SAMPLE=false ;; esac; done
docker compose --env-file deploy/.env -f deploy/docker-compose.yml -f "$OVERLAY" run --rm --no-deps -e Seed__SampleData=$SAMPLE api --seed-only
echo "Seed completed."
