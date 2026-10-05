namespace Quant.Desktop.Models;

public sealed class SymbolItem
{
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "function";
    public string File { get; set; } = "";
    public int Line { get; set; }
    public string Preview { get; set; } = "";
}
