using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace Quant.Desktop.Models;

public partial class FileNode : ObservableObject
{
    public string FullPath { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsDirectory { get; set; }
    public long Size { get; set; }
    public string Modified { get; set; } = "";
    public string Icon { get; set; } = "F";

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public ObservableCollection<FileNode> Children { get; } = new();
    public bool ChildrenLoaded { get; set; }
}
