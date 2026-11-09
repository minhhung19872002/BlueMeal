#!/bin/sh
# Run the API from source against the dockerized postgres/redis (dev). Usage: ./scripts/run-api-local.sh [--worker]
set -eu
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
get() { grep "^$1=" "$ROOT/deploy/.env" | cut -d= -f2-; }
export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS="http://localhost:${DEV_API_PORT:-5080}"
export BLUEMEAL_DB="Host=localhost;Port=${DEV_PG_PORT:-5442};Database=bluemeal;Username=bluemeal_app;Password=$(get BLUEMEAL_APP_PASSWORD);Maximum Pool Size=100"
export ConnectionStrings__Migration="Host=localhost;Port=${DEV_PG_PORT:-5442};Database=bluemeal;Username=bluemeal_owner;Password=$(get BLUEMEAL_OWNER_PASSWORD)"
export BLUEMEAL_REDIS="localhost:${DEV_REDIS_PORT:-6395},password=$(get REDIS_PASSWORD)"
export BLUEMEAL_JWT_KEY="$(get BLUEMEAL_JWT_KEY)"
export BLUEMEAL_ENC_KEYS="$(get BLUEMEAL_ENC_KEYS)"
export BLUEMEAL_HMAC_KEY="$(get BLUEMEAL_HMAC_KEY)"
export BLUEMEAL_ADMIN_PASSWORD="$(get BLUEMEAL_ADMIN_PASSWORD)"
export BLUEMEAL_PUBLIC_URL="http://localhost:5173"
export BLUEMEAL_FILES_PATH="$ROOT/data/files"
export BLUEMEAL_EXPORT_PATH="$ROOT/data/exports"
export BLUEMEAL_BACKUP_PATH="$ROOT/data/backups"
export Logging__FilePath="$ROOT/data/logs/bluemeal-.json"
cd "$ROOT/backend"
exec dotnet run --project src/BlueMeal.Api --no-launch-profile -- "$@"
