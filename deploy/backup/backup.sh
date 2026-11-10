#!/bin/sh
# Usage: backup.sh [Scheduled|Manual] [id]
# Produces /backups/archive/<name>.tar.enc (+ .sha256) and /backups/manifests/<id>.json
# Encryption: openssl AES-256-CBC, PBKDF2 200k iterations, passphrase from BACKUP_PASSPHRASE (ASSUMPTIONS A-29).
set -eu
TYPE="${1:-Scheduled}"
ID="${2:-sched-$(date +%Y%m%d%H%M%S)}"
TS="$(date +%Y%m%d-%H%M%S)"
NAME="bluemeal-${TS}-$(echo "$TYPE" | tr 'A-Z' 'a-z')"
ARCHIVE=/backups/archive
MANIFESTS=/backups/manifests
mkdir -p "$ARCHIVE" "$MANIFESTS"
STARTED="$(date -Iseconds)"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

fail() {
  printf '{"id":"%s","type":"%s","status":"Failed","startedAt":"%s","finishedAt":"%s","error":"%s"}\n' \
    "$ID" "$TYPE" "$STARTED" "$(date -Iseconds)" "$1" > "$MANIFESTS/$ID.json"
  echo "[backup] FAILED: $1" >&2
  exit 1
}

[ -n "${BACKUP_PASSPHRASE:-}" ] || fail "BACKUP_PASSPHRASE not set"
echo "[backup] $TYPE $ID -> $NAME"
pg_dump -h "${PGHOST:-postgres}" -U "${PGUSER:-bluemeal_owner}" -d "${PGDATABASE:-bluemeal}" -Fc -Z 6 -f "$WORK/db.dump" || fail "pg_dump failed"
if [ -d /data/files ]; then tar -C /data -czf "$WORK/files.tar.gz" files 2>/dev/null || fail "files archive failed"; else tar -czf "$WORK/files.tar.gz" -T /dev/null; fi
printf '{"version":1,"createdAt":"%s","database":"%s"}\n' "$STARTED" "${PGDATABASE:-bluemeal}" > "$WORK/meta.json"
tar -C "$WORK" -cf "$WORK/bundle.tar" db.dump files.tar.gz meta.json
openssl enc -aes-256-cbc -pbkdf2 -iter 200000 -salt -in "$WORK/bundle.tar" -out "$ARCHIVE/$NAME.tar.enc" -pass env:BACKUP_PASSPHRASE || fail "encryption failed"
SHA="$(sha256sum "$ARCHIVE/$NAME.tar.enc" | cut -d' ' -f1)"
echo "$SHA  $NAME.tar.enc" > "$ARCHIVE/$NAME.tar.enc.sha256"
SIZE="$(stat -c %s "$ARCHIVE/$NAME.tar.enc")"

if [ -n "${BACKUP_MIRROR_PATH:-}" ] && [ -d "${BACKUP_MIRROR_PATH}" ]; then
  cp "$ARCHIVE/$NAME.tar.enc" "$ARCHIVE/$NAME.tar.enc.sha256" "$BACKUP_MIRROR_PATH/" || echo "[backup] WARN: mirror copy failed" >&2
fi

printf '{"id":"%s","type":"%s","status":"Succeeded","file":"%s.tar.enc","size":%s,"sha256":"%s","startedAt":"%s","finishedAt":"%s"}\n' \
  "$ID" "$TYPE" "$NAME" "$SIZE" "$SHA" "$STARTED" "$(date -Iseconds)" > "$MANIFESTS/$ID.json"
echo "[backup] done $NAME ($SIZE bytes, sha256 $SHA)"

# ---- retention for scheduled backups: keep KEEP_DAILY newest, newest per week (KEEP_WEEKLY), newest per month (KEEP_MONTHLY)
if [ "$TYPE" = "Scheduled" ]; then
  ls -1 "$ARCHIVE"/bluemeal-*-scheduled.tar.enc 2>/dev/null | sort -r > "$WORK/all.txt" || true
  : > "$WORK/keep.txt"
  head -n "${KEEP_DAILY:-7}" "$WORK/all.txt" >> "$WORK/keep.txt"
  # file name: bluemeal-YYYYMMDD-HHMMSS-scheduled.tar.enc
  while read -r f; do
    d="$(basename "$f" | cut -d- -f2)"
    wk="$(date -d "$(echo "$d" | sed 's/\(....\)\(..\)\(..\)/\1-\2-\3/')" +%G-%V 2>/dev/null || echo "$d")"
    echo "$wk $f"
  done < "$WORK/all.txt" | awk '!seen[$1]++ {print $2}' | head -n "${KEEP_WEEKLY:-4}" >> "$WORK/keep.txt"
  while read -r f; do
    m="$(basename "$f" | cut -d- -f2 | cut -c1-6)"
    echo "$m $f"
  done < "$WORK/all.txt" | awk '!seen[$1]++ {print $2}' | head -n "${KEEP_MONTHLY:-6}" >> "$WORK/keep.txt"
  while read -r f; do
    if ! grep -qxF "$f" "$WORK/keep.txt"; then
      echo "[backup] retention: removing $(basename "$f")"
      rm -f "$f" "$f.sha256"
    fi
  done < "$WORK/all.txt"
fi
