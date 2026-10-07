// Demo 1a — PreToolUse guardrail, SIMPLE contract: exit 2 blocks the tool call,
// stderr becomes the feedback Claude sees.
//
// Registered in .claude/settings.json with matcher "Edit|Write".
using System.Text.Json;

string input = Console.In.ReadToEnd();
string path;
try
{
    using JsonDocument doc = JsonDocument.Parse(input);
    path = doc.RootElement.TryGetProperty("tool_input", out JsonElement ti)
        && ti.TryGetProperty("file_path", out JsonElement fp)
            ? fp.GetString() ?? ""
            : "";
}
catch (JsonException)
{
    return 0; // malformed payload — never crash a hook
}

path = path.Replace('\\', '/');
if (path.Contains("/src/generated/", StringComparison.Ordinal))
{
    Console.Error.WriteLine(
        $"Blocked by demo hook: {path} is generated code. Edit the generator input and regenerate instead.");
    return 2;
}

return 0; // no opinion — the normal permission flow continues
