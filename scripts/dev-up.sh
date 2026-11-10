#!/bin/sh
# BlueMeal — start the full development stack (Linux/macOS).
set -eu
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
"$ROOT/scripts/gen-secrets.sh" --dev
export GIT_COMMIT="$(git rev-parse --short HEAD 2>/dev/null || echo dev)"
export BUILD_TIME="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
COMPOSE="docker compose --env-file deploy/.env -f deploy/docker-compose.yml -f deploy/docker-compose.dev.yml"
BUILD="--build"; [ "${1:-}" = "--no-build" ] && BUILD=""
$COMPOSE up -d $BUILD
echo "==> Waiting for API readiness..."
i=0
until curl -fsS http://localhost:8080/health/ready >/dev/null 2>&1; do
  i=$((i+1)); [ $i -gt 90 ] && { $COMPOSE logs --tail 80 api; echo "API not ready" >&2; exit 1; }
  sleep 3
done
ADMIN_PWD="$(grep '^BLUEMEAL_ADMIN_PASSWORD=' deploy/.env | cut -d= -f2-)"
echo ""
echo "BlueMeal is running"
echo "  Web (WAF + SPA) : http://localhost:8080   (https://localhost:8443, self-signed)"
echo "  API             : http://localhost:5080/swagger"
echo "  Mailpit (email) : http://localhost:8025"
echo "  Admin account   : admin / $ADMIN_PWD"
echo "  -> you must change the password and enable MFA at first login."
