#!/bin/sh
# OWASP ZAP baseline scan of the running stack (SPEC §10.5: no High/Medium findings before hand-over).
# ZAP (Apache-2.0) runs as a throw-away container on the compose "frontend" network; reports go to docs/test-reports/zap/.
set -eu
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
TARGET="${1:-http://web:8080}"
NETWORK="${ZAP_NETWORK:-bluemeal_frontend}"
mkdir -p docs/test-reports/zap
cp deploy/zap/baseline-rules.tsv docs/test-reports/zap/baseline-rules.tsv
docker run --rm --network "$NETWORK" -v "${ZAP_MOUNT_ROOT:-$ROOT}/docs/test-reports/zap:/zap/wrk:rw" -u root ghcr.io/zaproxy/zaproxy:stable \
  zap-baseline.py -t "$TARGET" -c baseline-rules.tsv -r zap-baseline.html -J zap-baseline.json -I
# fail when any Medium (2) or High (3) alert exists
python3 - <<'PY'
import json, sys
d = json.load(open("docs/test-reports/zap/zap-baseline.json", encoding="utf-8"))
bad = [(a["riskdesc"], a["name"]) for s in d["site"] for a in s["alerts"] if int(a["riskcode"]) >= 2]
print("High/Medium findings:", bad or "none")
sys.exit(1 if bad else 0)
PY
