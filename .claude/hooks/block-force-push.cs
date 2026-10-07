// Demo 1b — PreToolUse guardrail, STRUCTURED contract: a permissionDecision
// JSON object on stdout.
//
// The settings file narrows the audience BEFORE this code runs:
//   matcher "Bash"          -> only Bash tool calls reach it
//   if "Bash(git push *)"   -> only git push commands execute it
//                              (Claude Code even catches `echo $(git push ...)`)
using System.Text.Json;
using System.Text.RegularExpressions;

string input = Console.In.ReadToEnd();
string command;
try
{
    using JsonDocument doc = JsonDocument.Parse(input);
    command = doc.RootElement.TryGetProperty("tool_input", out JsonElement ti)
        && ti.TryGetProperty("command", out JsonElement ci)
            ? ci.GetString() ?? ""
            : "";
}
catch (JsonException)
{
    return 0;
}

// Force flags: --force, --force-with-lease[=refspec], or a bare -f as its own
// word. Boundaries include ) ; & | and end-of-string so `git push --force)`
// inside $() and `git push --force && ...` are caught too.
const string ForcePattern =
    @"(^|[\s;&|)])-{1,2}force(-with-lease(=[^\s]*)?)?([\s;&|)]|$)"
    + @"|(^|[\s;&|)])-f([\s;&|)]|$)";

if (Regex.IsMatch(command, ForcePattern))
{
    // Hand-written JSON: published file-based apps run trimmed/AOT, where the
    // reflection-based JsonSerializer is disabled. Input parsing via
    // JsonDocument is fine — only Serialize needs reflection.
    Console.WriteLine(
        """{"hookSpecificOutput":{"hookEventName":"PreToolUse","permissionDecision":"deny","permissionDecisionReason":"Force pushes are blocked by the demo hook. Push normally, or open a branch and a PR instead."}}""");
}

return 0;
