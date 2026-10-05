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
        RememberBase(baseUrl);
        StatusText = "Starting…";
        ActiveActivity = _layout.State.ActiveActivity;
        SideTitle = TitleFor(ActiveActivity);
        SideVisible = _layout.State.SideVisible;
        RightVisible = _layout.State.RightVisible;
        BottomVisible = _layout.State.BottomVisible;
        BottomTab = _layout.State.BottomTab;
        OutputLog = "Activity appears here.\nAssistant and workspace events show up below.\n";
        CurrentTheme = "dark-premium";
        ShowExplorer = ActiveActivity == "Explorer";
        ShowSearch = ActiveActivity == "Search" || ActiveActivity == "Symbols";
        ShowModels = ActiveActivity == "Models";
        ShowGit = ActiveActivity == "Git";
        ShowSettings = ActiveActivity == "Settings";
        SyncNav(ActiveActivity);
        Tabs.CollectionChanged += (_, __) =>
        {
            HasTabs = Tabs.Count > 0;
            ShowWelcome = Tabs.Count == 0;
        };
        Problems.CollectionChanged += (_, __) => NoProblems = Problems.Count == 0;
        ShowProblemsView = BottomTab == "Problems";
        ShowTerminalView = BottomTab == "Terminal";
        ShowTasksView = BottomTab == "Tasks";
        ShowOutputView = BottomTab == "Output";
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
    public ObservableCollection<EngineModel> Models { get; } = new();
    public ObservableCollection<ApprovalCard> Approvals { get; } = new();
    public ObservableCollection<DevTask> Tasks { get; } = new();
    public ObservableCollection<TerminalSession> Terminals { get; } = new();
    public ObservableCollection<GitFile> GitFiles { get; } = new();
    public ObservableCollection<GitLog> GitLogs { get; } = new();
    public ObservableCollection<LaunchProfile> Launches { get; } = new();
    public ObservableCollection<CommandItem> ExtMenu { get; } = new();
    public ObservableCollection<ExtensionManifest> ExtensionList { get; } = new();
    public ObservableCollection<McpServer> McpList { get; } = new();

    [ObservableProperty]
    public partial LaunchProfile? SelectedLaunch { get; set; }

    [ObservableProperty]
    public partial bool PreviewMode { get; set; } = false;

    [ObservableProperty]
    public partial string PreviewText { get; set; } = "";

    [ObservableProperty]
    public partial bool CloudEnabled { get; set; } = false;

    [ObservableProperty]
    public partial string CloudModel { get; set; } = "gpt-4o-mini";

    [ObservableProperty]
    public partial McpServer? SelectedMcp { get; set; }

    [ObservableProperty]
    public partial string McpTools { get; set; } = "";

    [ObservableProperty]
    public partial string McpToolName { get; set; } = "";

    [ObservableProperty]
    public partial string McpToolArgs { get; set; } = "{}";

    [ObservableProperty]
    public partial string GitBranch { get; set; } = "";

    [ObservableProperty]
    public partial string GitAhead { get; set; } = "";

    [ObservableProperty]
    public partial string GitDiff { get; set; } = "";

    [ObservableProperty]
    public partial string CommitMessage { get; set; } = "";

    [ObservableProperty]
    public partial string GitLogQuery { get; set; } = "";

    [ObservableProperty]
    public partial GitFile? SelectedGitFile { get; set; }

    [ObservableProperty]
    public partial string ActiveModelId { get; set; } = "";

    [ObservableProperty]
    public partial EngineModel? SelectedModel { get; set; }

    [ObservableProperty]
    public partial string ModelStatus { get; set; } = "No model loaded. Presets work offline after download.";

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
    public partial string GhostText { get; set; } = "";

    [ObservableProperty]
    public partial bool GhostVisible { get; set; } = false;

    [ObservableProperty]
    public partial string GhostWhy { get; set; } = "";

    [ObservableProperty]
    public partial bool CompleteEnabled { get; set; } = true;

    [ObservableProperty]
    public partial string SelectionText { get; set; } = "";

    [ObservableProperty]
    public partial bool HasSelection { get; set; } = false;

    [ObservableProperty]
    public partial string ChatMode { get; set; } = "Edit";

    [ObservableProperty]
    public partial string TerminalInput { get; set; } = "";

    [ObservableProperty]
    public partial TerminalSession? ActiveTerminal { get; set; }

    [ObservableProperty]
    public partial string AgentStatus { get; set; } = "idle";

    [ObservableProperty]
    public partial ChatThread? ActiveThread { get; set; }

    partial void OnExplorerFilterChanged(string value) => RefreshExplorer();
    partial void OnShowExcludedChanged(bool value) => RefreshExplorer();

    [ObservableProperty]
    public partial string CurrentTheme { get; set; } = "dark-premium";

    [ObservableProperty]
    public partial string ActiveActivity { get; set; } = "Explorer";

    [ObservableProperty]
    public partial string SideTitle { get; set; } = "Files";

    [ObservableProperty]
    public partial bool HasTabs { get; set; } = false;

    [ObservableProperty]
    public partial bool ShowWelcome { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowSettings { get; set; } = false;

    [ObservableProperty]
    public partial bool NoProblems { get; set; } = true;

    [ObservableProperty]
    public partial string StatusModel { get; set; } = "No assistant";

    public string ThemeLabel => CurrentTheme == "light-pro" ? "Light" : "Dark";

    [ObservableProperty]
    public partial bool NavExplorer { get; set; } = true;

    [ObservableProperty]
    public partial bool NavSearch { get; set; } = false;

    [ObservableProperty]
    public partial bool NavGit { get; set; } = false;

    [ObservableProperty]
    public partial bool NavChat { get; set; } = false;

    [ObservableProperty]
    public partial bool NavModels { get; set; } = false;

    [ObservableProperty]
    public partial bool NavSettings { get; set; } = false;

    private void SyncNav(string id)
    {
        NavExplorer = id == "Explorer";
        NavSearch = id is "Search" or "Symbols";
        NavGit = id == "Git";
        NavChat = id == "Chat";
        NavModels = id == "Models";
        NavSettings = id == "Settings";
    }

    private static string TitleFor(string activity)
    {
        return activity switch
        {
            "Explorer" => "Files",
            "Search" => "Search",
            "Symbols" => "Symbols",
            "Models" => "Assistants",
            "Git" => "Source control",
            "Chat" => "Assistant",
            "Settings" => "Settings",
            _ => "Files",
        };
    }

    [ObservableProperty]
    public partial bool ShowExplorer { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowSearch { get; set; } = false;

    [ObservableProperty]
    public partial bool ShowModels { get; set; } = false;

    [ObservableProperty]
    public partial bool ShowGit { get; set; } = false;

    [ObservableProperty]
    public partial bool ShowProblemsView { get; set; } = false;

    [ObservableProperty]
    public partial bool ShowTerminalView { get; set; } = false;

    [ObservableProperty]
    public partial bool ShowTasksView { get; set; } = false;

    [ObservableProperty]
    public partial bool ShowOutputView { get; set; } = true;

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
        SideTitle = TitleFor(id);
        SyncNav(id);
        ShowExplorer = id == "Explorer";
        ShowSearch = id == "Search" || id == "Symbols";
        ShowModels = id == "Models";
        ShowGit = id == "Git";
        ShowSettings = id == "Settings";
        if (id == "Chat")
        {
            SideVisible = false;
            RightVisible = true;
        }
        else
        {
            SideVisible = true;
        }
        _layout.State.ActiveActivity = id;
        _layout.State.SideVisible = true;
        _layout.Save();
        if (ShowModels) _ = RefreshModelsAsync();
        if (ShowGit) _ = RefreshGitAsync();
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
        ShowTerminalView = tab == "Terminal";
        ShowTasksView = tab == "Tasks";
        ShowOutputView = tab == "Output";
        _layout.State.BottomTab = tab;
        _layout.State.BottomVisible = true;
        _layout.Save();
        if (tab == "Problems") RefreshProblemsCommand.Execute(null);
        if (tab == "Tasks" && Tasks.Count == 0) LoadTasksCommand.Execute(null);
        if (tab == "Terminal" && ActiveTerminal == null) NewTerminalCommand.Execute(null);
    }

    partial void OnCurrentThemeChanged(string value) => OnPropertyChanged(nameof(ThemeLabel));

    [RelayCommand]
    private void ToggleTheme()
    {
        _theme.Toggle();
        CurrentTheme = _theme.Current;
        OutputLog += $"[appearance] {(CurrentTheme == "light-pro" ? "Light" : "Dark")}\n";
    }

    [RelayCommand]
    private void OpenPalette()
    {
        try
        {
            var vm = new CommandPaletteViewModel();
            var win = new Views.CommandPalette { DataContext = vm };
            OutputLog += "[commands] opened\n";
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

    public object? PendingFolderRequest { get; private set; }

    [RelayCommand]
    private void RequestFolder()
    {
        PendingFolderRequest = new object();
        OnPropertyChanged(nameof(PendingFolderRequest));
    }

    [ObservableProperty]
    public partial string EditorText { get; set; } = "// Welcome to Quant.\n// Open any file on the left, or ask the assistant on the right.\n// Your code never leaves this device.\n\nfn main() {\n    println!(\"hello quant\");\n}\n";

    [ObservableProperty]
    public partial string InputText { get; set; } = "";

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Starting…";

    [ObservableProperty]
    public partial bool IsBusy { get; set; } = false;

    public ObservableCollection<ChatMessage> Messages { get; } = new()
    {
        new ChatMessage { Role = "assistant", Content = "Welcome to Quant. Open a file or ask anything. Your code never leaves this device." }
    };

    [RelayCommand]
    private async Task CheckHealth()
    {
        StatusText = "Waking up…";
        OutputLog += "[assistant] checking…\n";
        // Try auto-start sidecar before reporting offline.
        if (!await EngineSupervisor.Healthy(_port))
        {
            OutputLog += "[assistant] starting in the background…\n";
            await new EngineSupervisor().EnsureRunningAsync(_port);
        }
        var h = await _engine.GetHealthAsync();
        try
        {
            using var doc = JsonDocument.Parse(h);
            var v = doc.RootElement.GetProperty("version").GetString();
            StatusText = "Ready";
            OutputLog += $"[assistant] ready (v{v})\n";
            PushToast("Assistant ready", "Everything runs on this device.", "View activity");
        }
        catch
        {
            StatusText = "Starting…";
            OutputLog += "[assistant] still starting…\n";
            PushToast("Starting up", "This takes a moment on first run.", "Retry");
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
            // Phase 13.3 cloud opt-in route with triple guard. Default stays local.
            var cloud = await TryCloudAsync(prompt, _chatCts.Token);
            if (cloud != null)
            {
                assistant.Content = cloud;
                var cidx = Messages.IndexOf(assistant);
                Messages[cidx] = new ChatMessage { Role = "assistant", Content = cloud };
                StatusText = "cloud connected";
                PushToast("Reply ready", "Cloud reply completed", "");
            }
            else
            {
            var ctx = ActiveTab?.Content ?? EditorText;
            if (Chips.Count > 0)
                ctx += "\n\nAttached:\n" + string.Join("\n", System.Linq.Enumerable.Select(Chips, c => $"- {c.Kind} {c.Label}"));
            // Phase 7.3 auto RAG citations, best effort under 2s
            var cites = await RagCitationsAsync(raw);
            if (!string.IsNullOrEmpty(cites)) ctx += cites;
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
            }
            StatusText = "Ready";
            PushToast("Reply ready", "Assistant finished writing.", "");
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
            Messages[idx] = new ChatMessage { Role = "assistant", Content = $"Hmm, that did not go through ({ex.Message}). Give it another try in a moment." };
            StatusText = "Starting…";
            PushToast("Not ready yet", "The assistant is still starting.", "Retry");
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
    private void EnsureChildren(FileNode? node)
    {
        if (node == null || !node.IsDirectory || node.ChildrenLoaded) return;
        _explorer.LoadChildren(node, ExplorerFilter, ExplorerSort, ShowExcluded, 1);
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
            if (tab.LargeFileMode) PushToast("Big file", $"{tab.Title} opened in a safe preview.", "");
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
        ScheduleGhost();
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
            if (ActiveTab.LargeFileMode) { PushToast("Read-only preview", "Open the file directly to edit it.", ""); return; }
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
        PushToast("Stopped", "Kept what was written so far.", "");
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
        var helpers = new System.Collections.Generic.List<string>(_extensions.ChatTools()).Count;
        var prompt = $"chips={Chips.Count} tokens={TokenMeter} mode={ChatMode} helpers={helpers} cloud={(CloudEnabled ? "on" : "off")}";
        OutputLog += $"[dry-run] {prompt}\nInput: {InputText}\n";
        PushToast("Dry run", prompt.Length > 120 ? prompt[..120] : prompt, "");
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

    // ---------- Phase 6/7 Models + RAG + privacy ----------
    private string _baseUrl = "http://127.0.0.1:3737";

    private void RememberBase(string baseUrl) => _baseUrl = baseUrl;

    [RelayCommand]
    private async Task RefreshModels()
    {
        await RefreshModelsAsync();
    }

    [RelayCommand]
    private void OpenModelsFolder()
    {
        try
        {
            var dir = System.IO.Path.Combine(System.AppContext.BaseDirectory, "models");
            System.IO.Directory.CreateDirectory(dir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true,
            });
            PushToast("Models folder", "Drop any .gguf file in, then Refresh.", "");
        }
        catch (System.Exception ex) { PushToast("Please try again", ex.Message, ""); }
    }

    private async Task RefreshModelsAsync()
    {
        try
        {
            using var svc = new ModelService(_baseUrl);
            var list = await svc.ListAsync();
            Models.Clear();
            foreach (var m in list) Models.Add(m);
            var loaded = System.Linq.Enumerable.FirstOrDefault(Models, m => m.Loaded);
            ActiveModelId = loaded?.Id ?? "";
            StatusModel = loaded?.DisplayName ?? "No assistant";
            ModelStatus = loaded != null
                ? $"Using {loaded.DisplayName}"
                : "Pick an assistant to begin. Everything runs on this device.";
            OutputLog += $"[assistants] {Models.Count} available\n";
        }
        catch { ModelStatus = "Could not reach the assistant. Trying again…"; }
    }

    [RelayCommand]
    private async Task LoadSelectedModel()
    {
        var id = SelectedModel?.Id ?? ActiveModelId;
        if (string.IsNullOrWhiteSpace(id))
        {
            var first = System.Linq.Enumerable.FirstOrDefault(Models);
            if (first == null) { PushToast("Nothing here yet", "Pull down to refresh first.", ""); return; }
            id = first.Id;
        }
        await LoadModelAsync(id);
    }

    private static string FriendlyModel(string id, System.Collections.Generic.IEnumerable<EngineModel> models)
    {
        foreach (var m in models)
            if (m.Id == id) return m.DisplayName;
        return "assistant";
    }

    private async Task LoadModelAsync(string id)
    {
        string name = FriendlyModel(id, Models);
        try
        {
            using var svc = new ModelService(_baseUrl);
            var body = await svc.LoadAsync(id);
            if (body.Contains("INSUFFICIENT_RAM"))
            {
                ModelStatus = "This device is low on memory. Try Assistant Light.";
                PushToast("Not enough memory", "Assistant Light needs much less.", "");
            }
            else if (body.Contains("MODEL_NOT_FOUND"))
            {
                ModelStatus = "That assistant is not available.";
                PushToast("Not found", "Please pick another one.", "");
            }
            else
            {
                await RefreshModelsAsync();
                name = FriendlyModel(id, Models);
                ModelStatus = $"Using {name}";
                PushToast("All set", $"You are now talking to {name}.", "");
            }
        }
        catch { PushToast("Please try again", "The switch did not go through.", ""); }
    }

    [RelayCommand]
    private async Task UnloadModel()
    {
        try
        {
            using var svc = new ModelService(_baseUrl);
            await svc.UnloadAsync();
            await RefreshModelsAsync();
            PushToast("Paused", "It will wake up when you need it.", "");
        }
        catch { PushToast("Please try again", "That did not go through.", ""); }
    }

    [RelayCommand]
    private async Task RefreshIndex()
    {
        var root = _workspace.ActiveRoot;
        if (root == null) return;
        try
        {
            using var http = new System.Net.Http.HttpClient { BaseAddress = new System.Uri(_baseUrl), Timeout = System.TimeSpan.FromMinutes(5) };
            var json = System.Text.Json.JsonSerializer.Serialize(new { roots = new[] { root }, full = false });
            var res = await http.PostAsync("/v1/index/refresh", new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json"));
            await res.Content.ReadAsStringAsync();
            OutputLog += "[knowledge] workspace relearned\n";
            PushToast("Knowledge refreshed", "The assistant relearned this workspace.", "");
        }
        catch { PushToast("Please try again", "That did not go through.", ""); }
    }

    [RelayCommand]
    private void ClearIndex()
    {
        try
        {
            if (System.IO.Directory.Exists("./index"))
                System.IO.Directory.Delete("./index", true);
            OutputLog += "[privacy] workspace forgotten\n";
            PushToast("Forgotten", "What was learned about this workspace is gone.", "");
        }
        catch { PushToast("Please try again", "That did not go through.", ""); }
    }

    private async Task<string> RagCitationsAsync(string query)
    {
        try
        {
            using var svc = new ModelService(_baseUrl);
            var hits = await svc.CodeSearchAsync(_baseUrl, query, 3);
            if (hits.Count == 0) return "";
            OutputLog += $"[rag] {hits.Count} hits\n";
            return "\n\nCitations:\n" + string.Join("\n", System.Linq.Enumerable.Select(hits, h => $"- {h.Path}:{h.Start} score {h.Score:F2} {h.Why}"));
        }
        catch { return ""; }
    }

    // ---------- Phase 8.2 Approvals ----------
    [RelayCommand]
    private void SetMode(string mode)
    {
        ChatMode = mode;
        OutputLog += $"[agent] mode={mode}\n";
    }

    [RelayCommand]
    private async Task RunAgent()
    {
        if (IsBusy || string.IsNullOrWhiteSpace(InputText)) return;
        var msg = InputText.Trim();
        InputText = "";
        AgentStatus = "planning";
        Messages.Add(new ChatMessage { Role = "user", Content = $"[{ChatMode}] {msg}" });
        var working = new ChatMessage { Role = "assistant", Content = "Working on it…" };
        Messages.Add(working);
        void RefreshWorking()
        {
            var idx = Messages.IndexOf(working);
            if (idx >= 0) Messages[idx] = new ChatMessage { Role = "assistant", Content = working.Content };
        }
        void Step(string line)
        {
            working.Content += "\n" + line;
            RefreshWorking();
        }
        OutputLog += $"[agent] run mode={ChatMode}\n";
        try
        {
            using var svc = new AgentService(_baseUrl);
            var root = _workspace.ActiveRoot ?? ".";
            var steps = await svc.RunAsync(ChatMode, msg, root, data =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => OutputLog += $"[agent] {data}\n");
            });
            var executed = 0;
            foreach (var (type, tool, text) in steps)
            {
                if (type == "awaiting_approval")
                {
                    string reason = tool, argsJson = text;
                    try
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(text);
                        if (doc.RootElement.TryGetProperty("reason", out var r)) reason = r.GetString() ?? tool;
                        if (doc.RootElement.TryGetProperty("args", out var a)) argsJson = a.GetRawText();
                    }
                    catch { }
                    Approvals.Add(new ApprovalCard
                    {
                        Id = System.Guid.NewGuid().ToString("N")[..8],
                        Tool = tool,
                        Args = argsJson.Length > 200 ? argsJson[..200] : argsJson,
                        FullArgs = argsJson,
                        Reason = reason,
                        Context = msg,
                    });
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => Step($"• Needs your approval: {FriendlyStep(tool)}"));
                    continue;
                }
                if (type != "tool_result") continue;
                try
                {
                    if (tool == "grep")
                    {
                        var body = await svc.GrepAsync(msg, root);
                        var n = CountHits(body);
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => Step($"• Searched the workspace — {n} hits"));
                        executed++;
                    }
                    else if (tool == "glob")
                    {
                        await svc.GlobAsync("**/*", root);
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => Step("• Listed matching files"));
                        executed++;
                    }
                    else if (tool == "read")
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => Step("• Reading the most relevant file…"));
                        executed++;
                    }
                }
                catch (System.Exception ex)
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => Step($"• Skipped a step ({ex.Message})"));
                }
            }
            if (Approvals.Count > 0)
            {
                Step("• Waiting for your approval below to continue.");
                AgentStatus = "awaiting approval";
            }
            else
            {
                Step(executed > 0 ? "• Done. Ask for an edit to continue." : "• Done.");
                AgentStatus = "done";
            }
            if (ChatMode == "Ask" && Approvals.Count == 0) AgentStatus = "done";
            EnsureThreads();
            if (ActiveThread != null)
            {
                ActiveThread.Messages = new System.Collections.Generic.List<ChatMessage>(Messages);
                ActiveThread.Updated = System.DateTime.Now;
                _threads!.Save();
            }
        }
        catch (System.Exception ex)
        {
            AgentStatus = "error";
            working.Content += $"\n• Something went wrong ({ex.Message}).";
            RefreshWorking();
            PushToast("Agent failed", ex.Message, "");
        }
    }

    private static string FriendlyStep(string tool)
    {
        return tool switch
        {
            "grep" => "searching code",
            "read" => "reading a file",
            "glob" => "listing files",
            "symbols" => "looking up a symbol",
            "apply_diff" => "editing a file",
            "exec" => "running a command",
            _ when tool.StartsWith("mcp:", System.StringComparison.Ordinal) => "using a connector",
            _ => "working",
        };
    }

    private static int CountHits(string body)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("hits", out var h)) return h.GetArrayLength();
        }
        catch { }
        return 0;
    }

    [RelayCommand]
    private async Task ApproveCard(ApprovalCard? card)
    {
        card ??= System.Linq.Enumerable.LastOrDefault(Approvals);
        if (card == null) return;
        try
        {
            // MCP tools execute locally via stdio, no engine token needed.
            if (card.Tool.StartsWith("mcp:", StringComparison.Ordinal))
            {
                var rest = card.Tool["mcp:".Length..];
                var slash = rest.IndexOf('/');
                var serverName = slash > 0 ? rest[..slash] : rest;
                var tool = slash > 0 ? rest[(slash + 1)..] : "example.tool";
                var server = System.Linq.Enumerable.FirstOrDefault(McpList, s => s.Name == serverName)
                    ?? System.Linq.Enumerable.FirstOrDefault(_mcp.Servers, s => s.Name == serverName);
                if (server == null) { PushToast("MCP missing", serverName, ""); return; }
                var result = await _mcp.CallToolAsync(server, tool, card.Args);
                card.Status = "approved";
                OutputLog += $"[mcp] {serverName}/{tool} -> {(result.Length > 300 ? result[..300] : result)}\n";
                PushToast("MCP done", $"{tool} returned {result.Length} chars", "");
                Approvals.Remove(card);
                if (Approvals.Count == 0) AgentStatus = "done";
                return;
            }
            using var svc = new AgentService(_baseUrl);
            // Approving an edit asks the assistant to write the change as a code block.
            if (card.Tool == "apply_diff" && !string.IsNullOrWhiteSpace(card.Context))
            {
                card.Status = "approved";
                Approvals.Remove(card);
                if (Approvals.Count == 0) AgentStatus = "done";
                InputText = $"Make this change and show the full updated code:\n{card.Context}";
                PushToast("Working on the edit", "The proposal appears below.", "");
                await SendCommand.ExecuteAsync(null);
                return;
            }
            // Approving a command runs it with a one-time token, then reports back.
            if (card.Tool == "exec")
            {
                var command = ExtractCommand(card.FullArgs);
                var token = await svc.GrantAsync("workspace", card.Tool);
                var root = _workspace.ActiveRoot ?? ".";
                var result = await svc.ExecAsync(command, root, token);
                card.Status = "approved";
                Approvals.Remove(card);
                if (Approvals.Count == 0) AgentStatus = "done";
                var tail = SummarizeExec(result);
                Messages.Add(new ChatMessage { Role = "assistant", Content = tail });
                OutputLog += $"[exec] {command}\n{tail}\n";
                PushToast("Command finished", command.Length > 80 ? command[..80] : command, "");
                return;
            }
            var fallback = await svc.GrantAsync("workspace", card.Tool);
            card.Status = "approved";
            OutputLog += $"[approval] {card.Label} allowed once ({fallback.Length} chars token)\n";
            PushToast("Allowed once", $"{card.Label}: {card.Reason}", "");
            Approvals.Remove(card);
            if (Approvals.Count == 0) AgentStatus = "done";
        }
        catch (System.Exception ex) { PushToast("Approve failed", ex.Message, ""); }
    }

    private static string ExtractCommand(string argsJson)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(argsJson);
            if (doc.RootElement.TryGetProperty("command", out var c)) return c.GetString() ?? "";
        }
        catch { }
        return argsJson.Length > 200 ? argsJson[..200] : argsJson;
    }

    private static string SummarizeExec(string body)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            var code = doc.RootElement.TryGetProperty("code", out var c) ? c.ToString() : "?";
            var stdout = doc.RootElement.TryGetProperty("stdout", out var s) ? s.GetString() ?? "" : body;
            if (stdout.Length > 1500) stdout = stdout[^1500..];
            return $"Ran with exit {code}:\n{stdout.Trim()}";
        }
        catch
        {
            return body.Length > 1500 ? body[^1500..] : body;
        }
    }

    [RelayCommand]
    private void DenyCard(ApprovalCard? card)
    {
        card ??= System.Linq.Enumerable.LastOrDefault(Approvals);
        if (card == null) return;
        card.Status = "denied";
        OutputLog += $"[approval] {card.Tool} denied\n";
        Approvals.Remove(card);
        if (Approvals.Count == 0) AgentStatus = "done";
    }

    // ---------- Phase 9.3 Terminal ----------
    [RelayCommand]
    private void NewTerminal()
    {
        var term = new TerminalSession(_workspace.ActiveRoot ?? ".");
        term.Changed += () => OnPropertyChanged(nameof(TerminalOutput));
        Terminals.Add(term);
        ActiveTerminal = term;
        BottomTab = "Terminal";
        BottomVisible = true;
        ShowProblemsView = false;
        OutputLog += $"[terminal] new {term.Id}\n";
    }

    public string TerminalOutput => ActiveTerminal?.Output.ToString() ?? "No terminal. Press New.";

    [RelayCommand]
    private void SendTerminal()
    {
        if (ActiveTerminal == null || string.IsNullOrWhiteSpace(TerminalInput)) return;
        var cmd = TerminalInput.Trim();
        TerminalInput = "";
        ActiveTerminal.Send(cmd);
        OnPropertyChanged(nameof(TerminalOutput));
    }

    [RelayCommand]
    private void KillTerminal()
    {
        ActiveTerminal?.Kill();
        OnPropertyChanged(nameof(TerminalOutput));
    }

    [RelayCommand]
    private void AttachTerminalOutput()
    {
        if (ActiveTerminal == null) return;
        var text = ActiveTerminal.Output.ToString();
        if (text.Length > 4000) text = text[^4000..];
        Chips.Add(new ContextChip { Kind = "terminal", Label = $"terminal {ActiveTerminal.Id}", Path = "", Tokens = text.Length / 4 });
        Messages.Add(new ChatMessage { Role = "user", Content = "Terminal output:\n" + text });
        UpdateTokens();
    }

    // ---------- Phase 9.4 Tasks ----------
    [RelayCommand]
    private void LoadTasks()
    {
        Tasks.Clear();
        var svc = new TaskService();
        foreach (var t in svc.Load(_workspace.ActiveRoot ?? ".")) Tasks.Add(t);
        OutputLog += $"[tasks] {Tasks.Count} loaded\n";
    }

    [RelayCommand]
    private void RunTask(DevTask? task)
    {
        task ??= System.Linq.Enumerable.FirstOrDefault(Tasks);
        if (task == null) return;
        task.Running = true;
        BottomTab = "Tasks";
        BottomVisible = true;
        OutputLog += $"[task] run {task.Label}: {task.Command}\n";
        var term = new TerminalSession(task.Cwd);
        term.Changed += () =>
        {
            OutputLog = term.Output.ToString();
            // Live problem parsing so failed builds populate Problems.
            Problems.Clear();
            foreach (var p in _problems.Parse(term.Output.ToString(), "task"))
                Problems.Add(p);
        };
        Terminals.Add(term);
        ActiveTerminal = term;
        term.Send(task.Command);
        task.Running = false;
        OnPropertyChanged(nameof(TerminalOutput));
    }

    // ---------- Phase 11 Git ----------
    private async System.Threading.Tasks.Task RefreshGitAsync()
    {
        var root = _workspace.ActiveRoot;
        if (root == null) return;
        GitBranch = await GitService.Branch(root);
        GitAhead = await GitService.AheadBehind(root);
        var files = await GitService.Status(root);
        GitFiles.Clear();
        foreach (var f in files.Take(200)) GitFiles.Add(f);
        var logs = await GitService.Log(root, GitLogQuery, 50);
        GitLogs.Clear();
        foreach (var l in logs) GitLogs.Add(l);
        StatusText = $"git {GitBranch} {GitAhead}".Trim();
        OutputLog += $"[git] {GitBranch} {GitFiles.Count} changed\n";
    }

    [RelayCommand]
    private async Task RefreshGit()
    {
        await RefreshGitAsync();
    }

    [RelayCommand]
    private async Task StageGitFile(GitFile? file)
    {
        file ??= SelectedGitFile;
        var root = _workspace.ActiveRoot;
        if (file == null || root == null) return;
        await GitService.Stage(root, file.Path);
        OutputLog += $"[git] stage {file.Path}\n";
        await RefreshGitAsync();
    }

    [RelayCommand]
    private async Task ShowGitDiff(GitFile? file)
    {
        file ??= SelectedGitFile;
        var root = _workspace.ActiveRoot;
        if (file == null || root == null) return;
        GitDiff = await GitService.Diff(root, file.Path);
        if (string.IsNullOrWhiteSpace(GitDiff)) GitDiff = "(no diff or binary)";
        BottomTab = "Output";
        BottomVisible = true;
        ShowOutputView = true;
        OutputLog = GitDiff;
    }

    [RelayCommand]
    private async Task CommitNow()
    {
        var root = _workspace.ActiveRoot;
        if (root == null || string.IsNullOrWhiteSpace(CommitMessage)) { PushToast("Empty message", "Type a commit message first.", ""); return; }
        var res = await GitService.Commit(root, CommitMessage);
        OutputLog += $"[git] commit\n{res}\n";
        CommitMessage = "";
        await RefreshGitAsync();
        PushToast("Committed", GitBranch, "");
    }

    [RelayCommand]
    private async Task GenCommitMsg()
    {
        var root = _workspace.ActiveRoot;
        if (root == null) return;
        var files = await GitService.Status(root);
        var scope = files.Count > 0 ? files[0].Path : "workspace";
        InputText = $"/commit-msg {scope}";
        await SendCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task ShowPriorVersion(GitLog? log)
    {
        var root = _workspace.ActiveRoot;
        var file = SelectedGitFile?.Path ?? SelectedNode?.FullPath;
        if (root == null || log == null || string.IsNullOrEmpty(file)) { PushToast("Select first", "Pick a history row and a file.", ""); return; }
        var rel = System.IO.Path.IsPathRooted(file) ? System.IO.Path.GetRelativePath(root, file) : file;
        var text = await GitService.Show(root, log.Hash, rel);
        var tab = new EditorTab { FilePath = $"{rel}@{log.Hash}", Title = $"{System.IO.Path.GetFileName(rel)}@{log.Hash}", Content = string.IsNullOrWhiteSpace(text) ? "(empty or binary)" : text, IsPreview = true, Language = DetectLanguage(rel), LargeFileMode = text.Length > 200_000 };
        Tabs.Add(tab);
        ActiveTab = tab;
        SyncEditorFromTab();
    }

    // ---------- Phase 12.1 Launch ----------
    private readonly LaunchService _launch = new();

    [RelayCommand]
    private void LoadLaunches()
    {
        Launches.Clear();
        _launch.Output += s => OutputLog += s;
        foreach (var p in _launch.Load(_workspace.ActiveRoot ?? ".")) Launches.Add(p);
        OutputLog += $"[launch] {Launches.Count} profiles\n";
    }

    [RelayCommand]
    private void WriteLaunchTemplate()
    {
        _launch.WriteTemplate(_workspace.ActiveRoot ?? ".");
        LoadLaunchesCommand.Execute(null);
        PushToast("Template written", ".quant/launch.json", "");
    }

    [RelayCommand]
    private void StartLaunch(LaunchProfile? p)
    {
        p ??= SelectedLaunch ?? System.Linq.Enumerable.FirstOrDefault(Launches);
        if (p == null) return;
        _launch.Output += s => OutputLog += s;
        if (_launch.Start(p)) PushToast("Launched", p.Name, "");
        else PushToast("Launch failed", p.Name, "");
    }

    [RelayCommand]
    private void StopLaunch(LaunchProfile? p)
    {
        p ??= SelectedLaunch;
        if (p == null) return;
        _launch.Stop(p.Name);
        p.Running = false;
        PushToast("Stopped", p.Name, "");
    }

    // ---------- Phase 12.2 Preview ----------
    [RelayCommand]
    private void TogglePreview()
    {
        if (ActiveTab == null || !MarkdownService.IsPreviewable(ActiveTab.FilePath))
        {
            PushToast("Preview", "Open a .md or .html file first.", "");
            return;
        }
        PreviewMode = !PreviewMode;
        PreviewText = PreviewMode ? MarkdownService.ToPreview(ActiveTab.Content) : "";
        OutputLog += $"[preview] {(PreviewMode ? "on" : "off")} {ActiveTab.Title}\n";
    }

    // ---------- Phase 12.3 Matchers ----------
    [RelayCommand]
    private void TestMatcher()
    {
        var sample = "src/App.cs(42,5): error CS1002: ; expected\nerror: failed -->\n src/main.rs:10:5";
        var parsed = _problems.Parse(sample, "matcher-test");
        PushToast("Matcher test", $"{parsed.Count} problems from sample", "");
        OutputLog += $"[matcher] {parsed.Count} sample hits\n";
    }

    [RelayCommand]
    private void CopyFailureForChat()
    {
        var text = ActiveTerminal?.Output.ToString() ?? OutputLog;
        if (text.Length > 4000) text = text[^4000..];
        Messages.Add(new ChatMessage { Role = "user", Content = "Failure output:\n" + text });
        PushToast("Failure attached", "Terminal tail added to chat.", "");
    }

    // ---------- Phase 13 seams (deep) ----------
    private readonly ExtensionHost _extensions = new();
    private readonly McpService _mcp = new();
    private readonly CloudProvider _cloud = new();

    [RelayCommand]
    private void ToggleCloud()
    {
        CloudEnabled = !CloudEnabled;
        OutputLog += $"[cloud] {(CloudEnabled ? "opt-in requested (key " + _cloud.KeyPresent + ")" : "disabled")}\n";
        if (CloudEnabled) PushToast("Cloud opt-in", "Key " + _cloud.KeyPresent + ". No traffic unless key present.", "");
    }

    // ---------- Phase 13 deep: extensions ----------
    [RelayCommand]
    private void ListExtensions()
    {
        _extensions.Load(_workspace.ActiveRoot ?? ".");
        ExtensionList.Clear();
        ExtMenu.Clear();
        foreach (var m in _extensions.Manifests()) ExtensionList.Add(m);
        foreach (var c in _extensions.AllCommands()) OutputLog += $"[ext] {c.Id} {c.Title}\n";
        foreach (var c in _extensions.ExplorerMenu()) ExtMenu.Add(c);
        var tools = string.Join(",", _extensions.ChatTools());
        OutputLog += $"[ext] chat tools: {tools}\n";
        PushToast("Extensions", $"{ExtensionList.Count} loaded. Menu {ExtMenu.Count}.", "");
    }

    [RelayCommand]
    private void ToggleExtension(ExtensionManifest? m)
    {
        m ??= System.Linq.Enumerable.FirstOrDefault(ExtensionList);
        if (m == null) return;
        _extensions.SetEnabled(m.Id, !m.Enabled, _workspace.ActiveRoot ?? ".");
        m.Enabled = !m.Enabled;
        ListExtensionsCommand.Execute(null);
    }

    [RelayCommand]
    private void RunExtension(string id)
    {
        var target = string.IsNullOrWhiteSpace(id) ? "echo.hello" : id;
        var path = SelectedNode?.FullPath ?? _workspace.ActiveRoot ?? ".";
        var result = _extensions.Execute(target, path);
        OutputLog += $"[ext] {target} -> {result}\n";
        PushToast("Extension", result.Length > 120 ? result[..120] : result, "");
    }

    // ---------- Phase 13 deep: MCP ----------
    [RelayCommand]
    private void LoadMcp()
    {
        _mcp.Load(_workspace.ActiveRoot ?? ".");
        McpList.Clear();
        foreach (var s in _mcp.Servers) McpList.Add(s);
        SelectedMcp ??= System.Linq.Enumerable.FirstOrDefault(McpList);
        PushToast("MCP", $"{McpList.Count} servers. Disabled unless enabled in mcp.json.", "");
        OutputLog += $"[mcp] {McpList.Count} servers\n{_mcp.Log}";
    }

    [RelayCommand]
    private async Task ListMcpTools()
    {
        if (SelectedMcp == null) { PushToast("No server", "Load MCP first.", ""); return; }
        McpTools = await _mcp.ListToolsAsync(SelectedMcp);
        if (string.IsNullOrWhiteSpace(McpTools)) McpTools = $"(empty: {_mcp.LastError})";
        OutputLog += $"[mcp] tools {SelectedMcp.Name} {McpTools.Length} chars\n";
    }

    [RelayCommand]
    private void DryRunMcp()
    {
        if (SelectedMcp == null) return;
        var payload = McpService.DryRunPayload(string.IsNullOrWhiteSpace(McpToolName) ? "example.tool" : McpToolName, McpToolArgs);
        OutputLog += $"[mcp dry-run] {SelectedMcp.Name}\n{payload}\n";
        PushToast("MCP dry run", "Payload in Output. Nothing executed.", "");
    }

    [RelayCommand]
    private void CallMcpTool()
    {
        if (SelectedMcp == null) return;
        var tool = string.IsNullOrWhiteSpace(McpToolName) ? "example.tool" : McpToolName;
        Approvals.Add(new ApprovalCard { Id = System.Guid.NewGuid().ToString("N")[..8], Tool = $"mcp:{SelectedMcp.Name}/{tool}", Args = McpToolArgs.Length > 2000 ? McpToolArgs[..2000] : McpToolArgs, Reason = $"Run MCP tool {tool} on {SelectedMcp.Name} (10s timeout)" });
        AgentStatus = "awaiting approval";
        PushToast("Approval needed", $"mcp {tool}", "");
    }

    // ---------- Phase 13 deep: cloud route ----------
    private async Task<string?> TryCloudAsync(string message, System.Threading.CancellationToken ct)
    {
        if (!CloudEnabled) return null;
        var key = KeyStore.Read();
        if (string.IsNullOrEmpty(key)) { OutputLog += "[cloud] enabled but key missing, staying local\n"; return null; }
        try
        {
            using var provider = new OpenAiCompatProvider { Model = string.IsNullOrWhiteSpace(CloudModel) ? "gpt-4o-mini" : CloudModel };
            OutputLog += $"[cloud] {provider.CostFor(message.Length / 4)}\n";
            return await provider.ChatAsync(message, ct);
        }
        catch (System.Exception ex)
        {
            OutputLog += $"[cloud] error {ex.Message}, falling back to local\n";
            return null;
        }
    }

    // ---------- Phase 10.2 Ghost ----------
    private readonly System.Collections.Generic.List<CompleteService.Suggestion> _ghosts = new();
    private int _ghostIdx;
    private System.Threading.Timer? _ghostTimer;

    private void ScheduleGhost()
    {
        if (!CompleteEnabled || ActiveTab == null) return;
        _ghostTimer?.Dispose();
        _ghostTimer = new System.Threading.Timer(_ =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
            {
                await RefreshGhostAsync();
            });
        }, null, 600, System.Threading.Timeout.Infinite);
    }

    private async System.Threading.Tasks.Task RefreshGhostAsync()
    {
        if (!CompleteEnabled || ActiveTab == null || string.IsNullOrEmpty(EditorText)) return;
        try
        {
            using var svc = new CompleteService(_baseUrl);
            var hints = new System.Collections.Generic.List<string>();
            foreach (var t in Tabs)
            {
                if (t.FilePath != ActiveTab.FilePath && t.Content.Length > 0)
                    hints.Add(t.Content.Length > 2000 ? t.Content[..2000] : t.Content);
                if (hints.Count >= 3) break;
            }
            var list = await svc.FetchAsync(EditorText, "", ActiveTab.FilePath, hints);
            _ghosts.Clear();
            _ghosts.AddRange(list);
            _ghostIdx = 0;
            if (_ghosts.Count > 0)
            {
                GhostText = _ghosts[0].Text;
                GhostWhy = FriendlyHint(_ghosts[0].Why);
                GhostVisible = true;
            }
            else GhostVisible = false;
        }
        catch { GhostVisible = false; }
    }

    [RelayCommand]
    private void AcceptGhost()
    {
        if (!GhostVisible || ActiveTab == null) return;
        ActiveTab.Content += GhostText;
        EditorText = ActiveTab.Content;
        ActiveTab.IsDirty = true;
        GhostVisible = false;
        OutputLog += $"[complete] accepted {GhostText.Length} chars\n";
    }

    [RelayCommand]
    private void AcceptWord()
    {
        if (!GhostVisible || ActiveTab == null) return;
        var word = "";
        foreach (var c in GhostText)
        {
            word += c;
            if (char.IsWhiteSpace(c) && word.Trim().Length > 0) break;
            if (word.Length > 40) break;
        }
        ActiveTab.Content += word;
        EditorText = ActiveTab.Content;
        ActiveTab.IsDirty = true;
        GhostText = GhostText.Length > word.Length ? GhostText[word.Length..] : "";
        if (string.IsNullOrEmpty(GhostText)) GhostVisible = false;
    }

    [RelayCommand]
    private void DismissGhost()
    {
        GhostVisible = false;
    }

    [RelayCommand]
    private void NextGhost()
    {
        if (_ghosts.Count < 2) return;
        _ghostIdx = (_ghostIdx + 1) % _ghosts.Count;
        GhostText = _ghosts[_ghostIdx].Text;
        GhostWhy = FriendlyHint(_ghosts[_ghostIdx].Why);
    }

    private static string FriendlyHint(string why)
    {
        return why switch
        {
            "brace balance" => "Auto close",
            "rag hint" => "From your code",
            "pattern" => "Common pattern",
            "suffix" => "Completion",
            _ => "Suggestion",
        };
    }

    [RelayCommand]
    private void ToggleComplete()
    {
        CompleteEnabled = !CompleteEnabled;
        if (!CompleteEnabled) GhostVisible = false;
        OutputLog += $"[complete] {(CompleteEnabled ? "on" : "off")}\n";
    }

    // ---------- Phase 10.3 Selection actions ----------
    [RelayCommand]
    private void ExplainSelection()
    {
        if (string.IsNullOrWhiteSpace(SelectionText)) { PushToast("No selection", "Select code first.", ""); return; }
        InputText = $"/explain {SelectionText}";
        _ = SendCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private void FixSelection()
    {
        if (string.IsNullOrWhiteSpace(SelectionText)) { PushToast("No selection", "Select code first.", ""); return; }
        InputText = $"/fix {SelectionText}";
        _ = SendCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private void AddSelectionToChat()
    {
        if (string.IsNullOrWhiteSpace(SelectionText)) return;
        Chips.Add(new ContextChip { Kind = "selection", Label = SelectionText.Length > 40 ? SelectionText[..40] : SelectionText, Path = ActiveTab?.FilePath ?? "", Tokens = SelectionText.Length / 4 });
        UpdateTokens();
        PushToast("Attached", "Selection added as context.", "");
    }
}
