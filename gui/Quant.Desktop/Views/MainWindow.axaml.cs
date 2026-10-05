using Avalonia.Controls;
using AvaloniaEdit.Highlighting;
using Quant.Desktop.ViewModels;
using System;

namespace Quant.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var editor = this.FindControl<AvaloniaEdit.TextEditor>("Editor");
        var tree = this.FindControl<TreeView>("ExplorerTree");
        if (editor != null)
        {
            editor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("C#");
            editor.Text = "// loading...";

            DataContextChanged += (_, __) =>
            {
                if (DataContext is MainViewModel vm)
                {
                    editor.Text = vm.EditorText;
                    editor.TextChanged += (_, _) => vm.EditorText = editor.Text ?? "";
                    vm.PropertyChanged += async (_, e) =>
                    {
                        if (e.PropertyName == nameof(MainViewModel.EditorText) && editor.Text != vm.EditorText)
                            editor.Text = vm.EditorText;
                        if (e.PropertyName == nameof(MainViewModel.ActiveTab) && vm.ActiveTab != null)
                        {
                            var lang = vm.ActiveTab.Language switch
                            {
                                "Rust" => "Rust",
                                "C#" => "C#",
                                "TypeScript" => "JavaScript",
                                "Python" => "Python",
                                _ => "C#",
                            };
                            editor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition(lang);
                        }
                        if (e.PropertyName == nameof(MainViewModel.PendingPalette) && vm.PendingPalette is CommandPalette p)
                        {
                            await p.ShowDialog(this);
                        }
                    };
                }
            };
        }

        if (tree != null)
        {
            tree.DoubleTapped += (_, __) =>
            {
                if (DataContext is MainViewModel vm && vm.SelectedNode != null && !vm.SelectedNode.IsDirectory)
                    vm.OpenFileCommand.Execute(vm.SelectedNode);
                else if (DataContext is MainViewModel vm2 && vm2.SelectedNode != null)
                    vm2.ExpandNodeCommand.Execute(vm2.SelectedNode);
            };
        }
    }

    private async void PickFolder()
    {
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
            {
                Title = "Open Folder",
                AllowMultiple = false,
            });
            if (folders.Count > 0 && DataContext is MainViewModel vm)
            {
                vm.AddRootCommand.Execute(folders[0].Path.LocalPath);
            }
        }
        catch { }
    }
}
