using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Quant.Desktop.Models;

namespace Quant.Desktop.Services;

public sealed class ThreadService
{
    private readonly string _dir;
    public List<ChatThread> Threads { get; } = new();

    public ThreadService(string workspaceRoot)
    {
        _dir = Path.Combine(workspaceRoot, ".quant", "threads");
        Directory.CreateDirectory(_dir);
        Load();
        if (Threads.Count == 0)
            Threads.Add(new ChatThread { Title = "Welcome", Messages = new List<ChatMessage> { new() { Role = "assistant", Content = "Welcome to Quant. Attach a file or ask about the open editor." } } });
    }

    public ChatThread Active => Threads.First();

    public void New(string title = "New thread")
    {
        Threads.Insert(0, new ChatThread { Title = title });
        Save();
    }

    public void Save()
    {
        try
        {
            foreach (var t in Threads.Take(20))
                File.WriteAllText(Path.Combine(_dir, t.Id + ".json"), JsonSerializer.Serialize(t));
        }
        catch { }
    }

    public string Export(ChatThread t)
    {
        var path = Path.Combine(_dir, t.Id + ".md");
        var md = $"# {t.Title}\n\n" + string.Join("\n\n", t.Messages.Select(m => $"**{m.Role}**\n\n{m.Content}"));
        // Strip naive secret patterns on export
        md = md.Replace("sk-", "sk-***");
        File.WriteAllText(path, md);
        return path;
    }

    private void Load()
    {
        try
        {
            foreach (var f in Directory.GetFiles(_dir, "*.json").Take(20))
            {
                var t = JsonSerializer.Deserialize<ChatThread>(File.ReadAllText(f));
                if (t != null) Threads.Add(t);
            }
            Threads.Sort((a, b) => b.Updated.CompareTo(a.Updated));
        }
        catch { }
    }
}
