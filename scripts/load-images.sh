#!/bin/sh
# Offline deployment: load images exported by save-images.sh. Usage: ./scripts/load-images.sh [archive.tar]
set -eu
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
ARCHIVE="${1:-$(ls -t "$ROOT"/*/bluemeal-images-*.tar "$ROOT"/bluemeal-images-*.tar 2>/dev/null | head -n1)}"
[ -f "$ARCHIVE" ] || { echo "Image archive not found" >&2; exit 1; }
if [ -f "$ARCHIVE.sha256" ]; then (cd "$(dirname "$ARCHIVE")" && sha256sum -c "$(basename "$ARCHIVE").sha256"); fi
docker load -i "$ARCHIVE"
echo "Images loaded. Next: ./scripts/gen-secrets.sh && docker compose --env-file deploy/.env -f deploy/docker-compose.yml -f deploy/docker-compose.prod.yml up -d"
