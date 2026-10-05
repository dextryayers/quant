using System;
using System.IO;
using System.Text.Json;
using Quant.Desktop.Models;

namespace Quant.Desktop.Services;

public sealed class ConfigService
{
    public QuantConfig Config { get; private set; } = new();
    public string ConfigPath { get; }
    public int EnginePort { get; private set; } = 3737;

    public ConfigService(string[]? args = null)
    {
        var root = FindRepoRoot();
        ConfigPath = Path.Combine(root, "quant.json");
        Load();
        ApplyArgs(args ?? Array.Empty<string>());
    }

    public string BaseUrl => $"http://127.0.0.1:{EnginePort}";

    private void Load()
    {
        try
        {
            var example = Path.Combine(FindRepoRoot(), "quant.json.example");
            var path = File.Exists(ConfigPath) ? ConfigPath : example;
            if (!File.Exists(path)) return;
            var json = File.ReadAllText(path);
            var cfg = JsonSerializer.Deserialize<QuantConfig>(json);
            if (cfg != null) Config = cfg;
            EnginePort = Config.Engine.Port;
        }
        catch
        {
            // Keep defaults on corrupt config, surface via diagnostics later.
        }
    }

    private void ApplyArgs(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--engine-port" && i + 1 < args.Length && int.TryParse(args[i + 1], out var p))
                EnginePort = p;
            else if (args[i].StartsWith("--engine-port=", StringComparison.Ordinal) && int.TryParse(args[i]["--engine-port=".Length..], out var p2))
                EnginePort = p2;
        }
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 6 && dir != null; i++)
        {
            if (File.Exists(Path.Combine(dir, "Quant.slnx")) || File.Exists(Path.Combine(dir, "quant.json.example")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        return Directory.GetCurrentDirectory();
    }
}
