namespace Quant.Desktop.Models;

public sealed class DevTask
{
    public string Label { get; set; } = "";
    public string Command { get; set; } = "";
    public string Cwd { get; set; } = "";
    public bool Running { get; set; }
}
