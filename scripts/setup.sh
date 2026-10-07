#!/usr/bin/env bash
# One-time setup: publish all five hooks as standalone native executables and
# restore the test project.
#
# Takes ~1 minute: each hook's .cs file is compiled once to a self-contained
# binary (.claude/hooks/bin/<name>), which then starts in ~15ms — the same
# ballpark as the bash versions. Without this, settings.json has nothing to run.
#
# Needs the .NET 10 SDK and a native toolchain for the compiler (clang or gcc;
# on Debian/Ubuntu: apt install clang).
set -euo pipefail
cd "$(dirname "$0")/.."

mkdir -p .claude/hooks/bin
for hook in protect block-force-push format-on-edit stop-gate audit-bash; do
  echo "--- publishing $hook.cs"
  dotnet publish ".claude/hooks/$hook.cs" -o .claude/hooks/bin
done

echo "--- restoring test project (needs network once, then cached)"
dotnet restore tests/Calc.Tests

echo "--- setup done; next: python3 scripts/selftest.py"
