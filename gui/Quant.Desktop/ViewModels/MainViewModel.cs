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
    private readonly SearchService _search = new();
    private readonly SymbolService _symbols = new();
    private readonly ProblemService _problems = new();
    private readonly DiffService _diff = new();
    private readonly PromptService _prompts = new();
    private ThreadService? _threads;
    private System.Threading.CancellationTokenSource? _chatCts;
    private System.Threading.CancellationTokenSource? _searchCts;
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
        ShowExplorer = ActiveActivity == "Explorer";
        ShowSearch = ActiveActivity == "Search" || ActiveActivity == "Symbols";
        ShowProblemsView = BottomTab == "Problems";
        RefreshExplorer();
        StartWatcher();
    }

    public ObservableCollection<FileNode> Roots { get; } = new();
    public ObservableCollection<EditorTab> Tabs { get; } = new();
    public ObservableCollection<SearchResult> SearchResults { get; } = new();
    public ObservableCollection<SymbolItem> SymbolResults { get; } = new();
    public ObservableCollection<ProblemItem> Problems { get; } = new();
    public ObservableCollection<ContextChip> Chips { get; } = new();
    public ObservableCollection<ChatThread> ThreadList { get; } = new();

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

    // Phase 4.1 find
    [ObservableProperty]
    public partial string FindText { get; set; } = "";

    [ObservableProperty]
    public partial bool FindRegex { get; set; } = false;

    [ObservableProperty]
    public partial bool FindCase { get; set; } = false;

    [ObservableProperty]
    public partial bool FindWord { get; set; } = false;

    [ObservableProperty]
    public partial string FindStatus { get; set; } = "";

    // Phase 4.2 search
    [ObservableProperty]
    public partial string SearchQuery { get; set; } = "";

    [ObservableProperty]
    public partial string ReplaceText { get; set; } = "";

    [ObservableProperty]
    public partial bool SearchRegex { get; set; } = false;

    [ObservableProperty]
    public partial bool SearchCase { get; set; } = false;

    [ObservableProperty]
    public partial bool SearchWord { get; set; } = false;

    [ObservableProperty]
    public partial string SearchStatus { get; set; } = "Type to search workspace";

    // Phase 4.4 symbols
    [ObservableProperty]
    public partial string SymbolQuery { get; set; } = "";

    // Phase 4.5 problems
    [ObservableProperty]
    public partial bool ProblemsErrorsOnly { get; set; } = false;

    // Phase 5.1 thread UI
    [ObservableProperty]
    public partial string TokenMeter { get; set; } = "0 / 8192";

    [ObservableProperty]
    public partial string ChatMode { get; set; } = "Edit";

    [ObservableProperty]
    public partial ChatThread? ActiveThread { get; set; }

    partial void OnExplorerFilterChanged(string value) => RefreshExplorer();
    partial void OnShowExcludedChanged(bool value) => RefreshExplorer();

    [ObservableProperty]
    public partial string CurrentTheme { get; set; } = "dark-premium";

    [ObservableProperty]
    public partial string ActiveActivity { get; set; } = "Explorer";

    [ObservableProperty]
    public partial bool ShowExplorer { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowSearch { get; set; } = false;

    [ObservableProperty]
    public partial bool ShowProblemsView { get; set; } = false;

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
        ShowExplorer = id == "Explorer";
        ShowSearch = id == "Search" || id == "Symbols";
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
        ShowProblemsView = tab == "Problems";
        _layout.State.BottomTab = tab;
        _layout.State.BottomVisible = true;
        _layout.Save();
        if (tab == "Problems") RefreshProblemsCommand.Execute(null);
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
        var raw = InputText.Trim();
        InputText = "";

        // Phase 5.4 local commands
        if (PromptService.IsLocalCommand(raw, out var local))
        {
            if (local == "/context-clear") { Chips.Clear(); UpdateTokens(); PushToast("Context cleared", "", ""); }
            else if (local == "/index-refresh") { RefreshExplorer(); PushToast("Index refreshed", "Explorer rebuilt.", ""); }
            else PushToast(local, "queued", "");
            return;
        }
        var prompt = ExpandSlash(raw);
        Messages.Add(new ChatMessage { Role = "user", Content = raw });

        var assistant = new ChatMessage { Role = "assistant", Content = "" };
        Messages.Add(assistant);
        IsBusy = true;
        _chatCts?.Cancel();
        _chatCts = new System.Threading.CancellationTokenSource();
        UpdateTokens();

        try
        {
            var ctx = ActiveTab?.Content ?? EditorText;
            if (Chips.Count > 0)
                ctx += "\n\nAttached:\n" + string.Join("\n", System.Linq.Enumerable.Select(Chips, c => $"- {c.Kind} {c.Label}"));
            await _engine.ChatStreamAsync(prompt, ctx, delta =>
            {
                assistant.Content += delta;
                // Refresh bound row for streaming render, simple approach for MVP
                var idx = Messages.IndexOf(assistant);
                Messages[idx] = new ChatMessage { Role = "assistant", Content = assistant.Content };
            }, _chatCts.Token);
            if (string.IsNullOrWhiteSpace(assistant.Content))
            {
                var once = await _engine.ChatOnceAsync(prompt, ctx, _chatCts.Token);
                var idx = Messages.IndexOf(assistant);
                Messages[idx] = new ChatMessage { Role = "assistant", Content = once };
            }
            StatusText = "engine connected";
            PushToast("Reply ready", "Assistant stream completed", "");
            EnsureThreads();
            if (ActiveThread != null)
            {
                ActiveThread.Messages = new System.Collections.Generic.List<ChatMessage>(Messages);
                ActiveThread.Updated = System.DateTime.Now;
                if (ActiveThread.Title == "New thread" || ActiveThread.Title.StartsWith("Thread "))
                    ActiveThread.Title = raw.Length > 40 ? raw[..40] : raw;
                _threads!.Save();
            }
            UpdateTokens();
        }
        catch (OperationCanceledException)
        {
            PushToast("Stopped", "Streaming cancelled. Partial text kept.", "");
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

    // ---------- Phase 4.1 Find in file ----------
    [RelayCommand]
    private void FindNext()
    {
        if (ActiveTab == null || string.IsNullOrEmpty(FindText))
        {
            FindStatus = "Type text to find";
            return;
        }
        var text = ActiveTab.Content;
        var cmp = FindCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var idx = text.IndexOf(FindText, cmp);
        if (idx < 0) FindStatus = "0 matches";
        else
        {
            var line = text[..idx].Count(c => c == '\n') + 1;
            FindStatus = $"1 match at line {line}";
            FindJumpOffset = idx;
            FindJumpLength = FindText.Length;
            OnPropertyChanged(nameof(FindJumpOffset));
        }
    }

    public int FindJumpOffset { get; private set; } = -1;
    public int FindJumpLength { get; private set; }

    // ---------- Phase 4.2 Global search ----------
    [RelayCommand]
    private async Task RunSearch()
    {
        _searchCts?.Cancel();
        _searchCts = new System.Threading.CancellationTokenSource();
        var root = _workspace.ActiveRoot;
        if (root == null) { SearchStatus = "Open a folder first"; return; }
        SearchResults.Clear();
        SearchStatus = $"Searching for '{SearchQuery}'...";
        try
        {
            var hits = await _search.SearchAsync(root, SearchQuery, SearchRegex, SearchCase, SearchWord, 2000, _searchCts.Token, batch =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    foreach (var h in batch.Take(50)) SearchResults.Add(h);
                    SearchStatus = $"{SearchResults.Count} hits, scanned {_search.LastScanned} files";
                });
            });
            SearchResults.Clear();
            foreach (var h in hits.Take(500)) SearchResults.Add(h);
            SearchStatus = $"{hits.Count} hits in {_search.LastScanned} files";
            OutputLog += $"[search] '{SearchQuery}' {hits.Count} hits\n";
            EnsureProblemsTab();
        }
        catch (OperationCanceledException) { SearchStatus = "Cancelled"; }
    }

    [RelayCommand]
    private void StopSearch()
    {
        _searchCts?.Cancel();
        SearchStatus = "Cancelled";
    }

    // ---------- Phase 4.3 Replace ----------
    [RelayCommand]
    private void ReplaceSelected()
    {
        if (string.IsNullOrEmpty(SearchQuery)) return;
        var n = _search.ReplaceInFiles(new System.Collections.Generic.List<SearchResult>(SearchResults), SearchQuery, ReplaceText, onlySelected: true);
        PushToast("Replace done", $"{n} replacements", "Undo");
        OutputLog += $"[replace] {n} in selected\n";
        _ = RunSearchCommand.ExecuteAsync(null);
    }

    // ---------- Phase 4.4 Symbols ----------
    [RelayCommand]
    private void RunSymbolSearch()
    {
        SymbolResults.Clear();
        var root = _workspace.ActiveRoot;
        if (root == null || string.IsNullOrWhiteSpace(SymbolQuery)) return;
        foreach (var s in _symbols.Search(root, SymbolQuery, 30)) SymbolResults.Add(s);
        OutputLog += $"[symbols] '{SymbolQuery}' {SymbolResults.Count} hits\n";
    }

    [RelayCommand]
    private void GoToSymbol(SymbolItem? s)
    {
        if (s == null) return;
        OpenFilePath(s.File);
        PushToast("Symbol", $"{s.Name} at line {s.Line}", "");
    }

    // ---------- Phase 4.5 Problems ----------
    private void EnsureProblemsTab()
    {
        if (Problems.Count == 0 && SearchResults.Count > 0)
        {
            BottomTab = "Problems";
            BottomVisible = true;
        }
    }

    [RelayCommand]
    private void RefreshProblems()
    {
        Problems.Clear();
        // Parse OutputLog with matchers so failed builds populate Problems.
        foreach (var p in _problems.Parse(OutputLog, "task"))
        {
            if (ProblemsErrorsOnly && p.Severity != "error") continue;
            Problems.Add(p);
            if (Problems.Count >= 5000) break;
        }
        BottomTab = "Problems";
        BottomVisible = true;
        OutputLog += $"[problems] {Problems.Count} items\n";
    }

    [RelayCommand]
    private void OpenProblem(ProblemItem? p)
    {
        if (p == null || string.IsNullOrEmpty(p.File)) return;
        OpenFilePath(p.File);
    }

    [RelayCommand]
    private void CopyProblemsForChat()
    {
        var md = string.Join("\n", System.Linq.Enumerable.Select(Problems, p => $"- {p.Severity} {p.File}:{p.Line} {p.Message}"));
        Messages.Add(new ChatMessage { Role = "user", Content = "Problems:\n" + md });
        PushToast("Problems attached", $"{Problems.Count} items", "");
    }

    // ---------- Phase 5.1 Stop + tokens ----------
    [RelayCommand]
    private void StopChat()
    {
        _chatCts?.Cancel();
        IsBusy = false;
        PushToast("Stopped", "Streaming cancelled. Partial text kept.", "");
    }

    private void UpdateTokens()
    {
        var chars = (ActiveTab?.Content?.Length ?? 0) + System.Linq.Enumerable.Sum(Messages, m => m.Content?.Length ?? 0)
            + System.Linq.Enumerable.Sum(Chips, c => c.Tokens * 4);
        TokenMeter = $"{chars / 4} / 8192";
    }

    // ---------- Phase 5.2 Code actions ----------
    [RelayCommand]
    private void CopyLastCode()
    {
        var last = System.Linq.Enumerable.LastOrDefault(Messages, m => m.Role == "assistant");
        if (last == null) return;
        var blocks = DiffService.ExtractCodeBlocks(last.Content);
        var code = blocks.Count > 0 ? blocks[^1].Code : last.Content;
        OutputLog += $"[clipboard] code block {code.Length} chars\n";
        PushToast("Code ready", $"{code.Length} chars in Output log", "");
    }

    [RelayCommand]
    private void InsertLastCode()
    {
        var last = System.Linq.Enumerable.LastOrDefault(Messages, m => m.Role == "assistant");
        if (last == null || ActiveTab == null) return;
        var blocks = DiffService.ExtractCodeBlocks(last.Content);
        var code = blocks.Count > 0 ? blocks[^1].Code : last.Content;
        ActiveTab.Content += "\n" + code;
        EditorText = ActiveTab.Content;
        ActiveTab.IsDirty = true;
        PushToast("Inserted", "Code inserted at end of file.", "");
    }

    [RelayCommand]
    private void ApplyLastCode()
    {
        var last = System.Linq.Enumerable.LastOrDefault(Messages, m => m.Role == "assistant");
        if (last == null || ActiveTab == null) return;
        var blocks = DiffService.ExtractCodeBlocks(last.Content);
        var code = blocks.Count > 0 ? blocks[^1].Code : last.Content;
        if (_diff.TryApplyCreate(ActiveTab.FilePath + ".applied", code, out var err))
            PushToast("Applied", ActiveTab.FilePath + ".applied created. Review then accept.", "Undo");
        else PushToast("Apply failed", err, "");
    }

    [RelayCommand]
    private void UndoDiff()
    {
        if (_diff.Undo(out var f)) PushToast("Undone", f, "");
        else PushToast("Nothing to undo", "", "");
    }

    // ---------- Phase 5.3 Chips ----------
    [RelayCommand]
    private void AddFileChip()
    {
        if (ActiveTab == null) return;
        var tokens = (ActiveTab.Content?.Length ?? 0) / 4;
        Chips.Add(new ContextChip { Kind = "file", Label = ActiveTab.Title, Path = ActiveTab.FilePath, Tokens = tokens });
        UpdateTokens();
    }

    [RelayCommand]
    private void ClearChips()
    {
        Chips.Clear();
        UpdateTokens();
    }

    [RelayCommand]
    private void DryRun()
    {
        var prompt = $"chips={Chips.Count} tokens={TokenMeter} mode={ChatMode}";
        OutputLog += $"[dry-run] {prompt}\nInput: {InputText}\n";
        PushToast("Dry run", prompt, "");
    }

    // ---------- Phase 5.4 Slash ----------
    private string ExpandSlash(string input)
    {
        EnsureThreads();
        var sel = ActiveTab?.Content ?? "";
        if (sel.Length > 4000) sel = sel[..4000];
        return _prompts.Expand(input, sel, ActiveTab?.FilePath ?? "");
    }

    // ---------- Phase 5.5 Threads ----------
    private void EnsureThreads()
    {
        if (_threads != null) return;
        _threads = new ThreadService(_workspace.ActiveRoot ?? ".");
        ThreadList.Clear();
        foreach (var t in _threads.Threads) ThreadList.Add(t);
        ActiveThread = ThreadList.Count > 0 ? ThreadList[0] : null;
    }

    [RelayCommand]
    private void NewThread()
    {
        EnsureThreads();
        _threads!.New($"Thread {ThreadList.Count + 1}");
        ThreadList.Clear();
        foreach (var t in _threads.Threads) ThreadList.Add(t);
        ActiveThread = ThreadList[0];
        Messages.Clear();
        foreach (var m in ActiveThread.Messages) Messages.Add(m);
        PushToast("New thread", ActiveThread.Title, "");
    }

    [RelayCommand]
    private void ExportThread()
    {
        EnsureThreads();
        if (ActiveThread == null) return;
        ActiveThread.Messages = new System.Collections.Generic.List<ChatMessage>(Messages);
        var path = _threads!.Export(ActiveThread);
        PushToast("Exported", path, "");
    }
}
