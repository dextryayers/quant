namespace Quant.Desktop.Models;

public sealed class WorkspaceRoot
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Trusted { get; set; } = true;
    public bool Excluded { get; set; } = false;
}
