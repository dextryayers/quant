using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Quant.Desktop.Models;

namespace Quant.Desktop.Services;

public sealed class WorkspaceService
{
    private readonly string _globalDir;
    private readonly string _recentPath;
    public List<WorkspaceRoot> Roots { get; } = new();
    public List<string> Recent { get; } = new();
    public string? ActiveRoot => Roots.FirstOrDefault()?.Path;

    public WorkspaceService()
    {
        _globalDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Quant");
        Directory.CreateDirectory(_globalDir);
        _recentPath = Path.Combine(_globalDir, "recent.json");
        LoadRecent();
        // Default root for V0.1.0: repo itself if no saved state, so Explorer is never empty.
        var fallback = FindRepoRoot();
        if (Directory.Exists(fallback) && Roots.Count == 0)
            Roots.Add(new WorkspaceRoot { Path = fallback, Name = Path.GetFileName(fallback), Trusted = true });
    }

    public void AddRoot(string path, bool trusted = true)
    {
        path = Path.GetFullPath(path);
        if (Roots.Any(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase))) return;
        Roots.Add(new WorkspaceRoot { Path = path, Name = new DirectoryInfo(path).Name, Trusted = trusted });
        PushRecent(path);
    }

    public void RemoveRoot(string path)
    {
        Roots.RemoveAll(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));
    }

    public bool IsTrusted(string path)
    {
        var root = Roots.FirstOrDefault(r => path.StartsWith(r.Path, StringComparison.OrdinalIgnoreCase));
        return root?.Trusted ?? false;
    }

    public void SetTrust(string path, bool trusted)
    {
        var root = Roots.FirstOrDefault(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));
        if (root != null) root.Trusted = trusted;
    }

    private void PushRecent(string path)
    {
        Recent.Remove(path);
        Recent.Insert(0, path);
        while (Recent.Count > 15) Recent.RemoveAt(Recent.Count - 1);
        try { File.WriteAllText(_recentPath, JsonSerializer.Serialize(Recent)); } catch { }
    }

    private void LoadRecent()
    {
        try
        {
            if (!File.Exists(_recentPath)) return;
            var list = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_recentPath));
            if (list != null) Recent.AddRange(list.Take(15));
        }
        catch { }
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && dir != null; i++)
        {
            if (File.Exists(Path.Combine(dir, "Quant.slnx"))) return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        return Directory.GetCurrentDirectory();
    }
}
