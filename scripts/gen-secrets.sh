#!/bin/sh
# Creates deploy/.env from deploy/.env.example with freshly generated random secrets (never overwrites).
# Usage: ./scripts/gen-secrets.sh [--dev] [--force]
set -eu
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
ENV_FILE="$ROOT/deploy/.env"
DEV=0; FORCE=0
for a in "$@"; do case "$a" in --dev) DEV=1 ;; --force) FORCE=1 ;; esac; done
if [ -f "$ENV_FILE" ] && [ "$FORCE" = 0 ]; then echo "deploy/.env already exists - kept."; exit 0; fi

pw() { LC_ALL=C tr -dc 'A-HJ-NP-Za-km-z2-9' < /dev/urandom | head -c "${1:-24}"; }
b64() { head -c 32 /dev/urandom | base64 | tr -d '\n'; }

sed -e "s|POSTGRES_PASSWORD=change-me-postgres|POSTGRES_PASSWORD=$(pw)|" \
    -e "s|BLUEMEAL_OWNER_PASSWORD=change-me-owner|BLUEMEAL_OWNER_PASSWORD=$(pw)|" \
    -e "s|BLUEMEAL_APP_PASSWORD=change-me-app|BLUEMEAL_APP_PASSWORD=$(pw)|" \
    -e "s|REDIS_PASSWORD=change-me-redis|REDIS_PASSWORD=$(pw)|" \
    -e "s|BLUEMEAL_JWT_KEY=change-me-to-a-long-random-string-of-64-chars|BLUEMEAL_JWT_KEY=$(pw 64)|" \
    -e "s|BLUEMEAL_ENC_KEYS=k1:REPLACE_WITH_BASE64_32_BYTES|BLUEMEAL_ENC_KEYS=k1:$(b64)|" \
    -e "s|BLUEMEAL_HMAC_KEY=REPLACE_WITH_BASE64_32_BYTES|BLUEMEAL_HMAC_KEY=$(b64)|" \
    -e "s|BACKUP_PASSPHRASE=change-me-backup-passphrase|BACKUP_PASSPHRASE=$(pw 40)|" \
    "$ROOT/deploy/.env.example" > "$ENV_FILE"
if [ "$DEV" = 1 ]; then
  sed -i.bak -e 's|ASPNETCORE_ENVIRONMENT=Production|ASPNETCORE_ENVIRONMENT=Development|' \
      -e 's|BLUEMEAL_PUBLIC_URL=https://bluemeal.example.vn|BLUEMEAL_PUBLIC_URL=http://localhost:8080|' \
      -e 's|TLS_REDIRECT=on|TLS_REDIRECT=off|' -e 's|AUTH_COOKIE_SECURE=true|AUTH_COOKIE_SECURE=false|' \
      -e 's|BLUEMEAL_ADMIN_PASSWORD=ChangeMe@2026!|BLUEMEAL_ADMIN_PASSWORD=BlueMeal@2026!|' "$ENV_FILE"
  rm -f "$ENV_FILE.bak"
fi
chmod 600 "$ENV_FILE"
echo "Created deploy/.env with random secrets."
