namespace Quant.Desktop.Models;

public sealed class ApprovalCard
{
    public string Id { get; set; } = "";
    public string Tool { get; set; } = "";
    public string Args { get; set; } = "";
    public string Reason { get; set; } = "";
    public string Status { get; set; } = "pending";
}
