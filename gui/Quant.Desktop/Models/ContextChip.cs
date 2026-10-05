namespace Quant.Desktop.Models;

public sealed class ContextChip
{
    public string Kind { get; set; } = "file";
    public string Label { get; set; } = "";
    public string Path { get; set; } = "";
    public int Tokens { get; set; }
}
