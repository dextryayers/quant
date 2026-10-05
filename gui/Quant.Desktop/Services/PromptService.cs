using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Quant.Desktop.Services;

public sealed class PromptService
{
    public Dictionary<string, string> Custom { get; } = new();

    public void Refresh(string workspaceRoot)
    {
        Custom.Clear();
        try
        {
            var dir = Path.Combine(workspaceRoot, ".quant", "prompts");
            if (!Directory.Exists(dir)) return;
            foreach (var f in Directory.GetFiles(dir, "*.md").Take(50))
                Custom["/" + Path.GetFileNameWithoutExtension(f)] = File.ReadAllText(f);
        }
        catch { }
    }

    public string Expand(string input, string selection, string file)
    {
        if (!input.StartsWith("/")) return input;
        var cmd = input.Split(' ')[0].ToLowerInvariant();
        var rest = input.Contains(' ') ? input[(input.IndexOf(' ') + 1)..] : "";
        string body = cmd switch
        {
            "/explain" => $"Explain this code with citations. Code:\n{Sel(selection, file, rest)}",
            "/fix" => $"Find bugs and propose a minimal diff. Code:\n{Sel(selection, file, rest)}",
            "/refactor" => $"Refactor for clarity without behavior change. List risks. Code:\n{Sel(selection, file, rest)}",
            "/tests" => $"Generate unit tests with edge cases. Code:\n{Sel(selection, file, rest)}",
            "/docs" => $"Write concise docs and comments. Code:\n{Sel(selection, file, rest)}",
            "/commit-msg" => $"Write a conventional commit message from this diff:\n{Sel(selection, file, rest)}",
            "/review" => $"Review for bugs, perf, security with file lines. Code:\n{Sel(selection, file, rest)}",
            "/plan" => $"Break into steps with file list before edits. Task: {rest}",
            _ when Custom.TryGetValue(cmd, out var t) => t.Replace("{{selection}}", selection).Replace("{{file}}", file).Replace("{{input}}", rest),
            _ => input,
        };
        return body;
    }

    private static string Sel(string selection, string file, string rest)
    {
        var s = string.IsNullOrWhiteSpace(selection) ? rest : selection;
        return string.IsNullOrWhiteSpace(file) ? s : $"File {file}:\n{s}";
    }

    public static bool IsLocalCommand(string input, out string name)
    {
        name = "";
        if (!input.StartsWith("/")) return false;
        name = input.Split(' ')[0].ToLowerInvariant();
        return name is "/context-clear" or "/index-refresh" or "/model-load";
    }
}
