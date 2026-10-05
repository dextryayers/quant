namespace Quant.Desktop.Models;

public sealed class GitFile
{
    public string Path { get; set; } = "";
    public string Status { get; set; } = "M";
}

public sealed class GitLog
{
    public string Hash { get; set; } = "";
    public string Message { get; set; } = "";
    public string Date { get; set; } = "";
}
