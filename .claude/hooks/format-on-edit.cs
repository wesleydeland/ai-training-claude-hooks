// Demo 2 — PostToolUse auto-formatter.
//
// Reads the hook input JSON from stdin. If Claude just edited a text file,
// normalizes whitespace across the WHOLE file and reports back via PostToolUse
// `additionalContext`, so Claude (and you, in --debug) can see what the hook did.
//
// Normalizations: strip trailing whitespace, collapse runs of blank lines to
// one, guarantee exactly one final newline. Dependency-free — swap in
// `npx prettier --write` for real projects.
using System.Text;
using System.Text.Json;

static string Normalize(string text)
{
    var result = new List<string>();
    var blankRun = 0;
    foreach (var line in text.Split('\n'))
    {
        var trimmed = line.TrimEnd(); // also drops a trailing \r (CRLF -> LF)
        if (trimmed.Length == 0)
        {
            blankRun++;
            if (blankRun > 1)
            {
                continue; // collapse runs of blank lines
            }
        }
        else
        {
            blankRun = 0;
        }
        result.Add(trimmed);
    }

    while (result.Count > 0 && result[^1].Length == 0)
    {
        result.RemoveAt(result.Count - 1); // no trailing blank lines
    }

    return string.Join("\n", result) + "\n";
}

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
    return 0;
}

path = path.Replace('\\', '/');
if (path.Length == 0 || !File.Exists(path))
{
    return 0;
}
if (path.Contains("/.git/", StringComparison.Ordinal) || path.EndsWith("/.git", StringComparison.Ordinal))
{
    return 0;
}

string original;
try
{
    if (new FileInfo(path).Length > 1_000_000)
    {
        return 0;
    }
    // strict UTF-8: binary/unreadable files raise DecoderFallbackException
    using var reader = new StreamReader(path, new UTF8Encoding(false, throwOnInvalidBytes: true));
    original = reader.ReadToEnd();
}
catch (DecoderFallbackException)
{
    return 0;
}
catch (IOException)
{
    return 0;
}

if (original.Length == 0)
{
    return 0;
}

var normalized = Normalize(original);
if (normalized == original)
{
    return 0; // already clean — hooks stay silent when they have no opinion
}

try
{
    File.WriteAllText(path, normalized);
}
catch (IOException)
{
    return 0;
}
catch (UnauthorizedAccessException)
{
    return 0;
}

var rel = Path.GetRelativePath(Directory.GetCurrentDirectory(), path);
var msg =
    $"Formatting hook normalized {rel}: stripped trailing whitespace, "
    + "collapsed blank-line runs, ensured a single trailing newline.";
Console.WriteLine(
    $"{{\"hookSpecificOutput\":{{\"hookEventName\":\"PostToolUse\",\"additionalContext\":\"{Esc(msg)}\"}}}}");
return 0;
