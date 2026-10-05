using System;
using System.IO;
using System.Text.Json;
using Quant.Desktop.Models;

namespace Quant.Desktop.Services;

public sealed class LayoutService
{
    private readonly string _path;

    public LayoutState State { get; private set; } = new();

    public LayoutService()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Quant");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "layout.json");
        Load();
    }

    public void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var s = JsonSerializer.Deserialize<LayoutState>(File.ReadAllText(_path));
            if (s != null) State = s;
        }
        catch { }
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(State, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
