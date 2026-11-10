#!/bin/sh
# Offline deployment: build and export all images to a tar archive. Usage: ./scripts/save-images.sh [version] [output_dir]
set -eu
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
VERSION="${1:-1.0.0}"; OUT="${2:-offline-images}"
export BLUEMEAL_VERSION="$VERSION"
docker compose --env-file deploy/.env -f deploy/docker-compose.yml -f deploy/docker-compose.prod.yml build
IMAGES="bluemeal/api:$VERSION bluemeal/web:$VERSION bluemeal/backup:$VERSION postgres:16-alpine redis:7.2-alpine"
docker pull postgres:16-alpine >/dev/null; docker pull redis:7.2-alpine >/dev/null
mkdir -p "$OUT"
TAR="$OUT/bluemeal-images-$VERSION.tar"
# shellcheck disable=SC2086
docker save -o "$TAR" $IMAGES
mkdir -p "$OUT/deploy" "$OUT/scripts"
tar --exclude=.env -cf - deploy | tar -C "$OUT" -xf -
# never ship secrets: environment file and TLS key/certificate are created on the target server
rm -f "$OUT/deploy/.env" "$OUT"/deploy/nginx/ssl/*.key "$OUT"/deploy/nginx/ssl/*.crt "$OUT"/deploy/nginx/ssl/*.pem
cp -r scripts/. "$OUT/scripts/"
(cd "$OUT" && sha256sum "$(basename "$TAR")" > "$(basename "$TAR").sha256")
echo "Saved $TAR. Copy '$OUT' to the offline server and run scripts/load-images.sh"
