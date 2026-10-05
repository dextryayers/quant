using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Quant.Desktop.Models;

namespace Quant.Desktop.Services;

public sealed class SymbolService
{
    private static readonly (string Kind, Regex Rx)[] Rules =
    {
        ("class", new Regex(@"^\s*(public\s+)?(class|struct|interface|enum)\s+(\w+)", RegexOptions.Compiled)),
        ("function", new Regex(@"^\s*(fn|func|function|def)\s+(\w+)", RegexOptions.Compiled)),
        ("method", new Regex(@"^\s*(public|private|protected)?\s*(async\s+)?[\w<>\[\]]+\s+(\w+)\s*\(", RegexOptions.Compiled)),
    };

    public List<SymbolItem> FromText(string file, string[] lines)
    {
        var list = new List<SymbolItem>();
        for (var i = 0; i < lines.Length && list.Count < 500; i++)
        {
            foreach (var (kind, rx) in Rules)
            {
                var m = rx.Match(lines[i]);
                if (!m.Success) continue;
                var name = m.Groups[^1].Value;
                if (name is "if" or "for" or "return" or "using") continue;
                list.Add(new SymbolItem { Name = name, Kind = kind, File = file, Line = i + 1, Preview = lines[i].Trim() });
                break;
            }
        }
        return list;
    }

    public List<SymbolItem> Search(string root, string query, int max = 30)
    {
        var outList = new List<SymbolItem>();
        if (string.IsNullOrWhiteSpace(query) || !Directory.Exists(root)) return outList;
        var files = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".rs") || f.EndsWith(".cs") || f.EndsWith(".ts") || f.EndsWith(".py") || f.EndsWith(".go"))
            .Take(500);
        foreach (var f in files)
        {
            string[] lines;
            try { lines = File.ReadAllLines(f); } catch { continue; }
            foreach (var s in FromText(f, lines))
            {
                if (s.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    outList.Add(s);
                    if (outList.Count >= max) return outList;
                }
            }
        }
        return outList;
    }
}
