using Avalonia.Controls;
using AvaloniaEdit.Highlighting;
using Quant.Desktop.Models;
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
        var composer = this.FindControl<TextBox>("Composer");
        if (composer != null)
        {
            composer.AddHandler(Avalonia.Input.InputElement.KeyDownEvent, (s, e) =>
            {
                if (DataContext is not MainViewModel vm) return;
                if (e.Key == Avalonia.Input.Key.Enter && !e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift))
                {
                    vm.SendCommand.Execute(null);
                    e.Handled = true;
                }
            }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        }
        if (editor != null)
        {
            editor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("C#");
            editor.Text = "// loading...";
            editor.TextArea?.AddHandler(Avalonia.Input.InputElement.KeyDownEvent, (s, e) =>
            {
                if (DataContext is not MainViewModel vm) return;
                if (e.Key == Avalonia.Input.Key.Tab && vm.GhostVisible)
                {
                    vm.AcceptGhostCommand.Execute(null);
                    e.Handled = true;
                }
                else if (e.Key == Avalonia.Input.Key.Escape && vm.GhostVisible)
                {
                    vm.DismissGhostCommand.Execute(null);
                    e.Handled = true;
                }
            }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            editor.TextArea?.SelectionChanged += (_, __) =>
            {
                if (DataContext is MainViewModel vm && editor != null)
                {
                    var sel = editor.SelectedText ?? "";
                    vm.SelectionText = sel.Length > 4000 ? sel[..4000] : sel;
                    vm.HasSelection = !string.IsNullOrWhiteSpace(sel);
                }
            };

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
                        if (e.PropertyName == nameof(MainViewModel.FindJumpOffset) && vm.FindJumpOffset >= 0)
                        {
                            try
                            {
                                editor.CaretOffset = System.Math.Min(vm.FindJumpOffset, editor.Text?.Length ?? 0);
                                editor.TextArea?.Focus();
                            }
                            catch { }
                        }
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
                        if (e.PropertyName == nameof(MainViewModel.PendingFolderRequest) && vm.PendingFolderRequest != null)
                        {
                            PickFolder();
                        }
                        if (e.PropertyName == nameof(MainViewModel.PendingFileRequest) && vm.PendingFileRequest != null)
                        {
                            PickFile();
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
                {
                    vm2.EnsureChildrenCommand.Execute(vm2.SelectedNode);
                    vm2.SelectedNode.IsExpanded = !vm2.SelectedNode.IsExpanded;
                }
            };
            tree.AddHandler(TreeViewItem.ExpandedEvent, (_, e) =>
            {
                if (e.Source is TreeViewItem item && item.DataContext is FileNode node && DataContext is MainViewModel vm)
                    vm.EnsureChildrenCommand.Execute(node);
            });
        }
    }

    private async void PickFile()
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "Attach a file",
                AllowMultiple = false,
            });
            if (files.Count > 0 && DataContext is MainViewModel vm)
            {
                vm.AddFileChipPath(files[0].Path.LocalPath);
            }
        }
        catch { }
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
