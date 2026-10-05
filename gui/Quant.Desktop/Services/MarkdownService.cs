using System.Linq;
using System.Text.RegularExpressions;

namespace Quant.Desktop.Services;

/// Lightweight local preview without webview. Headings, lists, code fences preserved.
public static class MarkdownService
{
    public static string ToPreview(string markdown)
    {
        var lines = markdown.Split('\n');
        var outLines = new System.Collections.Generic.List<string>();
        var inCode = false;
        foreach (var raw in lines)
        {
            var l = raw.TrimEnd();
            if (l.TrimStart().StartsWith("```"))
            {
                inCode = !inCode;
                outLines.Add(inCode ? "[code]" : "[/code]");
                continue;
            }
            if (inCode) { outLines.Add("  " + l); continue; }
            if (l.StartsWith("### ")) outLines.Add(l[4..].ToUpperInvariant());
            else if (l.StartsWith("## ")) outLines.Add(l[3..].ToUpperInvariant());
            else if (l.StartsWith("# ")) outLines.Add(l[2..].ToUpperInvariant());
            else if (l.TrimStart().StartsWith("- ") || l.TrimStart().StartsWith("* "))
                outLines.Add("  - " + l.TrimStart()[2..]);
            else if (Regex.IsMatch(l.TrimStart(), @"^\d+\. "))
                outLines.Add("  " + l.Trim());
            else
            {
                var t = l;
                t = Regex.Replace(t, @"\[(.*?)\]\((.*?)\)", "$1 ($2)");
                t = Regex.Replace(t, @"\*\*(.*?)\*\*", "$1");
                t = Regex.Replace(t, @"`(.*?)`", "$1");
                outLines.Add(t);
            }
            if (outLines.Count >= 5000) break;
        }
        return string.Join('\n', outLines.Take(5000));
    }

    public static bool IsPreviewable(string path)
    {
        var e = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return e is ".md" or ".markdown" or ".html" or ".htm";
    }
}
