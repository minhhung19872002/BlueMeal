#!/bin/sh
# Wrapper so init.sql receives passwords from environment (never hard-coded).
set -e
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
  -v owner_password="$BLUEMEAL_OWNER_PASSWORD" -v app_password="$BLUEMEAL_APP_PASSWORD" \
  -f /docker-entrypoint-initdb.d/sql/init.sql
