using System.Collections.Generic;
using System.Text.RegularExpressions;
using Quant.Desktop.Models;

namespace Quant.Desktop.Services;

public sealed class ProblemService
{
    private static readonly (Regex Rx, string Source)[] Matchers =
    {
        (new Regex(@"^(?<file>.+?)\((?<line>\d+),(?<col>\d+)\):\s+(?<sev>error|warning)\s+(?<msg>.+)$", RegexOptions.Compiled), "dotnet"),
        (new Regex(@"^error(\[(?<code>E\d+)\])?:\s+(?<msg>.+?)\s+-->\s+(?<file>.+?):(?<line>\d+):(?<col>\d+)", RegexOptions.Compiled), "cargo"),
        (new Regex(@"^(?<file>.+?)\((?<line>\d+),(?<col>\d+)\):\s+TS(?<code>\d+):\s+(?<msg>.+)$", RegexOptions.Compiled), "tsc"),
    };

    public List<ProblemItem> Parse(string log, string source = "task")
    {
        var list = new List<ProblemItem>();
        foreach (var raw in log.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            var matched = false;
            foreach (var (rx, src) in Matchers)
            {
                var m = rx.Match(line);
                if (!m.Success) continue;
                list.Add(new ProblemItem
                {
                    Severity = m.Groups["sev"].Success ? m.Groups["sev"].Value : "error",
                    Message = m.Groups["msg"].Value.Trim(),
                    File = m.Groups["file"].Value.Trim(),
                    Line = int.TryParse(m.Groups["line"].Value, out var l) ? l : 1,
                    Col = int.TryParse(m.Groups["col"].Value, out var c) ? c : 1,
                    Source = src,
                });
                matched = true;
                break;
            }
            if (!matched && line.Contains("error", System.StringComparison.OrdinalIgnoreCase) && list.Count < 5000)
                list.Add(new ProblemItem { Severity = "error", Message = line.Length > 220 ? line[..220] : line, File = "", Line = 1, Source = source });
            if (list.Count >= 5000) break;
        }
        return list;
    }
}
