// Demo 3 — Stop hook: blocks Claude from ending the turn while tests are red.
// Structured contract: {"decision": "block", "reason": "..."} — the reason is
// fed back to Claude, which then keeps working.
using System.Diagnostics;
using System.Text;
using System.Text.Json;

static string Esc(string s)
{
    var sb = new StringBuilder(s.Length + 8);
    foreach (var c in s)
    {
        switch (c)
        {
            case '"': sb.Append("\\\""); break;
            case '\\': sb.Append("\\\\"); break;
            case '\n': sb.Append("\\n"); break;
            case '\r': sb.Append("\\r"); break;
            case '\t': sb.Append("\\t"); break;
            default:
                sb.Append(c < 0x20 ? $"\\u{(int)c:x4}" : c.ToString());
                break;
        }
    }
    return sb.ToString();
}

string input = Console.In.ReadToEnd();
string projectDir = Environment.GetEnvironmentVariable("CLAUDE_PROJECT_DIR")
    ?? Directory.GetCurrentDirectory();

// Live-demo kill-switch: `touch .claude/stop-gate.disabled` to turn this off.
if (File.Exists(Path.Combine(projectDir, ".claude", "stop-gate.disabled")))
{
    return 0;
}

bool stopHookActive;
string cwd = projectDir;
try
{
    using JsonDocument doc = JsonDocument.Parse(input);
    stopHookActive = doc.RootElement.TryGetProperty("stop_hook_active", out JsonElement s)
        && s.ValueKind == JsonValueKind.True;
    if (doc.RootElement.TryGetProperty("cwd", out JsonElement c))
    {
        cwd = c.GetString() ?? projectDir;
    }
}
catch (JsonException)
{
    return 0;
}

// Loop guard (the official pattern): if Claude is already continuing because a
// Stop hook blocked it, allow this stop so it can never be trapped forever.
if (stopHookActive)
{
    return 0;
}

var psi = new ProcessStartInfo("dotnet", "test tests/Calc.Tests")
{
    WorkingDirectory = cwd,
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    RedirectStandardInput = true,
};

try
{
    using var proc = Process.Start(psi);
    if (proc is null)
    {
        return 0; // couldn't start dotnet — fail open, never trap the session
    }
    proc.StandardInput.Close();
    var stdoutTask = proc.StandardOutput.ReadToEndAsync();
    var stderrTask = proc.StandardError.ReadToEndAsync();
    proc.WaitForExit();
    var stdout = stdoutTask.GetAwaiter().GetResult();
    var stderr = stderrTask.GetAwaiter().GetResult();

    if (proc.ExitCode == 0)
    {
        return 0; // green — Claude may stop
    }

    var parts = new[] { stdout, stderr }.Where(p => !string.IsNullOrEmpty(p));
    var reason =
        "Stop blocked by demo hook — the test suite is red. Fix the failures before finishing.\n\n"
        + string.Join("\n", parts);
    Console.WriteLine($"{{\"decision\":\"block\",\"reason\":\"{Esc(reason)}\"}}");
    return 0;
}
catch (Exception)
{
    // Never let a hook error break the session — fail open with a note.
    Console.Error.WriteLine("stop-gate: could not run the test suite; allowing stop.");
    return 0;
}
