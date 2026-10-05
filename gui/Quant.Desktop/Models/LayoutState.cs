namespace Quant.Desktop.Models;

public sealed class LayoutState
{
    public string ActiveActivity { get; set; } = "Explorer";
    public bool SideVisible { get; set; } = true;
    public double SideWidth { get; set; } = 300;
    public bool RightVisible { get; set; } = true;
    public double RightWidth { get; set; } = 360;
    public bool BottomVisible { get; set; } = true;
    public double BottomHeight { get; set; } = 200;
    public string BottomTab { get; set; } = "Output";
}
