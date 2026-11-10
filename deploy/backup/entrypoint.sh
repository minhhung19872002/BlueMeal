#!/bin/sh
# BlueMeal backup sidecar.
# - Scheduled backups via busybox crond (BACKUP_CRON, default 01:00 Asia/Ho_Chi_Minh).
# - Manual ("bất thường") backups requested by the worker through /backups/requests/<id>.req.
# Starts as root only to prepare volume permissions and run crond; all backup work runs as the postgres user.
set -eu

if [ "${1:-}" = "watch" ]; then
  umask 022
  echo "[backup] watching /backups/requests for manual backup requests"
  while true; do
    for req in /backups/requests/*.req; do
      [ -e "$req" ] || continue
      id=$(basename "$req" .req)
      mv "$req" "/backups/requests/$id.running" 2>/dev/null || continue
      echo "[backup] manual request $id"
      /usr/local/bin/backup.sh Manual "$id" >> /backups/backup.log 2>&1 || true
      rm -f "/backups/requests/$id.running"
    done
    sleep 5
  done
fi

mkdir -p /backups/archive /backups/manifests /backups/requests /backups/restore-tests
chown -R postgres:postgres /backups/archive /backups/manifests /backups/restore-tests
chown postgres:postgres /backups
touch /backups/backup.log && chown postgres:postgres /backups/backup.log
# the worker (uid 1654) drops request files here and the watcher (postgres) must be able to rename/delete them:
# no sticky bit (with 1777 only the file owner could rename). The volume is private to the backup and worker containers.
chmod 0777 /backups/requests

# Environment needed by cron jobs (crond does not inherit the container env)
ENV_FILE=/tmp/backup.env
: > "$ENV_FILE"
for v in PGHOST PGUSER PGPASSWORD PGDATABASE BACKUP_PASSPHRASE KEEP_DAILY KEEP_WEEKLY KEEP_MONTHLY BACKUP_MIRROR_PATH TZ; do
  eval "val=\${$v:-}"
  printf "export %s='%s'\n" "$v" "$(printf '%s' "$val" | sed "s/'/'\\\\''/g")" >> "$ENV_FILE"
done
chown postgres:postgres "$ENV_FILE" && chmod 600 "$ENV_FILE"

mkdir -p /etc/crontabs
echo "${BACKUP_CRON} su-exec postgres sh -c '. $ENV_FILE && /usr/local/bin/backup.sh Scheduled' >> /backups/backup.log 2>&1" > /etc/crontabs/root
echo "[backup] schedule: ${BACKUP_CRON} (TZ=${TZ:-UTC})"
crond -b -l 8 -L /backups/cron.log

exec su-exec postgres "$0" watch
