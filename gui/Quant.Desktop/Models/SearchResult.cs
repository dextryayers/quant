namespace Quant.Desktop.Models;

public sealed class SearchResult
{
    public string File { get; set; } = "";
    public int Line { get; set; }
    public int Col { get; set; }
    public string Preview { get; set; } = "";
    public bool Selected { get; set; } = true;
}
