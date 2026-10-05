using System.Collections.Generic;

namespace Quant.Desktop.Models;

public sealed class LaunchProfile
{
    public string Name { get; set; } = "";
    public string Command { get; set; } = "";
    public string Args { get; set; } = "";
    public string Cwd { get; set; } = "";
    public Dictionary<string, string> Env { get; set; } = new();
    public int Port { get; set; }
    public bool Running { get; set; }
}
