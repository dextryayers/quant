using CommunityToolkit.Mvvm.ComponentModel;

namespace Quant.Desktop.Models;

public partial class EditorTab : ObservableObject
{
    public string FilePath { get; set; } = "";
    public string Title { get; set; } = "untitled";

    [ObservableProperty]
    public partial string Content { get; set; } = "";

    [ObservableProperty]
    public partial bool IsDirty { get; set; }

    [ObservableProperty]
    public partial bool IsPreview { get; set; } = true;

    [ObservableProperty]
    public partial bool IsPinned { get; set; }

    public string Language { get; set; } = "Text";
    public bool LargeFileMode { get; set; }
}
