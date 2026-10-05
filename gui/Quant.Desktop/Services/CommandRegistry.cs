using System;
using System.Collections.Generic;
using System.Linq;
using Quant.Desktop.Models;

namespace Quant.Desktop.Services;

public sealed class CommandRegistry
{
    private readonly List<CommandItem> _all = new()
    {
        new() { Id = "palette.open", Title = "Open Command Palette", Group = "General", Hint = "Ctrl+Shift+P" },
        new() { Id = "explorer.focus", Title = "Focus Explorer", Group = "Explorer", Hint = "Ctrl+Shift+E" },
        new() { Id = "explorer.newFile", Title = "Explorer: New File", Group = "Explorer", Hint = "Ctrl+N" },
        new() { Id = "explorer.newFolder", Title = "Explorer: New Folder", Group = "Explorer", Hint = "Ctrl+Shift+N" },
        new() { Id = "explorer.rename", Title = "Explorer: Rename", Group = "Explorer", Hint = "F2" },
        new() { Id = "view.toggleSide", Title = "View: Toggle Side Panel", Group = "View", Hint = "Ctrl+B" },
        new() { Id = "view.toggleBottom", Title = "View: Toggle Bottom Panel", Group = "View", Hint = "Ctrl+J" },
        new() { Id = "view.toggleRight", Title = "View: Toggle Assistant", Group = "View", Hint = "Ctrl+Alt+B" },
        new() { Id = "view.theme", Title = "View: Toggle Theme", Group = "View", Hint = "" },
        new() { Id = "editor.saveAll", Title = "File: Save All", Group = "File", Hint = "Ctrl+K S" },
        new() { Id = "chat.new", Title = "Chat: New Thread", Group = "Chat", Hint = "Ctrl+Alt+N" },
        new() { Id = "chat.stop", Title = "Chat: Stop Streaming", Group = "Chat", Hint = "Esc" },
        new() { Id = "engine.check", Title = "Assistant: Check status", Group = "Assistant", Hint = "" },
        new() { Id = "engine.models", Title = "Assistants: Show list", Group = "Assistant", Hint = "" },
        new() { Id = "workspace.openFolder", Title = "Workspace: Open Folder", Group = "Workspace", Hint = "Ctrl+K Ctrl+O" },
        new() { Id = "echo.hello", Title = "Echo: Hello", Group = "Sample", Hint = "" },
        new() { Id = "echo.time", Title = "Echo: Time", Group = "Sample", Hint = "" },
        new() { Id = "launch.start", Title = "Launch: Start Selected", Group = "Run", Hint = "" },
        new() { Id = "preview.toggle", Title = "View: Toggle Preview", Group = "View", Hint = "" },
    };

    public IReadOnlyList<CommandItem> Filter(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return _all.Take(8).ToList();
        var q = query.Trim().ToLowerInvariant();
        return _all
            .Select(c => (c, score: Score(c, q)))
            .Where(t => t.score > 0)
            .OrderByDescending(t => t.score)
            .Select(t => t.c)
            .Take(12)
            .ToList();
    }

    private static int Score(CommandItem c, string q)
    {
        var title = c.Title.ToLowerInvariant();
        var id = c.Id.ToLowerInvariant();
        if (title.StartsWith(q, StringComparison.Ordinal)) return 100;
        if (id.StartsWith(q, StringComparison.Ordinal)) return 90;
        if (title.Contains(q, StringComparison.Ordinal)) return 50;
        // fuzzy subsequence
        var ti = 0;
        foreach (var ch in q)
        {
            ti = title.IndexOf(ch, ti);
            if (ti < 0) return 0;
            ti++;
        }
        return 20;
    }
}
