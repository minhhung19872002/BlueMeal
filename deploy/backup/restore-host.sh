#!/bin/sh
# Restore wrapper for Linux hosts: ./restore-host.sh <archive> [target_db] [--with-files]
set -eu
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
docker compose -f "$ROOT/docker-compose.yml" -f "$ROOT/docker-compose.prod.yml" exec backup /usr/local/bin/restore.sh "$@"
