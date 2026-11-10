#!/bin/sh
# Update BlueMeal on the shared VM to a pushed commit: build, back up, migrate, restart (CD step of
# .github/workflows/ci-cd.yml). Installed on the VM as ~/apps/bluemeal-deploy.sh — a copy, so a commit cannot
# change what the deploy key runs. The GitHub Actions key is pinned to it in ~/.ssh/authorized_keys:
#   restrict,command="/home/hung/apps/bluemeal-deploy.sh" ssh-ed25519 ... github-actions-deploy-bluemeal
# so the SHA arrives in SSH_ORIGINAL_COMMAND.  Manual: ~/apps/bluemeal-deploy.sh <git sha>
# deploy/.env and deploy/nginx/ssl/ are git-ignored and never touched.
set -eu
rev="${1:-${SSH_ORIGINAL_COMMAND:-}}"
case "$rev" in
  *[!0-9a-f]* | "") echo "usage: bluemeal-deploy.sh <git sha>" >&2; exit 2 ;;
esac
app="${BLUEMEAL_HOME:-$HOME/BlueMeal}"
exec 9> "$HOME/apps/bluemeal.lock"
flock 9
log=$(mktemp)
trap 'rm -f "$log"' EXIT
# Show the tail of a step's output; a failing step stops the deploy with its log (sh has no pipefail).
step() { echo "==> $*"; if ! "$@" > "$log" 2>&1; then tail -40 "$log"; echo "FAILED: $*" >&2; exit 1; fi; grep -vE "^#|^ *$" "$log" | tail -5; }
dc() { docker compose --env-file deploy/.env -f deploy/docker-compose.yml -f deploy/docker-compose.prod.yml -f deploy/docker-compose.proxy.yml "$@"; }

cd "$app"
step git fetch -q origin main
step git checkout -q -f -B main "$rev"
GIT_COMMIT=$(git rev-parse --short HEAD)
BUILD_TIME=$(date -u +%FT%TZ)
export GIT_COMMIT BUILD_TIME

step dc build
# Encrypted snapshot before migrations touch the schema (kept by the normal retention policy).
step dc exec -T backup /usr/local/bin/backup.sh Manual "predeploy-$GIT_COMMIT-$(date +%Y%m%d%H%M%S)"
step dc run --rm --no-deps -e Database__Seed=false api --migrate-only
step dc up -d --wait --wait-timeout 600 --remove-orphans

i=0
until docker exec bluemeal-web-1 curl -fsS http://127.0.0.1:8080/health/ready 2>/dev/null | grep -q Healthy; do
  i=$((i+1)); [ "$i" -gt 36 ] && { echo "FAILED: /health/ready" >&2; dc logs --tail 40 api >&2; exit 1; }
  sleep 5
done
echo "$rev" > "$HOME/apps/bluemeal.REVISION"
docker image prune -f > /dev/null
docker builder prune -f --filter until=72h > /dev/null
echo "Deployed $rev ($GIT_COMMIT)"
