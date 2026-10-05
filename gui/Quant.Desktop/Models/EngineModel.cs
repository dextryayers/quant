namespace Quant.Desktop.Models;

public sealed class EngineModel
{
    public string Id { get; set; } = "";
    public string File { get; set; } = "";
    public double? SizeMb { get; set; }
    public bool Loaded { get; set; }
    public double? RamMb { get; set; }
    public bool IsPreset { get; set; }
}
