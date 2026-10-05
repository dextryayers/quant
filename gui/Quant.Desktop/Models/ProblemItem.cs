namespace Quant.Desktop.Models;

public sealed class ProblemItem
{
    public string Severity { get; set; } = "info";
    public string Message { get; set; } = "";
    public string File { get; set; } = "";
    public int Line { get; set; }
    public int Col { get; set; }
    public string Source { get; set; } = "task";
}
