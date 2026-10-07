# Claude Code Hooks — Demo Repo

A self-contained playground for a 30–60 minute training session on Claude Code
hooks: what they are, how the lifecycle works, and the use cases that matter.

**The hooks are C# file-based apps** (.NET 10): every hook is a single `.cs`
file, published once to a standalone native executable. For a .NET audience
there is nothing to translate — they read the hook source in their own
language, and the repo has zero dependency on bash, `jq`, or Python at runtime.

## Requirements

- .NET 10 SDK (`dotnet --version` reports `10.x`)
- A native toolchain for the one-time publish (`clang` or `gcc`; on
  Debian/Ubuntu: `apt install clang`)
- `git`
- Python 3 — presenter-only, for `scripts/selftest.py`
- Claude Code (`claude`)

## Quick start

```bash
scripts/setup.sh            # one-time, ~30-60s: publishes the 5 hooks into
                            # .claude/hooks/bin/ and restores the test project
                            # (one network restore, then cached)
python3 scripts/selftest.py # 38 checks: settings wiring + every hook binary.
                            # Expect 10-20s — it runs `dotnet test` three times
claude                      # start a session in this repo
/hooks                       # Claude Code's built-in hook viewer/editor
```

Run the self-test before each session. It pipes realistic hook payloads into
every binary — the same debugging recipe the official docs recommend — so you
know the demo will fire before you're in front of people.

**After editing a hook's `.cs` file, republish it** (or just run `setup.sh` again):

```bash
dotnet publish .claude/hooks/protect.cs -o .claude/hooks/bin
```

Build artifacts are cached under `~/.local/share/dotnet/runfile/`, so the repo
stays clean; `.claude/hooks/bin/` is gitignored — teammates build locally from
reviewed source.

## The five hooks

| # | Source | Binary | Event | Matcher | Contract demonstrated |
|---|--------|--------|-------|---------|----------------------|
| 1a | `protect.cs` | `bin/protect` | `PreToolUse` | `Edit\|Write` | Simple: `return 2` + stderr |
| 1b | `block-force-push.cs` | `bin/block-force-push` | `PreToolUse` | `Bash` + `if: Bash(git push *)` | Structured: `permissionDecision: "deny"` JSON |
| 2 | `format-on-edit.cs` | `bin/format-on-edit` | `PostToolUse` | `Edit\|Write` | `additionalContext` fed back to Claude |
| 3 | `stop-gate.cs` | `bin/stop-gate` | `Stop` | *(none)* | `{"decision": "block", "reason": ...}` |
| + | `audit-bash.cs` | `bin/audit-bash` | `PostToolUse` | `Bash` (`async: true`) | Fire-and-forget background hook |

Config lives in `.claude/settings.json` (committed → the whole team inherits it).

## Porting notes — the C# story for a .NET audience

The hook contract is language-agnostic: JSON on stdin, decision on stdout or in
the exit code. The bash/Python idioms map one-to-one:

| bash / jq idiom | C# equivalent |
|---|---|
| `jq -r '.tool_input.file_path'` | `JsonDocument.Parse(Console.In.ReadToEnd())` |
| `grep -qE 'pattern'` | `Regex.IsMatch(command, pattern)` |
| `jq -n '{...}'` | `Console.WriteLine("""{...}""")` (literal JSON) |
| `echo "reason" >&2; exit 2` | `Console.Error.WriteLine(...); return 2` |
| `jq -c '{ts: now}'` | `DateTime.UtcNow.ToString("o")` |
| shell loop over files | plain `File.ReadAllText` / `File.WriteAllText` |

Two gotchas learned the hard way — worth showing if someone asks "why not
`JsonSerializer.Serialize`?":

1. **Published file-based apps are trimmed/AOT.** The reflection-based
   `JsonSerializer.Serialize` throws at runtime (`Reflection-based serialization
   has been disabled`). Parsing input with `JsonDocument` is fine (no
   reflection); for output the hooks print literal JSON strings — which has the
   bonus of making the exact wire format visible in the source. In `audit-bash.cs`,
   `JsonElement.GetRawText()` hands back already-escaped JSON for free.
2. **`dotnet run hook.cs` also works** (~145ms — measured) but goes through
   MSBuild on every single hook firing. Publishing once to `bin/` gets you
   ~12ms. That one-time `setup.sh` step is the price of a snappy demo.

Bonus pitfall from the test port: `double[]` has no `Count` property — the
compiler resolves `arr.Count` to the LINQ extension *method* and your code
fails with `Operator '/' cannot be applied to 'method group'`. Use `Length`,
or keep the variable typed as `IReadOnlyList<T>`.

## Measured hook latency (this machine)

| Hook | bash/python version | C# published binary |
|---|---|---|
| `protect` | 16ms | **12ms** |
| `block-force-push` | 16ms | **13ms** |
| `format-on-edit` | 37ms | **13ms** |
| `audit-bash` | ~15ms | **14ms** |
| `stop-gate` (runs `dotnet test`) | n/a | ~2.9s — that's the test suite, by design |
| `dotnet run hook.cs` (unpublished) | — | ~145ms |

All four fast hooks are in the same noise band as bash — startup cost is a
non-issue once published. Teach the table: *hooks must be cheap because some
events fire on every keystroke-equivalent action.*

## Demo runbooks

### Demo 1a — Guardrail: protected files (≈3 min)

**Setup:** none.
**Prompt:** *"Add a `phone` field to `src/generated/schema.json`."*

**Expected:** Claude's Edit is denied before it runs. The stderr message
("generated code — edit the generator instead") comes back as Claude's
feedback, and Claude pivots to explaining why it can't.

**Talk track:**
- Fires *before* the tool runs — nothing touched the file.
- `return 2` = block; stderr = what Claude reads. That's the whole simple contract.
- Compare with `PostToolUse` later: by then it's too late to prevent anything.

### Demo 1b — Guardrail: force-push deny (≈4 min)

**Prompt:** *"Force-push this branch with `git push --force origin main`."*

**Expected:** Denied with a structured JSON `permissionDecision: "deny"` and a
reason telling Claude to push normally or open a PR.

**Talk track — the two-stage filter:**
1. `matcher: "Bash"` — only Bash tool calls enter this entry.
2. `if: "Bash(git push *)"` — `block-force-push.cs` doesn't even execute unless
   the command contains `git push` (Claude Code finds it inside `$(...)` and
   backticks too — demo with *"run `echo $(git push --force)`"*).
3. The `Regex` inside the hook is the last line of defense — it catches
   `--force`, `--force-with-lease`, `-f`, and combinations like
   `git push origin main && echo $(git push --force)`.

**Also show:** `git push origin force` (branch named `force`) and
`git push --follow-tags` are allowed — matchers and patterns must be precise.

**Two contracts compared:** demo 1a used `return 2` + stderr; this one prints
`hookSpecificOutput.permissionDecision`. The structured form can also *allow*
or *ask* — `return 2` can only refuse.

### Demo 2 — Auto-format after every edit (≈5 min)

**Setup:** note the current state first:

```bash
git diff -- src/Utils.cs    # clean tree (everything staged)
cat -A src/Utils.cs | head  # trailing whitespace visible as $
```

**Prompt:** *"Add a `Truncate(string s, int max)` helper to `src/Utils.cs`.
Don't touch anything else."*

**Expected:** Claude's edit lands, then the `PostToolUse` hook rewrites the
*whole file* — collapsing the seeded blank-line runs, stripping trailing
whitespace, adding the final newline. `git diff` shows whitespace changes
Claude never made.

**Talk track:**
- The `additionalContext` JSON output tells Claude what the hook did — watch
  for Claude acknowledging it.
- "Formatting happened *because of a rule*, not because Claude remembered a
  style preference." Deterministic > probabilistic.
- Real-world swap: point the hook command at your formatter of choice (for
  .NET, `dotnet format` needs a solution in the repo — note this demo repo
  deliberately has none; the built-in normalizer keeps setup dependency-free).

### Demo 3 — Stop-gate: tests must be green (≈6 min)

**Setup:** `scripts/reset.sh` (restores the two seeded bugs in `src/Calc.cs`
and re-enables the gate). Tests are RED on purpose:

```bash
dotnet test tests/Calc.Tests   # 4 failing (Add subtracts; Median never sorts)
```

**Prompt:** *"Make the test suite pass."*

**Expected:** Claude fixes the bugs in `src/Calc.cs`. If it tries to end its
turn while tests are still red (a build error counts — the hook sees any
non-zero `dotnet test` exit), the `Stop` hook blocks it with the failing test
output as the reason — Claude keeps working. On the first *blocked* stop,
`stop_hook_active` comes back `true` on the next attempt, and the hook allows
it: that loop guard is the official pattern and the difference between a speed
bump and a trap (there's also a hard cap, `CLAUDE_CODE_STOP_HOOK_BLOCK_CAP`).

**Talk track:**
- `Stop` is the "you're not done yet" hook — completion criteria enforced by
  the environment, not by asking nicely in the prompt.
- The hook shells out to `dotnet test` (~3s) — hooks can be slow-ish when the
  *work* they check is slow; what matters is the decision, not the latency.
- Kill-switch for live demos: `touch .claude/stop-gate.disabled`
  (remove it after; it's gitignored).
- The green path matters too: when tests pass, the hook says nothing and
  Claude ends normally — hooks should be silent when they have no opinion.

### Bonus — Audit log (≈2 min)

After any of the demos:

```bash
cat .claude/logs/bash-audit.log
```

One JSON line per Bash command Claude ran, with timestamps. Registered with
`async: true` so it runs in the background and never slows the session.
This is the "observability" use case: hooks as a telemetry bus (ship these
lines to Slack/Splunk/whatever via a webhook instead of a file).

## Mapping to the session agenda

| Agenda slot | Demo |
|---|---|
| 1. Why hooks | The prompt-vs-hook one-liner; run selftest to show deterministic behavior |
| 2. Lifecycle map | Walk `.claude/settings.json` as the physical artifact of the diagram |
| 3. Anatomy | `matcher` vs `if`, stdin JSON, the two output contracts (1a vs 1b) |
| 4. Live demos | 1a → 1b → 2 → 3 (bonus if time) |
| 5. Use-case gallery | The five hooks are one instance of each category (guardrail / quality / gate / observability) |
| 6. Team rollout & safety | `.claude/settings.json` is committed; security section below |

## Debugging recipe (teach this — everyone will need it)

1. **Validate the config:** `python3 -m json.tool .claude/settings.json`
2. **Run the binary by hand with a fake payload:**

   ```bash
   echo '{"hook_event_name":"PreToolUse","tool_name":"Edit",
          "tool_input":{"file_path":"'$PWD'/src/generated/schema.json"}}' \
     | .claude/hooks/bin/protect
   echo "exit=$?"
   ```

3. **Watch hooks fire live:** `claude --debug` (or `--debug-file /tmp/claude.log`)
   and `Ctrl+O` to view the transcript; `/hooks` shows what's configured.
4. **Hook not firing?** Check `.claude/hooks/bin/` exists — the #1 cause is a
   fresh clone where `scripts/setup.sh` hasn't run yet (settings.json points
   at binaries that aren't built).
5. Hook stdout that doesn't start with `{` is treated as plain text, not a
   decision. If your JSON "does nothing," a stray `Console.WriteLine`
   debugging line (or `.bashrc` noise in the bash days) is prefixing it.

## Reset between runs

```bash
scripts/reset.sh           # restores seeded bugs/mess, clears audit log, re-enables stop-gate
python3 scripts/selftest.py  # verify
```

## Security (the section not to skip)

- **Hooks are arbitrary code execution on whoever opens the repo.** Claude
  Code requires workspace trust before project hooks run, but treat any change
  to `.claude/settings.json` or `.claude/hooks/*.cs` like a change to CI: review it.
- What teammates run is what they can read: the repo ships C# source only —
  binaries are built locally by `setup.sh` from that reviewed source, never
  committed.
- Committing hook config to the repo means every teammate inherits it — that's
  the feature and the risk. Org-wide, managed policy settings can lock this
  down (`allowManagedHooksOnly`).
- Emergency off switch: `claude --settings '{"disableAllHooks": true}'`.
- Never write hooks that echo secrets; hook input/output lands in debug logs.

## Beyond this session (for the curious)

- Five hook types: `command` (what this repo uses), `http`, `mcp_tool`,
  `prompt`, `agent` — a `prompt` hook on `Stop` can ask a model "is the work
  really done?" before allowing the turn to end.
- ~40 lifecycle events: `SessionStart` (re-inject context after compaction),
  `UserPromptSubmit` (validate/augment prompts), `PermissionRequest`
  (auto-approve specific prompts), `PreCompact`, `CwdChanged` (direnv)…
- Async hooks (`"async": true`) and `asyncRewake` for background work.
- Hook locations: `~/.claude/settings.json` (you, all projects),
  `.claude/settings.json` (this repo, shared), `.claude/settings.local.json`
  (you, this repo, gitignored), managed policy (org).

Official references:
- https://code.claude.com/docs/en/hooks (full reference)
- https://code.claude.com/docs/en/hooks-guide (guide + cookbook)
