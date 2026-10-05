using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Quant.Desktop.Models;

namespace Quant.Desktop.Services;

public sealed class SearchService
{
    public List<string> History { get; } = new();
    public int LastScanned { get; private set; }
    public int LastCapped { get; private set; }

    private static readonly string[] SkipDirs = { ".git", "node_modules", "target", "bin", "obj", "dist", "build", ".quant" };

    public async Task<List<SearchResult>> SearchAsync(
        string root,
        string query,
        bool regex,
        bool caseSensitive,
        bool wholeWord,
        int maxHits,
        CancellationToken ct,
        Action<List<SearchResult>>? onBatch = null)
    {
        var results = new ConcurrentBag<SearchResult>();
        if (string.IsNullOrWhiteSpace(query) || !Directory.Exists(root)) return new List<SearchResult>();
        PushHistory(query);

        Regex? rx = null;
        if (regex)
        {
            try { rx = new Regex(query, caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase); }
            catch { return new List<SearchResult>(); }
        }
        var cmp = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => !SkipDirs.Any(d => f.Contains(Path.DirectorySeparatorChar + d + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            .Take(20000)
            .ToList();
        LastScanned = files.Count;
        var batch = new List<SearchResult>();
        var opts = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = ct };

        await Task.Run(() =>
        {
            Parallel.ForEach(files, opts, file =>
            {
                if (results.Count >= maxHits) return;
                string[] lines;
                try
                {
                    var fi = new FileInfo(file);
                    if (fi.Length > 2_000_000) return;
                    lines = File.ReadAllLines(file);
                }
                catch { return; }
                for (var i = 0; i < lines.Length; i++)
                {
                    if (results.Count >= maxHits) break;
                    var line = lines[i];
                    bool hit;
                    int col = 0;
                    if (rx != null)
                    {
                        var m = rx.Match(line);
                        hit = m.Success;
                        col = hit ? m.Index : 0;
                    }
                    else if (wholeWord)
                    {
                        col = IndexOfWholeWord(line, query, cmp);
                        hit = col >= 0;
                    }
                    else
                    {
                        col = line.IndexOf(query, cmp);
                        hit = col >= 0;
                    }
                    if (hit)
                    {
                        var r = new SearchResult { File = file, Line = i + 1, Col = col + 1, Preview = line.Trim().Length > 220 ? line.Trim()[..220] : line.Trim() };
                        results.Add(r);
                        lock (batch)
                        {
                            batch.Add(r);
                            if (batch.Count >= 50)
                            {
                                onBatch?.Invoke(new List<SearchResult>(batch));
                                batch.Clear();
                            }
                        }
                    }
                }
            });
        }, ct);

        if (batch.Count > 0) onBatch?.Invoke(batch);
        LastCapped = results.Count >= maxHits ? maxHits : 0;
        return results.OrderBy(r => r.File).ThenBy(r => r.Line).Take(maxHits).ToList();
    }

    public int ReplaceInFiles(List<SearchResult> hits, string find, string replace, bool onlySelected)
    {
        var count = 0;
        foreach (var g in hits.Where(h => !onlySelected || h.Selected).GroupBy(h => h.File))
        {
            try
            {
                var backup = g.Key + ".quantbak";
                File.Copy(g.Key, backup, true);
                var lines = File.ReadAllLines(g.Key);
                foreach (var h in g.OrderByDescending(x => x.Line))
                {
                    if (h.Line - 1 < lines.Length)
                    {
                        lines[h.Line - 1] = lines[h.Line - 1].Replace(find, replace, StringComparison.Ordinal);
                        count++;
                    }
                }
                File.WriteAllLines(g.Key, lines);
                File.Delete(backup);
            }
            catch { }
        }
        return count;
    }

    private void PushHistory(string q)
    {
        History.Remove(q);
        History.Insert(0, q);
        while (History.Count > 20) History.RemoveAt(History.Count - 1);
    }

    private static int IndexOfWholeWord(string line, string query, StringComparison cmp)
    {
        var idx = line.IndexOf(query, cmp);
        while (idx >= 0)
        {
            var before = idx == 0 || !char.IsLetterOrDigit(line[idx - 1]);
            var after = idx + query.Length >= line.Length || !char.IsLetterOrDigit(line[idx + query.Length]);
            if (before && after) return idx;
            idx = line.IndexOf(query, idx + 1, cmp);
        }
        return -1;
    }
}
