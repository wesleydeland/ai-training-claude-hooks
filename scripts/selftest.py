#!/usr/bin/env python3
"""Smoke-test every hook in this repo by piping realistic Claude Code
payloads into the published hook executables — the same debugging recipe
the official docs recommend. Run before each session:

    scripts/setup.sh               # one-time: publish hooks + restore tests
    python3 scripts/selftest.py    # this file (expect ~10-20s: dotnet test runs)

No Claude Code install required; this validates the hook binaries and the
settings wiring, not Claude's end-to-end firing of them.
"""

import json
import os
import shutil
import subprocess
import sys
import tempfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
HOOKS = os.path.join(ROOT, ".claude", "hooks")
BIN = os.path.join(HOOKS, "bin")

HOOK_NAMES = [
    "protect",
    "block-force-push",
    "format-on-edit",
    "stop-gate",
    "audit-bash",
]

PASS = 0
FAIL = 0


def check(name, ok, detail=""):
    global PASS, FAIL
    if ok:
        PASS += 1
        print(f"  ok   {name}")
    else:
        FAIL += 1
        print(f"  FAIL {name}" + (f"  -- {detail}" if detail else ""))


def run_hook(name, payload, env=None):
    full_env = dict(os.environ)
    full_env.setdefault("CLAUDE_PROJECT_DIR", ROOT)
    if env:
        full_env.update(env)
    proc = subprocess.run(
        [os.path.join(BIN, name)],
        input=json.dumps(payload),
        capture_output=True,
        text=True,
        env=full_env,
        cwd=ROOT,
    )
    return proc.returncode, proc.stdout.strip(), proc.stderr.strip()


def payload(event, tool, tool_input, **extra):
    p = {
        "session_id": "selftest",
        "cwd": ROOT,
        "hook_event_name": event,
        "tool_name": tool,
        "tool_input": tool_input,
    }
    p.update(extra)
    return p


def main():
    # ---- setup state -------------------------------------------------------
    print("setup")
    missing = [n for n in HOOK_NAMES
               if not os.access(os.path.join(BIN, n), os.X_OK)]
    check("published hook binaries present (run scripts/setup.sh)",
          not missing, f"missing: {missing}")
    if missing:
        print(f"\n{PASS} passed, {FAIL} failed")
        return 1

    # ---- settings.json wiring ---------------------------------------------
    print("settings.json")
    settings_path = os.path.join(ROOT, ".claude", "settings.json")
    with open(settings_path, encoding="utf-8") as fh:
        settings = json.load(fh)
    check("parses as JSON", True)

    valid_events = {
        "PreToolUse", "PostToolUse", "PostToolUseFailure", "PostToolBatch",
        "UserPromptSubmit", "SessionStart", "SessionEnd", "Stop",
        "Notification", "SubagentStart", "SubagentStop", "PermissionRequest",
    }
    for event, entries in settings.get("hooks", {}).items():
        check(f"known event '{event}'", event in valid_events)
        for entry in entries:
            for handler in entry.get("hooks", []):
                check(f"'{event}' handler has type", "type" in handler)
                if handler.get("type") == "command":
                    cmd = handler.get("command", "")
                    # expand the env var the way Claude Code would
                    expanded = cmd.replace('"$CLAUDE_PROJECT_DIR"', ROOT).replace(
                        "$CLAUDE_PROJECT_DIR", ROOT
                    )
                    script = expanded.split()[-1].strip('"')
                    check(
                        f"'{event}' binary exists + executable: {os.path.basename(script)}",
                        os.path.isfile(script) and os.access(script, os.X_OK),
                        script,
                    )

    # ---- Demo 1a: protect (exit 2 contract) --------------------------------
    print("demo 1a: protect.cs")
    rc, out, err = run_hook(
        "protect",
        payload("PreToolUse", "Edit",
                {"file_path": os.path.join(ROOT, "src/generated/schema.json")}),
    )
    check("blocks edit to src/generated/", rc == 2 and "Blocked" in err,
          f"rc={rc} err={err!r}")

    rc, out, err = run_hook(
        "protect",
        payload("PreToolUse", "Edit", {"file_path": os.path.join(ROOT, "src/Calc.cs")}),
    )
    check("allows edit to src/Calc.cs", rc == 0 and out == "" and err == "",
          f"rc={rc} out={out!r} err={err!r}")

    rc, out, err = run_hook(
        "protect",
        payload("PreToolUse", "Edit", {"file_path": "not-json-payload"}),
    )
    check("survives malformed payload", rc == 0, f"rc={rc}")

    # ---- Demo 1b: block-force-push (structured JSON contract) --------------
    print("demo 1b: block-force-push.cs")
    for cmd in ["git push --force origin main",
                "git push -f origin main",
                "git push --force-with-lease origin main",
                "git push origin main && echo $(git push --force)"]:
        rc, out, err = run_hook(
            "block-force-push",
            payload("PreToolUse", "Bash", {"command": cmd}),
        )
        try:
            decision = json.loads(out)["hookSpecificOutput"]["permissionDecision"]
        except Exception:
            decision = None
        check(f"denies: {cmd}", rc == 0 and decision == "deny",
              f"rc={rc} out={out!r}")

    rc, out, err = run_hook(
        "block-force-push",
        payload("PreToolUse", "Bash", {"command": "git push origin main"}),
    )
    check("allows plain git push", rc == 0 and out == "", f"rc={rc} out={out!r}")

    rc, out, err = run_hook(
        "block-force-push",
        payload("PreToolUse", "Bash", {"command": "git fetch --all"}),
    )
    check("allows non-push command", rc == 0 and out == "", f"rc={rc} out={out!r}")

    for cmd in ["git push --follow-tags origin main",
                "git push origin force"]:
        rc, out, err = run_hook(
            "block-force-push",
            payload("PreToolUse", "Bash", {"command": cmd}),
        )
        check(f"allows: {cmd}", rc == 0 and out == "", f"rc={rc} out={out!r}")

    # ---- Demo 2: format-on-edit (PostToolUse additionalContext) ------------
    print("demo 2: format-on-edit.cs")
    with tempfile.TemporaryDirectory() as tmp:
        messy = os.path.join(tmp, "Messy.cs")
        with open(messy, "w", encoding="utf-8") as fh:
            fh.write("var x = 1;   \n\n\n\n\nvar y = 2;\nvar z = 3;   ")
        rc, out, err = run_hook(
            "format-on-edit",
            payload("PostToolUse", "Edit", {"file_path": messy}),
        )
        with open(messy, encoding="utf-8") as fh:
            fixed = fh.read()
        try:
            ctx = json.loads(out)["hookSpecificOutput"]["additionalContext"]
        except Exception:
            ctx = None
        check("rewrote messy file",
              fixed == "var x = 1;\n\nvar y = 2;\nvar z = 3;\n", f"got {fixed!r}")
        check("emitted additionalContext JSON", bool(ctx) and "normalized" in ctx,
              f"out={out!r}")

        rc, out, err = run_hook(
            "format-on-edit",
            payload("PostToolUse", "Edit", {"file_path": messy}),
        )
        check("no-op on already-clean file", rc == 0 and out == "",
              f"rc={rc} out={out!r}")

        rc, out, err = run_hook(
            "format-on-edit",
            payload("PostToolUse", "Edit", {"file_path": os.path.join(tmp, "nope.cs")}),
        )
        check("skips missing file", rc == 0 and out == "")

        binary = os.path.join(tmp, "blob.bin")
        with open(binary, "wb") as fh:
            fh.write(b"\x00\x01binary\xff data \x02")
        rc, out, err = run_hook(
            "format-on-edit",
            payload("PostToolUse", "Edit", {"file_path": binary}),
        )
        with open(binary, "rb") as fh:
            after = fh.read()
        check("leaves binary files alone", rc == 0 and after == b"\x00\x01binary\xff data \x02",
              f"rc={rc} changed={after!r}")

    # ---- Demo 3: stop-gate --------------------------------------------------
    print("demo 3: stop-gate.cs")
    rc, out, err = run_hook(
        "stop-gate",
        payload("Stop", "", {}, stop_hook_active=False),
    )
    try:
        parsed = json.loads(out)
        decision = parsed.get("decision")
        reason = parsed.get("reason", "")
    except Exception:
        decision, reason = None, ""
    check("blocks stop while tests red", rc == 0 and decision == "block"
          and "red" in reason.lower() and "fail" in reason.lower(),
          f"rc={rc} out={out[:200]!r}")

    rc, out, err = run_hook(
        "stop-gate",
        payload("Stop", "", {}, stop_hook_active=True),
    )
    check("loop guard: allows re-stop", rc == 0 and out == "", f"rc={rc} out={out!r}")

    marker = os.path.join(ROOT, ".claude", "stop-gate.disabled")
    try:
        open(marker, "w").close()
        rc, out, err = run_hook(
            "stop-gate",
            payload("Stop", "", {}, stop_hook_active=False),
        )
        check("kill-switch disables gate", rc == 0 and out == "", f"rc={rc} out={out!r}")
    finally:
        if os.path.exists(marker):
            os.remove(marker)

    # green path: copy repo, fix both seeded bugs, run REAL dotnet test there
    with tempfile.TemporaryDirectory() as tmp:
        for sub in ("src", "tests"):
            shutil.copytree(
                os.path.join(ROOT, sub),
                os.path.join(tmp, sub),
                ignore=shutil.ignore_patterns("bin", "obj", "__pycache__"),
            )
        calc = os.path.join(tmp, "src", "Calc.cs")
        with open(calc, encoding="utf-8") as fh:
            src = fh.read()
        src = src.replace("a - b", "a + b")
        src = src.replace(
            "var data = values;",
            "var data = values.OrderBy(x => x).ToList();",
        )
        with open(calc, "w", encoding="utf-8") as fh:
            fh.write(src)
        rc, out, err = run_hook(
            "stop-gate",
            {"session_id": "selftest", "cwd": tmp, "hook_event_name": "Stop",
             "stop_hook_active": False},
        )
        check("allows stop when tests green", rc == 0 and out == "",
              f"rc={rc} out={out!r} err={err!r}")

    # ---- Bonus: audit-bash --------------------------------------------------
    print("bonus: audit-bash.cs")
    with tempfile.TemporaryDirectory() as tmp:
        rc, out, err = run_hook(
            "audit-bash",
            payload("PostToolUse", "Bash", {"command": 'dotnet test "quotes" && echo done'}),
            env={"CLAUDE_PROJECT_DIR": tmp},
        )
        log = os.path.join(tmp, ".claude", "logs", "bash-audit.log")
        line = ""
        if os.path.isfile(log):
            with open(log, encoding="utf-8") as fh:
                line = fh.read().strip()
        try:
            entry = json.loads(line)
        except Exception:
            entry = {}
        check("appends valid JSON audit line",
              rc == 0 and entry.get("command") == 'dotnet test "quotes" && echo done',
              f"rc={rc} line={line!r}")

    # ---- Seed state ----------------------------------------------------------
    print("seed state")
    with open(os.path.join(ROOT, "src", "Utils.cs"), encoding="utf-8") as fh:
        raw = fh.read()
    has_trailing = any(line != line.rstrip() for line in raw.split("\n"))
    check("Utils.cs has trailing whitespace (demo 2 seed)", has_trailing)
    check("Utils.cs missing final newline (demo 2 seed)", not raw.endswith("\n"))

    proc = subprocess.run(
        ["dotnet", "test", "tests/Calc.Tests"],
        cwd=ROOT, capture_output=True, text=True,
    )
    state = ("RED (seeded bugs present — expected before demo 3)"
             if proc.returncode != 0 else "GREEN")
    print(f"  info tests currently: {state}")

    print(f"\n{PASS} passed, {FAIL} failed")
    return 1 if FAIL else 0


if __name__ == "__main__":
    sys.exit(main())
