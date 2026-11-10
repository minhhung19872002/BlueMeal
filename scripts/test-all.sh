#!/bin/sh
# BlueMeal CI script (Linux/macOS). Usage: ./scripts/test-all.sh [--skip-integration] [--skip-frontend]
set -u
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
REPORTS="$ROOT/docs/test-reports"
mkdir -p "$REPORTS"
SKIP_INT=0; SKIP_FE=0
for a in "$@"; do case "$a" in --skip-integration) SKIP_INT=1 ;; --skip-frontend) SKIP_FE=1 ;; esac; done
FAILED=""
step() { name="$1"; shift; echo "==> $name"; if "$@"; then echo "OK: $name"; else echo "FAILED: $name"; FAILED="$FAILED|$name"; fi; }

cd "$ROOT/backend"
step "backend build" dotnet build BlueMeal.sln -c Release
step "backend unit tests" dotnet test tests/BlueMeal.UnitTests -c Release --no-build --logger "trx;LogFileName=unit.trx" --results-directory "$REPORTS/backend" --collect:"XPlat Code Coverage"
[ "$SKIP_INT" = 1 ] || step "backend integration tests" dotnet test tests/BlueMeal.IntegrationTests -c Release --no-build --logger "trx;LogFileName=integration.trx" --results-directory "$REPORTS/backend"
vuln() { dotnet list BlueMeal.sln package --vulnerable --include-transitive > "$REPORTS/nuget-vulnerable.txt" 2>&1; ! grep -q "has the following vulnerable packages" "$REPORTS/nuget-vulnerable.txt"; }
step "nuget vulnerability scan" vuln

if [ "$SKIP_FE" = 0 ]; then
  cd "$ROOT/frontend"
  [ -d node_modules ] || step "npm ci" npm ci --no-audit --no-fund
  step "frontend i18n key check" npm run -s i18n:check
  step "frontend lint" npx eslint .
  step "frontend typecheck" npx tsc -b
  step "frontend unit tests" npx vitest run --reporter=default --reporter=junit --outputFile="$REPORTS/frontend-junit.xml"
  step "npm audit (production)" npm audit --omit=dev --audit-level=moderate
fi

if [ -n "$FAILED" ]; then echo "Failed steps:$FAILED" | tr '|' '\n'; exit 1; fi
echo "All checks passed."
