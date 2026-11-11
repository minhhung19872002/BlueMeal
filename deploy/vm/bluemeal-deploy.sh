#!/bin/sh
# Update BlueMeal on the shared VM to a pushed commit: get the images, back up, migrate, restart (CD step of
# .github/workflows/ci-cd.yml). Installed on the VM as ~/apps/bluemeal-deploy.sh — a copy, so a commit cannot
# change what the deploy key runs. The GitHub Actions key is pinned to it in ~/.ssh/authorized_keys:
#   restrict,command="/home/hung/apps/bluemeal-deploy.sh" ssh-ed25519 ... github-actions-deploy-bluemeal
# so "<sha> pull" arrives in SSH_ORIGINAL_COMMAND.
#   pull  (CD): the images were built on GitHub Actions and pushed to GHCR; this VM only pulls them. Two lines
#         on stdin: GHCR user, then a token that can read the packages (the job's GITHUB_TOKEN).
#   build (default; manual: ~/apps/bluemeal-deploy.sh <git sha>): build here. Avoid it while the VM is busy —
#         it is shared, has 11 GB RAM and no swap, and a .NET build takes several GB.
# deploy/.env and deploy/nginx/ssl/ are git-ignored and never touched.
set -eu
set -f
[ $# -gt 0 ] || set -- ${SSH_ORIGINAL_COMMAND:-}
set +f
rev="${1:-}"
mode="${2:-build}"
usage() { echo "usage: bluemeal-deploy.sh <git sha> [build|pull]" >&2; exit 2; }
case "$rev" in *[!0-9a-f]* | "") usage ;; esac
case "$mode" in build | pull) ;; *) usage ;; esac
registry=ghcr.io/minhhung19872002
app="${BLUEMEAL_HOME:-$HOME/BlueMeal}"
exec 9> "$HOME/apps/bluemeal.lock"
flock 9
log=$(mktemp)
ghcr=$(mktemp -d)   # throwaway docker config for the GHCR login; ~/.docker belongs to every app on the VM
trap 'rm -rf "$log" "$ghcr"' EXIT
# Show the tail of a step's output; a failing step stops the deploy with its log (sh has no pipefail).
step() { echo "==> $*"; if ! "$@" > "$log" 2>&1; then tail -40 "$log"; echo "FAILED: $*" >&2; exit 1; fi; grep -vE "^#|^ *$" "$log" | tail -5; }
dc() { docker compose --env-file deploy/.env -f deploy/docker-compose.yml -f deploy/docker-compose.prod.yml -f deploy/docker-compose.proxy.yml "$@"; }

cd "$app"
step git fetch -q origin main
step git checkout -q -f -B main "$rev"
GIT_COMMIT=$(git rev-parse --short HEAD)
BUILD_TIME=$(date -u +%FT%TZ)
export GIT_COMMIT BUILD_TIME

if [ "$mode" = pull ]; then
  IFS= read -r ghcr_user && IFS= read -r ghcr_token || { echo "FAILED: pull needs '<user>' and '<token>' on stdin" >&2; exit 1; }
  printf '%s' "$ghcr_token" | docker --config "$ghcr" login ghcr.io -u "$ghcr_user" --password-stdin > "$log" 2>&1 \
    || { tail -5 "$log"; echo "FAILED: docker login ghcr.io" >&2; exit 1; }
  sha=$(git rev-parse HEAD)
  for svc in api web backup; do
    remote="$registry/bluemeal-$svc:$sha"
    # The name compose expects for this service (bluemeal/<svc>:$BLUEMEAL_VERSION), so the compose files stay as is.
    target=$(dc config --images | grep "^bluemeal/$svc:" | head -1)
    [ -n "$target" ] || { echo "FAILED: no compose image for $svc" >&2; exit 1; }
    step docker --config "$ghcr" pull -q "$remote"
    docker tag "$remote" "$target"
    docker rmi "$remote" > /dev/null || true   # drops the GHCR tag only
  done
  no_build=--no-build
else
  step dc build
  no_build=
fi
# Encrypted snapshot before migrations touch the schema (kept by the normal retention policy).
step dc exec -T backup /usr/local/bin/backup.sh Manual "predeploy-$GIT_COMMIT-$(date +%Y%m%d%H%M%S)"
step dc run --rm --no-deps -e Database__Seed=false api --migrate-only
step dc up -d $no_build --wait --wait-timeout 600 --remove-orphans

i=0
until docker exec bluemeal-web-1 curl -fsS http://127.0.0.1:8080/health/ready 2>/dev/null | grep -q Healthy; do
  i=$((i+1)); [ "$i" -gt 36 ] && { echo "FAILED: /health/ready" >&2; dc logs --tail 40 api >&2; exit 1; }
  sleep 5
done
echo "$rev" > "$HOME/apps/bluemeal.REVISION"
docker image prune -f > /dev/null
docker builder prune -f --filter until=72h > /dev/null
echo "Deployed $rev ($GIT_COMMIT)"
