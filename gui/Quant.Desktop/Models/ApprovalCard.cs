namespace Quant.Desktop.Models;

public sealed class ApprovalCard
{
    public string Id { get; set; } = "";
    public string Tool { get; set; } = "";
    public string Args { get; set; } = "";
    public string Reason { get; set; } = "";
    public string Status { get; set; } = "pending";
    public string Context { get; set; } = "";
    public string FullArgs { get; set; } = "";

    // Plain words for the approval card. Tool ids stay in logs only.
    public string Label
    {
        get
        {
            if (Tool.StartsWith("mcp:", System.StringComparison.Ordinal)) return "Use connector";
            return Tool switch
            {
                "grep" => "Search code",
                "read" => "Read file",
                "glob" => "Find files",
                "symbols" => "Look up symbol",
                "apply_diff" => "Edit file",
                "exec" => "Run command",
                "download_model" => "Download model",
                _ => "Continue",
            };
        }
    }
}
