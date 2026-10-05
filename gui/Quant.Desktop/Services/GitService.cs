using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Quant.Desktop.Models;

namespace Quant.Desktop.Services;

public sealed class GitService
{
    private static async Task<string> Run(string cwd, string args, int timeoutMs = 15000)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = args,
                WorkingDirectory = cwd,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p == null) return "";
            var task = p.StandardOutput.ReadToEndAsync();
            var done = await Task.WhenAny(task, Task.Delay(timeoutMs)).ConfigureAwait(false);
            if (done != task) { try { p.Kill(true); } catch { } return ""; }
            try { p.WaitForExit(2000); } catch { }
            return await task.ConfigureAwait(false);
        }
        catch { return ""; }
    }

    public static async Task<string> Branch(string cwd)
    {
        var b = (await Run(cwd, "branch --show-current").ConfigureAwait(false)).Trim();
        return string.IsNullOrEmpty(b) ? "no git" : b;
    }

    public static async Task<string> AheadBehind(string cwd)
    {
        var s = (await Run(cwd, "rev-list --left-right --count HEAD...@{upstream}").ConfigureAwait(false)).Trim();
        if (string.IsNullOrEmpty(s)) return "";
        var parts = s.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2) return $"ahead {parts[0]} behind {parts[1]}";
        return "";
    }

    public static async Task<List<GitFile>> Status(string cwd)
    {
        var outList = new List<GitFile>();
        var s = await Run(cwd, "status --porcelain=v1 -uall").ConfigureAwait(false);
        foreach (var line in s.Split('\n'))
        {
            if (line.Length < 4) continue;
            outList.Add(new GitFile { Status = line[..2].Trim(), Path = line[3..].Trim().Trim('"') });
            if (outList.Count >= 2000) break;
        }
        return outList;
    }

    public static Task<string> Diff(string cwd, string file)
        => Run(cwd, $"diff -- \"{file.Replace("\"", "")}\"");

    public static async Task<bool> Stage(string cwd, string file)
    {
        var s = await Run(cwd, $"add -- \"{file.Replace("\"", "")}\"").ConfigureAwait(false);
        return true;
    }

    public static async Task<string> Commit(string cwd, string message)
    {
        var safe = message.Replace("\"", "").Replace('\n', ' ');
        if (safe.Length > 200) safe = safe[..200];
        var err = await Run(cwd, $"commit -m \"{safe}\"").ConfigureAwait(false);
        return err;
    }

    public static async Task<List<GitLog>> Log(string cwd, string query = "", int max = 50)
    {
        var outList = new List<GitLog>();
        var s = await Run(cwd, $"log --pretty=format:%H%x1f%s%x1f%ad --date=short -n {max}").ConfigureAwait(false);
        foreach (var line in s.Split('\n'))
        {
            var parts = line.Split('\x1f');
            if (parts.Length < 3) continue;
            if (!string.IsNullOrEmpty(query) && !parts[1].Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
            outList.Add(new GitLog { Hash = parts[0][..Math.Min(8, parts[0].Length)], Message = parts[1], Date = parts[2] });
        }
        return outList;
    }

    public static Task<string> Show(string cwd, string hash, string file)
        => Run(cwd, $"show {hash}:\"{file.Replace("\"", "")}\"");
}
