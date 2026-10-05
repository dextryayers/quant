using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Quant.Desktop.Models;
using Quant.Desktop.Services;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Threading.Tasks;

namespace Quant.Desktop.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly QuantEngineClient _engine;
    private readonly LayoutService _layout = new();
    private readonly ThemeService _theme = new();
    private readonly WorkspaceService _workspace = new();
    private readonly ExplorerService _explorer = new();
    private readonly int _port;
    private FileSystemWatcher? _watcher;
    private System.Threading.Timer? _watchDebounce;
    private string? _pendingTrashRestore;
    private string? _pendingTrashOrigin;

    public MainViewModel() : this("http://127.0.0.1:3737", 3737) { }

    public MainViewModel(string baseUrl, int port)
    {
        _engine = new QuantEngineClient(baseUrl);
        _port = port;
        StatusText = $"engine :{port} not checked";
        ActiveActivity = _layout.State.ActiveActivity;
        SideVisible = _layout.State.SideVisible;
        RightVisible = _layout.State.RightVisible;
        BottomVisible = _layout.State.BottomVisible;
        BottomTab = _layout.State.BottomTab;
        OutputLog = "Engine log will appear here.\nIndex log will appear here.\n";
        CurrentTheme = "dark-premium";
        RefreshExplorer();
        StartWatcher();
    }

    public ObservableCollection<FileNode> Roots { get; } = new();
    public ObservableCollection<EditorTab> Tabs { get; } = new();

    [ObservableProperty]
    public partial string ExplorerFilter { get; set; } = "";

    [ObservableProperty]
    public partial string ExplorerSort { get; set; } = "Name";

    [ObservableProperty]
    public partial bool ShowExcluded { get; set; } = false;

    [ObservableProperty]
    public partial FileNode? SelectedNode { get; set; }

    [ObservableProperty]
    public partial EditorTab? ActiveTab { get; set; }

    [ObservableProperty]
    public partial string Breadcrumbs { get; set; } = "No file open";

    [ObservableProperty]
    public partial bool ZenMode { get; set; } = false;

    partial void OnExplorerFilterChanged(string value) => RefreshExplorer();
    partial void OnShowExcludedChanged(bool value) => RefreshExplorer();

    [ObservableProperty]
    public partial string CurrentTheme { get; set; } = "dark-premium";

    [ObservableProperty]
    public partial string ActiveActivity { get; set; } = "Explorer";

    [ObservableProperty]
    public partial bool SideVisible { get; set; } = true;

    [ObservableProperty]
    public partial bool RightVisible { get; set; } = true;

    [ObservableProperty]
    public partial bool BottomVisible { get; set; } = true;

    [ObservableProperty]
    public partial string BottomTab { get; set; } = "Output";

    [ObservableProperty]
    public partial string OutputLog { get; set; } = "";

    public ObservableCollection<Toast> Toasts { get; } = new();

    private void PushToast(string title, string message, string action = "")
    {
        Toasts.Insert(0, new Toast { Title = title, Message = message, ActionLabel = action });
        while (Toasts.Count > 3) Toasts.RemoveAt(Toasts.Count - 1);
        OutputLog += $"[toast] {title}: {message}\n";
    }

    [RelayCommand]
    private void DismissToast(Toast t)
    {
        if (Toasts.Contains(t)) Toasts.Remove(t);
    }

    [RelayCommand]
    private void SetActivity(string id)
    {
        ActiveActivity = id;
        SideVisible = true;
        _layout.State.ActiveActivity = id;
        _layout.State.SideVisible = true;
        _layout.Save();
    }

    [RelayCommand]
    private void ToggleSide()
    {
        SideVisible = !SideVisible;
        _layout.State.SideVisible = SideVisible;
        _layout.Save();
    }

    [RelayCommand]
    private void ToggleRight()
    {
        RightVisible = !RightVisible;
        _layout.State.RightVisible = RightVisible;
        _layout.Save();
    }

    [RelayCommand]
    private void ToggleBottom()
    {
        BottomVisible = !BottomVisible;
        _layout.State.BottomVisible = BottomVisible;
        _layout.Save();
    }

    [RelayCommand]
    private void SetBottomTab(string tab)
    {
        BottomTab = tab;
        BottomVisible = true;
        _layout.State.BottomTab = tab;
        _layout.State.BottomVisible = true;
        _layout.Save();
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        _theme.Toggle();
        CurrentTheme = _theme.Current;
        OutputLog += $"[theme] {CurrentTheme}\n";
    }

    [RelayCommand]
    private void OpenPalette()
    {
        try
        {
            var vm = new CommandPaletteViewModel();
            var win = new Views.CommandPalette { DataContext = vm };
            OutputLog += "[palette] opened with 15 commands indexed\n";
            // Owner wiring happens in view code behind via singleton ref when available.
            // Fallback: store pending palette request for MainWindow to show.
            PendingPalette = win;
            OnPropertyChanged(nameof(PendingPalette));
        }
        catch (System.Exception ex)
        {
            OutputLog += $"[palette] error {ex.Message}\n";
        }
    }

    public object? PendingPalette { get; private set; }

    [ObservableProperty]
    public partial string EditorText { get; set; } = "// Welcome to Quant IDE (MVP)\n// 1. Start engine: cargo run -p quant-engine (port 3737)\n// 2. Ask in the assistant panel on the right\n// 3. Next: load a .gguf file from Hugging Face into engine/models/\n\nfn main() {\n    println!(\"hello quant\");\n}\n";

    [ObservableProperty]
    public partial string InputText { get; set; } = "";

    [ObservableProperty]
    public partial string StatusText { get; set; } = "engine: not checked";

    [ObservableProperty]
    public partial bool IsBusy { get; set; } = false;

    public ObservableCollection<ChatMessage> Messages { get; } = new()
    {
        new ChatMessage { Role = "assistant", Content = "Welcome to Quant. Attach a file or ask about the open editor. Local GGUF inference stays offline and memory mapped." }
    };

    [RelayCommand]
    private async Task CheckHealth()
    {
        StatusText = $"checking engine on :{_port}...";
        OutputLog += $"[health] GET :{_port}/health\n";
        var h = await _engine.GetHealthAsync();
        try
        {
            using var doc = JsonDocument.Parse(h);
            var v = doc.RootElement.GetProperty("version").GetString();
            StatusText = $"engine v{v} on :{_port} connected";
            OutputLog += $"[health] OK v{v}\n";
            PushToast("Engine connected", $"v{v} on :{_port}", "View logs");
        }
        catch
        {
            StatusText = h.Length > 120 ? h[..120] : h;
            OutputLog += $"[health] {StatusText}\n";
            PushToast("Engine offline", "Start with cargo run -p quant-engine", "Retry");
        }
    }

    [RelayCommand]
    private async Task Send()
    {
        if (IsBusy || string.IsNullOrWhiteSpace(InputText)) return;
        var prompt = InputText.Trim();
        InputText = "";
        Messages.Add(new ChatMessage { Role = "user", Content = prompt });

        var assistant = new ChatMessage { Role = "assistant", Content = "" };
        Messages.Add(assistant);
        IsBusy = true;

        try
        {
            var ctx = ActiveTab?.Content ?? EditorText;
            await _engine.ChatStreamAsync(prompt, ctx, delta =>
            {
                assistant.Content += delta;
                // Refresh bound row for streaming render, simple approach for MVP
                var idx = Messages.IndexOf(assistant);
                Messages[idx] = new ChatMessage { Role = "assistant", Content = assistant.Content };
            });
            if (string.IsNullOrWhiteSpace(assistant.Content))
            {
                var once = await _engine.ChatOnceAsync(prompt, ctx);
                var idx = Messages.IndexOf(assistant);
                Messages[idx] = new ChatMessage { Role = "assistant", Content = once };
            }
            StatusText = "engine connected";
            PushToast("Reply ready", "Assistant stream completed", "");
        }
        catch (System.Exception ex)
        {
            var idx = Messages.IndexOf(assistant);
            Messages[idx] = new ChatMessage { Role = "assistant", Content = $"Engine offline: {ex.Message}. Start it with: cargo run -p quant-engine" };
            StatusText = "engine offline";
            PushToast("Engine offline", ex.Message, "Retry");
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ---------- Phase 2.1 Workspace ----------
    [RelayCommand]
    private void AddRoot(string path)
    {
        if (!System.IO.Directory.Exists(path))
        {
            PushToast("Folder not found", path, "");
            return;
        }
        _workspace.AddRoot(path, trusted: true);
        RefreshExplorer();
        StartWatcher();
        PushToast("Folder added", path, "");
        OutputLog += $"[workspace] add root {path}\n";
    }

    [RelayCommand]
    private void RemoveRoot(string path)
    {
        _workspace.RemoveRoot(path);
        RefreshExplorer();
        PushToast("Folder removed", path, "Undo");
    }

    // ---------- Phase 2.2 Explorer ----------
    [RelayCommand]
    private void RefreshExplorer()
    {
        Roots.Clear();
        var nodes = _explorer.BuildRoots(_workspace.Roots, ExplorerFilter, ExplorerSort, ShowExcluded);
        foreach (var n in nodes) Roots.Add(n);
        OutputLog += $"[explorer] {Roots.Count} roots filter='{ExplorerFilter}'\n";
        if (_explorer.LargeMode)
            PushToast("Large folder mode", "Over 100k entries. Showing indexed subset.", "");
    }

    [RelayCommand]
    private void ExpandNode(FileNode node)
    {
        if (node == null || !node.IsDirectory) return;
        if (!node.ChildrenLoaded)
            _explorer.LoadChildren(node, ExplorerFilter, ExplorerSort, ShowExcluded, 1);
        node.IsExpanded = !node.IsExpanded;
    }

    [RelayCommand]
    private void OpenFile(FileNode? node)
    {
        node ??= SelectedNode;
        if (node == null || node.IsDirectory) return;
        OpenFilePath(node.FullPath);
    }

    private void OpenFilePath(string path)
    {
        try
        {
            var fi = new System.IO.FileInfo(path);
            var existing = System.Linq.Enumerable.FirstOrDefault(Tabs, t => t.FilePath == path);
            if (existing != null)
            {
                ActiveTab = existing;
                existing.IsPreview = false;
                SyncEditorFromTab();
                return;
            }
            var content = System.IO.File.ReadAllText(path);
            if (content.Length > 200_000) content = content[..200_000] + "\n... [truncated large file preview]";
            var tab = new EditorTab
            {
                FilePath = path,
                Title = System.IO.Path.GetFileName(path),
                Content = content,
                IsPreview = false,
                Language = DetectLanguage(path),
                LargeFileMode = fi.Length > 2_000_000,
            };
            var preview = System.Linq.Enumerable.FirstOrDefault(Tabs, t => t.IsPreview);
            if (preview != null) Tabs.Remove(preview);
            Tabs.Add(tab);
            ActiveTab = tab;
            SyncEditorFromTab();
            UpdateBreadcrumbs();
            if (tab.LargeFileMode) PushToast("Large file guard", $"{tab.Title} opened in safe preview.", "");
            OutputLog += $"[editor] open {path}\n";
        }
        catch (System.Exception ex) { PushToast("Open failed", ex.Message, ""); }
    }

    private static string DetectLanguage(string path)
    {
        return System.IO.Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".rs" => "Rust",
            ".cs" => "C#",
            ".ts" or ".tsx" or ".js" => "TypeScript",
            ".py" => "Python",
            ".go" => "Go",
            ".md" => "Markdown",
            ".json" => "JSON",
            ".toml" => "TOML",
            _ => "Text",
        };
    }

    private void SyncEditorFromTab()
    {
        if (ActiveTab != null)
        {
            EditorText = ActiveTab.Content;
            Breadcrumbs = ActiveTab.FilePath;
        }
    }

    partial void OnActiveTabChanged(EditorTab? value)
    {
        if (value != null)
        {
            EditorText = value.Content;
            Breadcrumbs = value.FilePath;
        }
    }

    partial void OnEditorTextChanged(string value)
    {
        if (ActiveTab != null && ActiveTab.Content != value && !ActiveTab.LargeFileMode)
        {
            ActiveTab.Content = value;
            ActiveTab.IsDirty = true;
        }
    }

    // ---------- Phase 2.3 File CRUD ----------
    [RelayCommand]
    private void NewFile()
    {
        var dir = TargetDir();
        var name = "untitled.txt";
        var i = 1;
        while (System.IO.File.Exists(System.IO.Path.Combine(dir, name)))
            name = $"untitled{i++}.txt";
        if (_explorer.CreateFile(dir, name, out var full))
        {
            RefreshExplorer();
            OpenFilePath(full);
            PushToast("File created", full, "");
        }
        else PushToast("Create failed", _explorer.LastError ?? "unknown", "");
    }

    [RelayCommand]
    private void NewFolder()
    {
        var dir = TargetDir();
        var name = "new-folder";
        var i = 1;
        while (System.IO.Directory.Exists(System.IO.Path.Combine(dir, name)))
            name = $"new-folder{i++}";
        if (_explorer.CreateFolder(dir, name, out var full))
        {
            RefreshExplorer();
            PushToast("Folder created", full, "");
        }
        else PushToast("Create failed", _explorer.LastError ?? "unknown", "");
    }

    [RelayCommand]
    private void DuplicateNode(FileNode? node)
    {
        node ??= SelectedNode;
        if (node == null) return;
        if (_explorer.Duplicate(node.FullPath, out var dest))
        {
            RefreshExplorer();
            PushToast("Duplicated", dest, "");
        }
        else PushToast("Duplicate failed", _explorer.LastError ?? "", "");
    }

    [RelayCommand]
    private void DeleteNode(FileNode? node)
    {
        node ??= SelectedNode;
        if (node == null || _workspace.Roots.Count == 0) return;
        var root = _workspace.ActiveRoot ?? "";
        if (_explorer.MoveToTrash(root, node.FullPath, out var trash, out var token))
        {
            _pendingTrashRestore = trash;
            _pendingTrashOrigin = node.FullPath;
            RefreshExplorer();
            PushToast("Deleted", $"{node.Name} moved to trash. Undo within 10s.", "Undo");
            _ = new System.Threading.Timer(_ => { _pendingTrashRestore = null; }, null, 10_000, System.Threading.Timeout.Infinite);
            OutputLog += $"[delete] {node.FullPath} -> {trash}\n";
        }
        else PushToast("Delete failed", _explorer.LastError ?? "", "");
    }

    [RelayCommand]
    private void UndoDelete()
    {
        if (_pendingTrashRestore == null || _pendingTrashOrigin == null) return;
        if (_explorer.RestoreFromTrash(_pendingTrashRestore, _pendingTrashOrigin))
        {
            PushToast("Restored", _pendingTrashOrigin, "");
            RefreshExplorer();
        }
        _pendingTrashRestore = null;
    }

    [RelayCommand]
    private void RenameNode(FileNode? node)
    {
        node ??= SelectedNode;
        if (node == null) return;
        var next = node.Name + "_renamed";
        var err = ExplorerService.ValidateName(next);
        if (err != null) { PushToast("Invalid name", err, ""); return; }
        if (_explorer.Rename(node.FullPath, next, out var dest))
        {
            RefreshExplorer();
            PushToast("Renamed", dest, "");
        }
        else PushToast("Rename failed", _explorer.LastError ?? "", "");
    }

    private string TargetDir()
    {
        if (SelectedNode != null)
        {
            if (SelectedNode.IsDirectory) return SelectedNode.FullPath;
            return System.IO.Path.GetDirectoryName(SelectedNode.FullPath) ?? _workspace.ActiveRoot ?? ".";
        }
        return _workspace.ActiveRoot ?? ".";
    }

    // ---------- Phase 2.5 Clipboard ----------
    [RelayCommand]
    private void CopyNode(FileNode? node)
    {
        node ??= SelectedNode;
        if (node == null) return;
        _explorer.SetClipboard(new[] { node.FullPath }, isCut: false);
        PushToast("Copied", node.Name, "");
    }

    [RelayCommand]
    private void CutNode(FileNode? node)
    {
        node ??= SelectedNode;
        if (node == null) return;
        _explorer.SetClipboard(new[] { node.FullPath }, isCut: true);
        PushToast("Cut", node.Name, "");
    }

    [RelayCommand]
    private void PasteNode(FileNode? node)
    {
        var dest = node != null && node.IsDirectory ? node.FullPath : TargetDir();
        var r = _explorer.Paste(dest, keepBoth: true);
        RefreshExplorer();
        PushToast("Pasted", $"copied={r.Copied} moved={r.Moved} skipped={r.Skipped}", "");
    }

    // ---------- Phase 2.6 Reveal ----------
    [RelayCommand]
    private void RevealInOS(FileNode? node)
    {
        node ??= SelectedNode;
        if (node == null) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = node.FullPath,
                UseShellExecute = true,
            });
        }
        catch (System.Exception ex) { PushToast("Reveal failed", ex.Message, ""); }
    }

    [RelayCommand]
    private void CopyPath(FileNode? node)
    {
        node ??= SelectedNode;
        if (node == null) return;
        OutputLog += $"[clipboard] {node.FullPath}\n";
        PushToast("Path ready", node.FullPath, "");
    }

    [RelayCommand]
    private void CopyRelative(FileNode? node)
    {
        node ??= SelectedNode;
        if (node == null || _workspace.ActiveRoot == null) return;
        var rel = System.IO.Path.GetRelativePath(_workspace.ActiveRoot, node.FullPath);
        OutputLog += $"[clipboard] {rel}\n";
        PushToast("Relative path ready", rel, "");
    }

    // ---------- Phase 2.7 Watcher ----------
    private void StartWatcher()
    {
        try
        {
            _watcher?.Dispose();
            var root = _workspace.ActiveRoot;
            if (root == null || !System.IO.Directory.Exists(root)) return;
            _watcher = new System.IO.FileSystemWatcher(root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = System.IO.NotifyFilters.FileName | System.IO.NotifyFilters.LastWrite | System.IO.NotifyFilters.DirectoryName,
                EnableRaisingEvents = true,
            };
            System.IO.FileSystemEventHandler h = (_, __) => DebouncedRefresh();
            System.IO.RenamedEventHandler rh = (_, __) => DebouncedRefresh();
            _watcher.Created += h;
            _watcher.Changed += h;
            _watcher.Deleted += h;
            _watcher.Renamed += rh;
        }
        catch (System.Exception ex) { OutputLog += $"[watcher] {ex.Message}\n"; }
    }

    private void DebouncedRefresh()
    {
        _watchDebounce?.Dispose();
        _watchDebounce = new System.Threading.Timer(_ =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                RefreshExplorer();
                OutputLog += "[watcher] external change refreshed\n";
            });
        }, null, 400, System.Threading.Timeout.Infinite);
    }

    // ---------- Phase 3.1 Tabs ----------
    [RelayCommand]
    private void CloseTab(EditorTab? tab)
    {
        tab ??= ActiveTab;
        if (tab == null) return;
        if (tab.IsDirty)
        {
            PushToast("Unsaved changes", $"{tab.Title} has unsaved edits. Save first.", "Save");
            return;
        }
        Tabs.Remove(tab);
        ActiveTab = System.Linq.Enumerable.LastOrDefault(Tabs);
        if (ActiveTab != null) SyncEditorFromTab();
        else { EditorText = ""; Breadcrumbs = "No file open"; }
    }

    [RelayCommand]
    private void SaveTab()
    {
        if (ActiveTab == null || string.IsNullOrEmpty(ActiveTab.FilePath)) return;
        try
        {
            if (ActiveTab.LargeFileMode) { PushToast("Large file", "Save disabled in preview guard.", ""); return; }
            System.IO.File.WriteAllText(ActiveTab.FilePath, ActiveTab.Content);
            ActiveTab.IsDirty = false;
            ActiveTab.IsPreview = false;
            PushToast("Saved", ActiveTab.Title, "");
        }
        catch (System.Exception ex) { PushToast("Save failed", ex.Message, ""); }
    }

    [RelayCommand]
    private void SaveAllTabs()
    {
        foreach (var t in Tabs)
        {
            if (!t.IsDirty || t.LargeFileMode || string.IsNullOrEmpty(t.FilePath)) continue;
            try { System.IO.File.WriteAllText(t.FilePath, t.Content); t.IsDirty = false; } catch { }
        }
        PushToast("Saved all", $"{Tabs.Count} tabs", "");
    }

    // ---------- Phase 3.3 Edit ----------
    [RelayCommand]
    private void ToggleComment()
    {
        if (ActiveTab == null) return;
        var lines = ActiveTab.Content.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var t = lines[i];
            if (string.IsNullOrWhiteSpace(t)) continue;
            var indent = t[..(t.Length - t.TrimStart().Length)];
            var body = t.TrimStart();
            lines[i] = body.StartsWith("//") ? indent + body[2..].TrimStart() : indent + "// " + body;
        }
        ActiveTab.Content = string.Join('\n', lines);
        EditorText = ActiveTab.Content;
        ActiveTab.IsDirty = true;
    }

    [RelayCommand]
    private void DuplicateLine()
    {
        if (ActiveTab == null) return;
        var lines = ActiveTab.Content.Split('\n');
        if (lines.Length == 0) return;
        var list = new System.Collections.Generic.List<string>(lines) { lines[0] };
        ActiveTab.Content = string.Join('\n', list);
        EditorText = ActiveTab.Content;
        ActiveTab.IsDirty = true;
        PushToast("Duplicated", "First line duplicated.", "");
    }

    [RelayCommand]
    private void SortLines()
    {
        if (ActiveTab == null) return;
        var lines = ActiveTab.Content.Split('\n');
        System.Array.Sort(lines, System.StringComparer.Ordinal);
        ActiveTab.Content = string.Join('\n', lines);
        EditorText = ActiveTab.Content;
        ActiveTab.IsDirty = true;
    }

    [RelayCommand]
    private void ToggleZen()
    {
        ZenMode = !ZenMode;
        SideVisible = !ZenMode;
        BottomVisible = !ZenMode;
        RightVisible = !ZenMode;
        OutputLog += $"[zen] {(ZenMode ? "on" : "off")}\n";
    }

    // ---------- Phase 3.4 Nav ----------
    [RelayCommand]
    private void GoToLine()
    {
        PushToast("Go to line", "Use Ctrl+G then type number. Demo jumps to top.", "");
        OutputLog += $"[nav] {Breadcrumbs}\n";
    }

    private void UpdateBreadcrumbs()
    {
        if (ActiveTab == null || _workspace.ActiveRoot == null) return;
        try { Breadcrumbs = System.IO.Path.GetRelativePath(_workspace.ActiveRoot, ActiveTab.FilePath); }
        catch { Breadcrumbs = ActiveTab.FilePath; }
    }
}
