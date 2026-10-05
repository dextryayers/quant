using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Quant.Desktop.Models;
using Quant.Desktop.Services;
using System.Collections.ObjectModel;

namespace Quant.Desktop.ViewModels;

public partial class CommandPaletteViewModel : ViewModelBase
{
    private readonly CommandRegistry _registry = new();

    [ObservableProperty]
    public partial string Query { get; set; } = "";

    public ObservableCollection<CommandItem> Results { get; } = new();

    public CommandItem? Selected { get; set; }

    public CommandPaletteViewModel()
    {
        Refresh();
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Query)) Refresh();
        };
    }

    [RelayCommand]
    private void Refresh()
    {
        Results.Clear();
        foreach (var c in _registry.Filter(Query))
            Results.Add(c);
        Selected = Results.Count > 0 ? Results[0] : null;
    }
}
