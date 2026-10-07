// Utility helpers — deliberately messy formatting for the auto-format demo.   
//
// Trailing whitespace, runaway blank lines, and no final newline are all seeded
// on purpose. After demo 2, `git diff` shows what the PostToolUse formatter
// cleaned up behind Claude's back.



using System.Text.RegularExpressions;

namespace DemoUtils;

public static class TextUtils
{
    public static string Slugify(string text)   
    {
        text = text.ToLowerInvariant().Trim();
        text = Regex.Replace(text, "[^a-z0-9]+", "-");
        return text.Trim('-');
    }



    public static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var result = string.Empty;
        foreach (var part in parts)
        {
            result += char.ToUpperInvariant(part[0]);
        }
        return result;
    }
}