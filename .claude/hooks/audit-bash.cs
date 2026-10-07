// Bonus — PostToolUse audit log: every Bash command Claude ran is appended to
// .claude/logs/bash-audit.log as one JSON line per event.
//
// Registered with "async": true in settings, so it runs in the background and
// never slows the session down.
using System.Text.Json;

string input = Console.In.ReadToEnd();
string projectDir = Environment.GetEnvironmentVariable("CLAUDE_PROJECT_DIR")
    ?? Directory.GetCurrentDirectory();

try
{
    using JsonDocument doc = JsonDocument.Parse(input);
    if (!doc.RootElement.TryGetProperty("tool_input", out JsonElement ti)
        || !ti.TryGetProperty("command", out JsonElement ci)
        || ci.ValueKind != JsonValueKind.String)
    {
        return 0;
    }

    // GetRawText() returns the string as valid JSON, escaping included —
    // no serializer (or escaper) needed.
    var commandJson = ci.GetRawText();
    var ts = DateTime.UtcNow.ToString("o");

    var logDir = Path.Combine(projectDir, ".claude", "logs");
    Directory.CreateDirectory(logDir);
    File.AppendAllText(
        Path.Combine(logDir, "bash-audit.log"),
        $"{{\"ts\":\"{ts}\",\"command\":{commandJson}}}\n");
}
catch (JsonException)
{
    return 0;
}
catch (IOException)
{
    return 0;
}

return 0;
