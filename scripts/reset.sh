#!/usr/bin/env bash
# Reset the demo repo to its seeded pre-session state:
# buggy calc.py, messy utils.py, empty audit log, stop-gate re-enabled.
set -euo pipefail
cd "$(dirname "$0")/.."

# Restore from the git INDEX (the staged baseline), so no commit is needed.
if ! git rev-parse --git-dir >/dev/null 2>&1; then
  echo "not a git repository — cannot restore seeded files" >&2
  exit 1
fi

git checkout -- src tests
echo "restored src/ and tests/ from the staged baseline"

rm -f .claude/logs/bash-audit.log
rm -f .claude/stop-gate.disabled
echo "audit log cleared, stop-gate re-enabled"
echo "run scripts/selftest.py to verify the demo state"
