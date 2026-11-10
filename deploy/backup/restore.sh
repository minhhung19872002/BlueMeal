#!/bin/sh
# Usage: restore.sh <archive.tar.enc> [target_db] [--with-files]
# Verifies SHA-256, decrypts, then pg_restore --clean into target_db (created if missing).
set -eu
ARCHIVE="$1"; TARGET="${2:-${PGDATABASE:-bluemeal}}"; WITH_FILES="${3:-}"
[ -f "$ARCHIVE" ] || ARCHIVE="/backups/archive/$ARCHIVE"
[ -f "$ARCHIVE" ] || { echo "archive not found: $1" >&2; exit 1; }
[ -n "${BACKUP_PASSPHRASE:-}" ] || { echo "BACKUP_PASSPHRASE not set" >&2; exit 1; }
if [ -f "$ARCHIVE.sha256" ]; then
  (cd "$(dirname "$ARCHIVE")" && sha256sum -c "$(basename "$ARCHIVE").sha256") || { echo "checksum mismatch" >&2; exit 2; }
fi
WORK="$(mktemp -d)"; trap 'rm -rf "$WORK"' EXIT
openssl enc -d -aes-256-cbc -pbkdf2 -iter 200000 -in "$ARCHIVE" -out "$WORK/bundle.tar" -pass env:BACKUP_PASSPHRASE
tar -C "$WORK" -xf "$WORK/bundle.tar"
H="${PGHOST:-postgres}"; U="${PGUSER:-bluemeal_owner}"
if ! psql -h "$H" -U "$U" -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname='$TARGET'" | grep -q 1; then
  psql -h "$H" -U "$U" -d postgres -c "CREATE DATABASE \"$TARGET\""
fi
pg_restore -h "$H" -U "$U" -d "$TARGET" --clean --if-exists --no-owner --role="$U" "$WORK/db.dump"
if [ "$WITH_FILES" = "--with-files" ] && [ -d /data ]; then
  tar -C /data -xzf "$WORK/files.tar.gz"
fi
echo "[restore] restored $(basename "$ARCHIVE") into database '$TARGET'"
