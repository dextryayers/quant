namespace Quant.Desktop.Models;

public sealed class EngineModel
{
    public string Id { get; set; } = "";
    public string File { get; set; } = "";
    public double? SizeMb { get; set; }
    public bool Loaded { get; set; }
    public double? RamMb { get; set; }
    public bool IsPreset { get; set; }

    // Friendly product names. Internal file and quant details stay out of the UI.
    public string DisplayName
    {
        get
        {
            var id = Id.ToLowerInvariant();
            if (id.Contains("3b")) return "Assistant Light";
            if (id.Contains("coder") || id.Contains("code")) return "Assistant Coder";
            if (id.Contains("llama") || id.Contains("8b")) return "Assistant Chat";
            if (id.StartsWith("mock")) return "Getting started";
            return ToTitle(Id);
        }
    }

    public string DisplaySize
    {
        get
        {
            if (SizeMb is not double mb) return Loaded ? "In use" : "Not downloaded";
            if (Loaded) return "In use";
            return mb >= 1024 ? $"{mb / 1024:F1} GB" : $"{mb:F0} MB";
        }
    }

    private static string ToTitle(string id)
    {
        var clean = id.Replace('-', ' ').Replace('_', ' ').Trim();
        if (clean.Length == 0) return "Assistant";
        return char.ToUpperInvariant(clean[0]) + clean[1..];
    }
}
