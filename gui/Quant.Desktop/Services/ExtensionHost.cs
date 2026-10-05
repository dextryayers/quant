using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Quant.Desktop.Models;

namespace Quant.Desktop.Services;

public interface ICommandProvider
{
    IEnumerable<CommandItem> GetCommands();
}

public interface IExplorerMenuProvider
{
    IEnumerable<CommandItem> GetExplorerMenu();
}

public interface IChatToolProvider
{
    IEnumerable<string> GetToolNames();
    string ExecuteTool(string name, string args);
}

public sealed class ExtensionManifest
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "0.1.0";
    public bool Enabled { get; set; } = true;
    public string Source { get; set; } = "builtin";
}

public interface IExtension : ICommandProvider, IExplorerMenuProvider, IChatToolProvider
{
    ExtensionManifest Manifest { get; }
}

public sealed class EchoExtension : IExtension
{
    public ExtensionManifest Manifest { get; } = new() { Id = "echo", Name = "Echo Sample", Version = "0.1.0", Enabled = true, Source = "builtin" };

    public IEnumerable<CommandItem> GetCommands()
    {
        yield return new CommandItem { Id = "echo.hello", Title = "Echo: Hello", Group = "Sample", Hint = "" };
        yield return new CommandItem { Id = "echo.time", Title = "Echo: Time", Group = "Sample", Hint = "" };
    }

    public IEnumerable<CommandItem> GetExplorerMenu()
    {
        yield return new CommandItem { Id = "echo.hello", Title = "Echo hello here", Group = "Sample", Hint = "" };
    }

    public IEnumerable<string> GetToolNames()
    {
        yield return "echo";
    }

    public string ExecuteTool(string name, string args)
    {
        return name switch
        {
            "echo.hello" => $"hello from {args}",
            "echo.time" => DateTime.Now.ToString("o"),
            "echo" => $"echo: {args}",
            _ => throw new InvalidOperationException($"unknown tool {name}"),
        };
    }
}

public sealed class ExtensionHost
{
    private readonly List<IExtension> _extensions = new() { new EchoExtension() };
    public string LastError { get; private set; } = "";
    public event Action<string>? Log;

    public void Register(IExtension ext) => _extensions.Add(ext);

    public void Load(string workspaceRoot)
    {
        try
        {
            var path = Path.Combine(workspaceRoot, ".quant", "extensions.json");
            if (!File.Exists(path)) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("disabled", out var arr)) return;
            var disabled = new HashSet<string>();
            foreach (var d in arr.EnumerateArray())
                if (d.GetString() is string id) disabled.Add(id);
            foreach (var e in _extensions)
                e.Manifest.Enabled = !disabled.Contains(e.Manifest.Id);
        }
        catch (Exception ex) { LastError = ex.Message; }
    }

    public void Save(string workspaceRoot)
    {
        try
        {
            var dir = Path.Combine(workspaceRoot, ".quant");
            Directory.CreateDirectory(dir);
            var disabled = new List<string>();
            foreach (var e in _extensions)
                if (!e.Manifest.Enabled) disabled.Add(e.Manifest.Id);
            File.WriteAllText(Path.Combine(dir, "extensions.json"), JsonSerializer.Serialize(new { disabled }));
        }
        catch (Exception ex) { LastError = ex.Message; }
    }

    public void SetEnabled(string id, bool enabled, string workspaceRoot)
    {
        foreach (var e in _extensions)
            if (e.Manifest.Id == id) e.Manifest.Enabled = enabled;
        Save(workspaceRoot);
    }

    public IEnumerable<CommandItem> AllCommands()
    {
        foreach (var ext in _extensions)
        {
            if (!ext.Manifest.Enabled) continue;
            IEnumerable<CommandItem> items;
            try { items = ext.GetCommands(); }
            catch (Exception ex) { LastError = $"{ext.Manifest.Id}: {ex.Message}"; Log?.Invoke(LastError); continue; }
            foreach (var c in items)
            {
                CommandItem? safe = null;
                try { safe = c; }
                catch (Exception ex) { LastError = ex.Message; continue; }
                if (safe != null) yield return safe;
            }
        }
    }

    public IEnumerable<CommandItem> ExplorerMenu()
    {
        foreach (var ext in _extensions)
        {
            if (!ext.Manifest.Enabled) continue;
            IEnumerable<CommandItem> items;
            try { items = ext.GetExplorerMenu(); }
            catch (Exception ex) { LastError = $"{ext.Manifest.Id}: {ex.Message}"; continue; }
            foreach (var c in items) yield return c;
        }
    }

    public IEnumerable<string> ChatTools()
    {
        foreach (var ext in _extensions)
        {
            if (!ext.Manifest.Enabled) continue;
            IEnumerable<string> names;
            try { names = ext.GetToolNames(); }
            catch { continue; }
            foreach (var n in names) yield return n;
        }
    }

    public string Execute(string toolId, string args)
    {
        foreach (var ext in _extensions)
        {
            if (!ext.Manifest.Enabled) continue;
            try
            {
                // Try each extension, first success wins. Unknown tool throws.
                return ext.ExecuteTool(toolId, args);
            }
            catch (InvalidOperationException) { continue; }
            catch (Exception ex) { return $"ERROR: {ex.Message}"; }
        }
        return $"ERROR: unknown tool {toolId}";
    }

    public IEnumerable<ExtensionManifest> Manifests()
    {
        foreach (var e in _extensions) yield return e.Manifest;
    }
}
