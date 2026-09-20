using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Gitland.Core;
using static Gitland.App.Palette;

namespace Gitland.App;

public sealed partial class MainWindow : Window {
    GitRepository? _repo;
    readonly WorkspaceFixture? _fixture;
    RepositoryState _state = new("", "", [], [], []);
    string _mode = "changes", _filter = "all";
    string _fileScope = "changed";
    GitChange? _selected;
    IReadOnlyList<GitChange> _compareChanges = [];
    FileComparison? _comparison;
    DiffResult? _diff;
    bool _unified, _fold, _whitespace, _busy, _reviewStaged;
    int _loadId;
    readonly StackPanel _fileList = new() { Spacing = 3 };
    readonly StackPanel _navigation = new() { Spacing = 4 };
    readonly TextBox _fileSearch = new() { Watermark = "Filter files…", MinWidth = 100 };
    readonly TextBox _codeSearch = new() { Watermark = "Find in diff  ·  Ctrl+F", Width = 210 };
    readonly TextBlock _repoName = Text("gitland", 15, strong: true), _branch = Text("No repository", 11, Accent);
    readonly TextBlock _status = Text("Open, clone, or create a repository to get started", 12, Muted);
    readonly ContentControl _workspace = new();
    readonly Grid _contextBar = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto"), RowSpacing = 0 };
    StackPanel? _pageHeading;
    WrapPanel? _pageControls;
    StackPanel? _revisionControls;
    readonly StackPanel _hunkActions = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    readonly TextBlock _fileCount = Text("5", 12, Faint), _searchCount = Text("", 12, Faint);
    readonly DiffCanvas _canvas = new();
    readonly ChangeMap _map = new();
    readonly ScrollViewer _scroll;
    readonly TextBlock _leftLabel = Text("INDEX", 11, Muted, true), _rightLabel = Text("WORKING TREE", 11, Muted, true);
    readonly TextBlock _fileTitle = Text("diff-service.ts", 14, strong: true), _addedStats = Text("", 12, Green), _removedStats = Text("", 12, Red);
    readonly Button _resolveFileButton;
    readonly TextBox _leftRef = new() { Text = "HEAD~1", Width = 170, Watermark = "Base revision" }, _rightRef = new() { Text = "HEAD", Width = 170, Watermark = "Target revision" };
    readonly CheckBox _foldCheck = new() { Content = "Changes only", FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    readonly CheckBox _spaceCheck = new() { Content = "Ignore whitespace", FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    readonly Button _stageButton;
    readonly Button _discardButton;
    readonly Button _splitButton, _unifiedButton;
    readonly Grid _diffRoot;
    string? _externalLeft, _externalRight;
    string? _compareLeft, _compareRight;
    string _compareLeftLabel = "Base", _compareRightLabel = "Target";

    public MainWindow(string? path = null, WorkspaceFixture? fixture = null) {
        _fixture = fixture; if (fixture != null) _state = fixture.State;
        Title = "Gitland — Diff & Merge"; Width = 1440; Height = 920; MinWidth = 980; MinHeight = 640;
        Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://gitland/Assets/gitland.ico")));
        Background = Ground; Foreground = Ink; FontFamily = Sans;
        UseLayoutRounding = true;
        RenderOptions.SetTextRenderingMode(this, TextRenderingMode.Antialias);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        TransparencyLevelHint = [WindowTransparencyLevel.Mica, WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.None];
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome;
        ExtendClientAreaTitleBarHeightHint = 48;
        _scroll = new ScrollViewer { Content = _canvas, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _scroll.ScrollChanged += (_, _) => UpdateViewport();
        _scroll.SizeChanged += (_, _) => UpdateViewport();
        _map.Navigate += fraction => _scroll.Offset = new Vector(0, Math.Max(0, fraction * _canvas.Height - _scroll.Viewport.Height / 2));
        _canvas.ExpandRequested += () => { _foldCheck.IsChecked = false; };
        _fileSearch.TextChanged += (_, _) => RenderFiles();
        _codeSearch.TextChanged += (_, _) => { if (!string.IsNullOrEmpty(_codeSearch.Text)) _foldCheck.IsChecked = false; _canvas.Search = _codeSearch.Text ?? ""; _canvas.InvalidateVisual(); UpdateSearchCount(); };
        _codeSearch.KeyDown += (_, e) => { if (e.Key == Key.Enter) { FindNext(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1); e.Handled = true; } };
        _foldCheck.IsCheckedChanged += (_, _) => { _fold = _foldCheck.IsChecked == true; RenderDiff(); };
        _spaceCheck.IsCheckedChanged += (_, _) => { _whitespace = _spaceCheck.IsChecked == true; Run(RecalculateDiff); };
        _stageButton = Button("Stage file", () => Run(StageSelected), "check", true);
        _discardButton = Button("Discard file", () => Run(DiscardSelected), "trash");
        _resolveFileButton = Button("Open merge editor", () => Run(OpenSelectedMerge), "merge", true); _resolveFileButton.IsVisible = false;
        var titleBar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(18, 10) };
        _fileTitle.FontSize = 12; _fileTitle.FontWeight = FontWeight.Normal;
        _addedStats.Name = "DiffAdditions"; _removedStats.Name = "DiffDeletions";
        ToolTip.SetTip(_addedStats, "Added lines"); ToolTip.SetTip(_removedStats, "Deleted lines");
        titleBar.Children.Add(Row(Icon("file", Faint, 14), _fileTitle, _addedStats, _removedStats));
        var fileActions = Row(IconButton("Copy right-hand source", () => Run(CopyRight), "arrow-out"), _discardButton, _stageButton, _resolveFileButton);
        Grid.SetColumn(fileActions, 1); titleBar.Children.Add(fileActions);
        var toolbar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto"), Margin = new Thickness(16, 0, 16, 10) };
        _splitButton = Button("Side by side", () => SetUnified(false)); _splitButton.Classes.Add("primary");
        _unifiedButton = Button("Unified", () => SetUnified(true));
        toolbar.Children.Add(Row(Segmented(_splitButton, _unifiedButton), _foldCheck, _spaceCheck));
        var find = Row(_codeSearch, _searchCount, IconButton("Previous match · Shift+Enter", () => FindNext(-1), "up"), IconButton("Next match · Enter", () => FindNext(1), "down"));
        Grid.SetColumn(find, 1); toolbar.Children.Add(find);
        void DiffDensity() {
            bool compact = _workspace.Bounds.Width < 1030;
            Grid.SetRow(find, compact ? 1 : 0); Grid.SetColumn(find, compact ? 0 : 1); Grid.SetColumnSpan(find, compact ? 2 : 1);
            find.Margin = new Thickness(0, compact ? 8 : 0, 0, 0);
            _fileTitle.MaxWidth = Math.Max(110, _workspace.Bounds.Width - 420);
            UpdatePageDensity();
        }
        SizeChanged += (_, _) => DiffDensity();
        _workspace.SizeChanged += (_, _) => DiffDensity();
        var labels = new Grid { ColumnDefinitions = new ColumnDefinitions("*,22,*"), Height = 32, Background = Raised };
        _leftLabel.Margin = new Thickness(18, 0); _rightLabel.Margin = new Thickness(18, 0);
        labels.Children.Add(_leftLabel); Grid.SetColumn(_rightLabel, 2); labels.Children.Add(_rightLabel);
        var diffGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,22") };
        diffGrid.Children.Add(_scroll); Grid.SetColumn(_map, 1); diffGrid.Children.Add(_map);
        _diffRoot = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto,Auto"), Background = Ground };
        Add(_diffRoot, titleBar, 0); Add(_diffRoot, toolbar, 1); Add(_diffRoot, labels, 2); Add(_diffRoot, diffGrid, 3);
        var horizontal = new ScrollBar { Orientation = Orientation.Horizontal, Maximum = 2000, Height = 13, ViewportSize = 600 };
        horizontal.ValueChanged += (_, _) => { _canvas.HorizontalOffset = horizontal.Value; _canvas.InvalidateVisual(); };
        Add(_diffRoot, horizontal, 4);
        var bottom = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(16, 8) };
        bottom.Children.Add(new ScrollViewer { Content = _hunkActions, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, MaxHeight = 76 });
        var arrows = Row(IconButton("Previous change · Alt+Up", () => Jump(-1), "up"), IconButton("Next change · Alt+Down", () => Jump(1), "down")); Grid.SetColumn(arrows, 1); bottom.Children.Add(arrows); Add(_diffRoot, bottom, 5);
        Content = BuildShell();
        ApplyPreferences(true);
        PropertyChanged += (_, e) => { if (e.Property == OffScreenMarginProperty || e.Property == WindowStateProperty) UpdateWindowInsets(); };
        UpdateWindowInsets();
        InitializeHoswl();
        KeyDown += HandleKey;
        Opened += (_, _) => { if (!string.IsNullOrWhiteSpace(path)) Run(() => OpenRepository(path)); else if (_fixture != null) Run(() => SelectFile(_state.Changes[0])); else ShowWelcome(); };
        RenderNavigation(); RenderFiles(); RenderContext();
        Closing += (_, e) => {
            if (!_mergeDirty) return;
            e.Cancel = true;
            Run(async () => { if (await MayLeaveMerge()) { _mergeDirty = false; Close(); } });
        };
    }

    static void Add(Grid grid, Control child, int row, int column = 0) { Grid.SetRow(child, row); Grid.SetColumn(child, column); grid.Children.Add(child); }
    IEnumerable<GitChange> VisibleFiles() {
        IEnumerable<GitChange> files = _mode == "threeway" ? _threeRevisions?.Files ?? [] : _mode == "files" ? [] : _mode == "compare" ? _compareChanges : _fileScope == "all" ? _state.AllFiles : _fileScope == "conflicts" ? _state.Changes.Where(c => c.IsConflict) : _state.Changes;
        if (_mode is "changes" or "merge" && _fileScope == "changed" && _filter == "staged") files = files.Where(c => c.IsStaged && !c.IsConflict);
        if (_mode is "changes" or "merge" && _fileScope == "changed" && _filter == "unstaged") files = files.Where(c => c.IsUnstaged && !c.IsConflict);
        var filter = _fileSearch.Text ?? ""; return files.Where(c => c.Path.Contains(filter, StringComparison.OrdinalIgnoreCase));
    }
    async Task SetMode(string mode) {
        if (!await MayLeaveMerge()) return;
        if (mode == "merge") { _fileScope = "conflicts"; _filter = "all"; }
        if (mode == "changes") _fileScope = "changed";
        _mode = mode; _selected = null; _externalLeft = null; RenderNavigation(); RenderContext(); RenderFiles();
        if (_repo == null && _fixture == null && mode != "threeway") { ShowWelcome(); return; }
        if (mode is "repository" or "github") { await LoadManagement(); return; }
        if (mode == "threeway") { await EnterThreeWay(); return; }
        if (mode == "compare") { Empty("Compare any two revisions", "Use branch names, tags, or commit hashes. The comparison does not change your checkout."); if (_repo == null) await LoadComparison(); }
        else if (VisibleFiles().FirstOrDefault() is { } file) await SelectFile(file);
        else Empty(mode == "merge" ? "No conflicts to resolve" : "Your working tree is clean", mode == "merge" ? "Conflicted files appear here when a Git merge or rebase needs your input." : "Make a change in your editor, then refresh this workspace.");
    }
    async Task SelectFile(GitChange file, bool? stagedView = null) {
        if (_repo == null && _fixture == null) { ShowWelcome(); return; }
        if (_selected?.Path == file.Path && _mergeDirty) return;
        if (_selected?.Path != file.Path && !await MayLeaveMerge()) return;
        if (_mode is "repository" or "github" || (_mode == "merge" && !file.IsConflict)) { _mode = "changes"; RenderNavigation(); RenderContext(); }
        bool staged = !file.IsConflict && (stagedView ?? (_filter == "staged" || (!file.IsUnstaged && file.IsStaged)));
        _reviewStaged = staged;
        int id = ++_loadId; _selected = file; _externalLeft = null; RenderFiles();
        _comparison = null; _diff = null; _stageButton.IsEnabled = false;
        Empty("Loading file…", file.Path);
        _resolveFileButton.IsVisible = false;
        if (_mode == "threeway") { await SelectThreeWay(file); return; }
        if (file.IsConflict && _mode == "merge") { await LoadMerge(file); return; }
        _merge = null; _mergeDirty = false;
        if (file.IsConflict) {
            _comparison = _repo == null ? new(file.Path, _fixture!.Merge().Ours, _fixture!.Merge().Snapshot.Text, "Ours · current branch", "Working tree · unresolved") : await _repo.ConflictDiffAsync(file.Path);
            _stageButton.IsVisible = false; _resolveFileButton.IsVisible = true;
            await RecalculateDiff(); _status.Text = "Unresolved conflict · Review the changes here, or open the merge editor to resolve it."; return;
        }

        FileComparison comparison = _repo == null ? !file.IsChanged ? new FileComparison(file.Path, "// " + file.Path + "\n// No changes in this file.\n", "// " + file.Path + "\n// No changes in this file.\n", "Index", "Working tree") : _fixture!.Compare(file.Path, staged) : _mode == "compare" ? await _repo.CompareFileAsync(file, _compareLeft!, _compareRight!) : await _repo.WorkingDiffAsync(file, staged);
        if (id != _loadId) return;
        if (_mode == "compare") comparison = comparison with { LeftLabel = _compareLeftLabel, RightLabel = _compareRightLabel };
        _comparison = comparison; _stageButton.IsVisible = _mode == "changes";
        _stageButton.Content = Row(Icon(staged ? "close" : "check"), Text(!file.IsChanged ? "Unchanged" : _repo == null ? "Read only" : staged ? "Unstage file" : "Stage file")); _stageButton.IsEnabled = _repo != null && file.IsChanged;
        Avalonia.Automation.AutomationProperties.SetName(_stageButton, staged ? "Unstage file" : "Stage file");
        // Discarding is offered wherever staging is, so the way out of a change sits beside the way in.
        _discardButton.IsVisible = _mode == "changes";
        _discardButton.Content = Row(Icon("trash"), Text(file.Index == '?' ? "Delete file" : "Discard file"));
        _discardButton.IsEnabled = _repo != null && file.IsChanged;
        ToolTip.SetTip(_discardButton, file.Index == '?' ? "Delete this untracked file, keeping a copy under Recovery" : "Throw away this file's changes, keeping a snapshot under Recovery");
        Avalonia.Automation.AutomationProperties.SetName(_discardButton, file.Index == '?' ? "Delete file" : "Discard file");
        await RecalculateDiff();
        _status.Text = _repo == null ? "Open, clone, or create a repository to get started" : file.Path + " · " + (staged ? "Staged changes" : "Working tree changes");
    }
    async Task RecalculateDiff() {
        if (_comparison == null) return;
        if (_comparison.Binary) { Empty("Binary file", "This file cannot be displayed as a text diff. Use Git to inspect its binary changes."); return; }
        var comparison = _comparison; int generation = _loadId; bool whitespace = _whitespace;
        var result = await Task.Run(() => DiffEngine.Compare(comparison.Left, comparison.Right, whitespace));
        if (generation != _loadId) return;
        _diff = result;
        _fileTitle.Text = comparison.Path.Replace('\\', '/');
        _leftLabel.Text = _unified ? "UNIFIED · " + comparison.LeftLabel.ToUpperInvariant() + " → " + comparison.RightLabel.ToUpperInvariant() : comparison.LeftLabel.ToUpperInvariant(); _rightLabel.Text = comparison.RightLabel.ToUpperInvariant();
        _addedStats.Text = $"+{_diff.Added}"; _removedStats.Text = $"−{_diff.Removed}";
        _workspace.Content = _diffRoot; RenderDiff(); RenderHunks();
        _scroll.Offset = new Vector(0, 0); Jump(1);
    }
    void RenderDiff() {
        if (_diff == null) return;
        _canvas.SetRows(_fold ? DiffEngine.Fold(_diff.Rows) : _diff.Rows, _unified); _map.Rows = _canvas.Rows; _canvas.ActiveRow = -1; UpdateViewport(); UpdateSearchCount();
    }
    void SetUnified(bool value) {
        _unified = value; _splitButton.Classes.Set("primary", !value); _unifiedButton.Classes.Set("primary", value);
        RenderDiff(); Jump(1); _leftLabel.Text = value ? "UNIFIED · " + _comparison?.LeftLabel.ToUpperInvariant() + " → " + _comparison?.RightLabel.ToUpperInvariant() : _comparison?.LeftLabel.ToUpperInvariant(); _rightLabel.IsVisible = !value;
    }
    void RenderHunks() {
        _hunkActions.Children.Clear();
        if (_comparison == null) return;
        var hunks = GitRepository.ParseHunks(_comparison.Patch);
        bool stageable = _repo != null && _mode == "changes" && _comparison.RightLabel == "Working tree";
        if (hunks.Count == 0 || !stageable) {
            bool eofDiff = _comparison.Left.EndsWith('\n') != _comparison.Right.EndsWith('\n');
            string endings(string s) => s.Contains("\r\n") ? "CRLF" : "LF";
            string metadata = endings(_comparison.Left) != endings(_comparison.Right) ? $" · {endings(_comparison.Left)} → {endings(_comparison.Right)} line endings" : "";
            _hunkActions.Children.Add(Text((eofDiff ? "Final newline differs" : $"{_diff?.ChangeStarts.Count ?? 0} change regions · Drag across lines to select, Ctrl+C to copy") + metadata, 12, Faint)); return;
        }
        foreach (var hunk in hunks) {
            var jump = Button($"Hunk {hunk.Index + 1}", () => GoToLine(hunk.RightLine));
            bool stagedView = _comparison!.RightLabel == "Index · staged";
            var stage = stagedView
                ? Button("Unstage", () => Run(async () => { await _repo!.UnstageHunkAsync(_selected!.Path, _comparison!.Patch, hunk.Index); await Refresh(); _status.Text = $"Hunk {hunk.Index + 1} unstaged."; }), "close")
                : Button("Stage", () => Run(async () => { await _repo!.StageHunkAsync(_selected!.Path, _comparison!.Patch, hunk.Index); await Refresh(); _status.Text = $"Hunk {hunk.Index + 1} staged."; }), "check");
            var actions = Row(jump, stage);
            if (!stagedView) actions.Children.Add(Button("Discard", () => Run(() => DiscardHunk(hunk.Index)), "trash"));
            _hunkActions.Children.Add(actions);
        }
    }
    void UpdateViewport() {
        _canvas.ScrollTop = _scroll.Offset.Y; _canvas.ViewHeight = _scroll.Viewport.Height; _canvas.InvalidateVisual();
        _map.ViewStart = _scroll.Offset.Y / Math.Max(1, _canvas.Height); _map.ViewEnd = Math.Min(1, (_scroll.Offset.Y + _scroll.Viewport.Height) / Math.Max(1, _canvas.Height)); _map.InvalidateVisual();
    }
    void Jump(int direction) {
        var rows = _canvas.Rows; var changes = rows.Select((r, i) => (r, i)).Where(x => x.r.Kind is not (ChangeKind.Equal or ChangeKind.Fold) && (x.i == 0 || rows[x.i - 1].Kind is ChangeKind.Equal or ChangeKind.Fold)).Select(x => x.i).ToArray();
        if (changes.Length == 0) return;
        int current = _canvas.ActiveRow;
        int target = direction > 0 ? changes.FirstOrDefault(i => i > current, changes[0]) : changes.LastOrDefault(i => i < current, changes[^1]); ScrollTo(target);
    }
    void GoToLine(int line) { int i = _canvas.Rows.ToList().FindIndex(r => r.RightNumber >= line); if (i >= 0) ScrollTo(i); }
    void ScrollTo(int row) { _canvas.ActiveRow = row; _scroll.Offset = new Vector(0, Math.Max(0, row * DiffCanvas.LineHeight - 3 * DiffCanvas.LineHeight)); _canvas.InvalidateVisual(); }
    int[] Matches() => string.IsNullOrEmpty(_codeSearch.Text) ? [] : _canvas.Rows.Select((r, i) => (r, i)).Where(x => (x.r.Left ?? "").Contains(_codeSearch.Text, StringComparison.OrdinalIgnoreCase) || (x.r.Right ?? "").Contains(_codeSearch.Text, StringComparison.OrdinalIgnoreCase)).Select(x => x.i).ToArray();
    void UpdateSearchCount() => _searchCount.Text = string.IsNullOrEmpty(_codeSearch.Text) ? "" : $"{Matches().Length}";
    void FindNext(int direction) { var matches = Matches(); if (matches.Length == 0) return; ScrollTo(direction > 0 ? matches.FirstOrDefault(i => i > _canvas.ActiveRow, matches[0]) : matches.LastOrDefault(i => i < _canvas.ActiveRow, matches[^1])); }
    async Task CopyRight() { if (Clipboard != null && _comparison != null) { await Clipboard.SetTextAsync(_comparison.Right); _status.Text = "Right-hand source copied."; } }
    async Task StageSelected() {
        if (_repo == null || _selected == null) return;
        if (_comparison?.RightLabel == "Index · staged") await _repo.UnstageFileAsync(_selected.Path, _selected.OldPath); else await _repo.StageFileAsync(_selected.Path);
        await Refresh();
    }
    async Task DiscardSelected() {
        if (_repo == null || _selected is not { IsChanged: true } file) return;
        bool untracked = file.Index == '?';
        bool staged = _comparison?.RightLabel == "Index · staged";
        string detail = untracked
            ? $"Delete {file.Path}? It is not tracked by Git, so Gitland copies it into the repository's recovery folder first."
            : staged
                ? $"Throw away every change to {file.Path}, staged and unstaged, returning it to the last commit? Gitland saves a snapshot under Recovery first."
                : $"Throw away the unstaged changes to {file.Path}? Anything already staged is kept, and Gitland saves a snapshot under Recovery first.";
        var (confirmed, permanent) = await ReviewActionWithOption(
            untracked ? "Delete this file?" : "Discard these changes?", detail,
            untracked ? "Delete file" : "Discard changes",
            untracked ? "Delete permanently" : "Discard permanently",
            untracked ? "Delete permanently - keep no copy" : "Discard permanently - keep no recovery snapshot",
            untracked ? "Nothing is copied anywhere. This file cannot be brought back." : "No snapshot is taken. These changes cannot be brought back.");
        if (!confirmed) return;
        var result = untracked ? await _repo.CleanUntrackedAsync([file.Path], !permanent)
            : staged ? await _repo.DiscardFileAsync([file.Path], !permanent)
            : await _repo.DiscardUnstagedAsync([file.Path], !permanent);
        await Refresh();
        _status.Text = permanent
            ? $"{file.Path} removed permanently · nothing was kept"
            : untracked ? $"{file.Path} deleted · a copy is in {result.BackupDirectory}" : $"{file.Path} discarded · recoverable from Repository → Recovery";
    }
    async Task DiscardHunk(int index) {
        if (_repo == null || _selected == null || _comparison == null) return;
        var (confirmed, permanent) = await ReviewActionWithOption(
            "Discard this hunk?", $"Throw away hunk {index + 1} of {_selected.Path}? The rest of the file keeps its changes, and Gitland saves a snapshot under Recovery first.",
            "Discard hunk", "Discard permanently",
            "Discard permanently - keep no recovery snapshot",
            "No snapshot is taken. This hunk cannot be brought back.");
        if (!confirmed) return;
        await _repo.DiscardHunkAsync(_selected.Path, _comparison.Patch, index, !permanent);
        await Refresh();
        _status.Text = permanent ? $"Hunk {index + 1} removed permanently · nothing was kept" : $"Hunk {index + 1} discarded · recoverable from Repository → Recovery";
    }
    async Task DiscardEverything(bool includeUntracked) {
        if (_repo == null) return;
        var (confirmed, permanent) = await ReviewActionWithOption("Discard all changes?", includeUntracked
            ? "Return every tracked file to the last commit and delete untracked files? Gitland saves a snapshot under Recovery and copies untracked files into the recovery folder first."
            : "Return every tracked file to the last commit? Untracked files are left alone, and Gitland saves a snapshot under Recovery first.",
            "Discard all", "Discard all permanently",
            "Discard permanently - keep no recovery snapshot or copies",
            "Nothing is saved anywhere. Every change being discarded here is gone for good.");
        if (!confirmed) return;
        var result = await _repo.DiscardEverythingAsync(includeUntracked, !permanent);
        await Refresh();
        _status.Text = permanent
            ? $"{result.Files} file{(result.Files == 1 ? "" : "s")} removed permanently · nothing was kept"
            : $"{result.Files} file{(result.Files == 1 ? "" : "s")} discarded · recoverable from Repository → Recovery";
    }
    async Task BlameSelected() {
        if (_repo == null || _selected == null) return;
        var lines = await _repo.BlameAsync(_selected.Path);
        if (lines.Count == 0) { _status.Text = "This file has no committed history to blame."; return; }
        await ShowListDialog("Blame · " + _selected.Path,
            lines.Select(l => $"{l.ShortHash}  {l.Date}  {l.Author,-16}  {l.Number,5}  {l.Text}").ToArray());
    }
    async Task FileHistorySelected() {
        if (_repo == null || _selected == null) return;
        var commits = await _repo.ReadHistoryAsync(new(Path: _selected.Path));
        if (commits.Count == 0) { _status.Text = "No commits have touched this file yet."; return; }
        await ShowListDialog("History · " + _selected.Path,
            commits.Select(c => $"{c.ShortHash}  {c.Date[..Math.Min(10, c.Date.Length)]}  {c.Author,-16}  {c.Subject}").ToArray());
    }
    async Task OpenSelectedMerge() {
        if (_selected is not { IsConflict: true } file || !await MayLeaveMerge()) return;
        _mode = "merge"; RenderNavigation(); RenderContext(); RenderFiles(); await LoadMerge(file);
    }
    async Task PickRepository() {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Open a Git repository", AllowMultiple = false });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path) await OpenRepository(path);
    }
    public async Task OpenRepository(string path) {
        if (!await MayLeaveMerge()) return;
        var repo = await GitRepository.OpenAsync(path); var state = await repo.ReadStateAsync();
        var management = await repo.ReadManagementAsync();
        _threeRevisions = null; _threeFile = null; _threeLocalPaths = null; _tools = null; _repo = repo; _state = state; _management = management; RestoreCommitDraft(_commitDrafts.GetValueOrDefault(repo.Root, "")); _mode = "changes"; _filter = "all"; _fileScope = "changed"; _selected = null; _merge = null; _mergeDirty = false;
        _repoName.Text = System.IO.Path.GetFileName(repo.Root); _branch.Text = state.Branch; RenderNavigation(); RenderContext(); RenderFiles();
        _leftRef.Text = state.Refs.Contains("main") ? "main" : "HEAD~1"; _rightRef.Text = "HEAD";
        if (VisibleFiles().FirstOrDefault() is { } file) await SelectFile(file); else Empty("Your working tree is clean", "Edit a file to see changes here, or compare two revisions.");
        WatchRepository(repo.Root);
        RememberRepository(repo.Root);
        _status.Text = repo.Root;
    }
    async Task Refresh() {
        if (!await MayLeaveMerge()) return;
        if (_mode == "threeway" && _threeLocalPaths != null) { await ShowThreeLocalFiles(_threeLocalPaths); return; }
        if (_mode == "files" && _externalLeft != null && _externalRight != null) { await ShowLocalFiles(_externalLeft, _externalRight); return; }
        if (_repo == null) { _status.Text = "Open a repository to refresh its changes."; return; }
        _state = await _repo.ReadStateAsync(); _management = await _repo.ReadManagementAsync();
        if (_commitFailed) { _commitFailed = false; _commitFeedback = ""; }
        _branch.Text = _state.Branch; RenderNavigation();
        if (_mode is "repository" or "github") { await LoadManagement(); RenderFiles(); return; }
        if (_mode == "compare") { await LoadComparison(); return; }
        if (_mode == "threeway") { await EnterThreeWay(); return; }
        _merge = null; _mergeDirty = false; RenderFiles(); RenderContext();
        var next = VisibleFiles().FirstOrDefault(c => c.Path == _selected?.Path) ?? VisibleFiles().FirstOrDefault();
        if (next != null) await SelectFile(next, next.IsStaged && (_reviewStaged || !next.IsUnstaged)); else { _selected = null; Empty("All clear", "There are no changes in this view."); }
    }
    async Task LoadComparison() {
        string leftLabel = _leftRef.Text ?? "", rightLabel = _rightRef.Text ?? "";
        string left = _repo == null ? leftLabel : await _repo.ResolveRef(leftLabel);
        string right = _repo == null ? rightLabel : await _repo.ResolveRef(rightLabel);
        var changes = _repo == null ? _state.Changes.Where(c => !c.IsConflict).ToArray() : await _repo.CompareChangesAsync(left, right);
        _compareLeft = left; _compareRight = right; _compareLeftLabel = leftLabel; _compareRightLabel = rightLabel; _compareChanges = changes;
        RenderFiles(); if (_compareChanges.FirstOrDefault() is { } file) await SelectFile(file); else Empty("These revisions match", "There are no file changes between the selected revisions.");
    }
    async Task CompareLocalFiles() {
        if (!await MayLeaveMerge()) return;
        var left = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Choose the original file", AllowMultiple = false });
        if (left.Count == 0) return;
        var right = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Choose the modified file", AllowMultiple = false });
        if (right.Count == 0) return;
        var a = left[0].TryGetLocalPath(); var b = right[0].TryGetLocalPath(); if (a == null || b == null) return;
        await ShowLocalFiles(a, b);
    }
    async Task ShowLocalFiles(string a, string b) {
        var one = await new GitRepository(System.IO.Path.GetDirectoryName(a)!).ReadWorkingFile(System.IO.Path.GetFileName(a));
        var two = await new GitRepository(System.IO.Path.GetDirectoryName(b)!).ReadWorkingFile(System.IO.Path.GetFileName(b));
        _externalLeft = a; _externalRight = b; _merge = null; _mergeDirty = false; _selected = null; _mode = "files";
        RenderNavigation(); RenderContext(); RenderFiles();
        _comparison = new(System.IO.Path.GetFileName(a) + " ↔ " + System.IO.Path.GetFileName(b), one.Text, two.Text, a, b, Binary: one.Text.Contains('\0') || two.Text.Contains('\0'));
        _stageButton.IsVisible = false; _resolveFileButton.IsVisible = false; await RecalculateDiff(); _status.Text = "Local file comparison · Read only";
    }
    void Empty(string title, string detail) {
        var content = Col(Icon("check", Accent, 40), Text(title, 23, strong: true), new TextBlock { Text = detail, Foreground = Muted, FontSize = 14, MaxWidth = 470, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center });
        content.Spacing = 16; content.HorizontalAlignment = HorizontalAlignment.Center; content.VerticalAlignment = VerticalAlignment.Center;
        foreach (var c in content.Children) c.HorizontalAlignment = HorizontalAlignment.Center;
        _workspace.Content = content;
    }
    void Run(Func<Task> action) {
        if (_busy) return;
        _ = RunCore(action);
    }
    public bool IsWorking => _busy;
    async Task RunCore(Func<Task> action) {
        _busy = true; UpdateMenus(); _status.Text = "Working…";
        try { await action(); }
        catch (Exception e) { _status.Text = e.Message; await ShowMessage("Could not complete the action", e.Message); }
        finally { _busy = false; UpdateMenus(); if (_status.Text == "Working…") _status.Text = _repo?.Root ?? "Open, clone, or create a repository to get started"; }
    }
    void HandleKey(object? sender, KeyEventArgs e) {
        if (e.Key == Key.OemComma && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { _ = ShowSettings(); e.Handled = true; }
        else if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control) && _mode == "changes" && CanCommit && OwnedWindows.Count == 0) { Run(CommitCurrent); e.Handled = true; }
        else if (e.Key == Key.F11) { ToggleFullscreen(); e.Handled = true; }
        else if (e.Key == Key.Escape && WindowState == WindowState.FullScreen) { ToggleFullscreen(); e.Handled = true; }
        else if (e.Key == Key.O && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { Run(PickRepository); e.Handled = true; }
        else if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { var search = _mode == "threeway" ? _threeSearch : _codeSearch; search?.Focus(); search?.SelectAll(); e.Handled = true; }
        else if (e.Key == Key.F5) { Run(Refresh); e.Handled = true; }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Alt) && e.Key is Key.Up or Key.Down) { if (_mode == "threeway") _threeNavigate?.Invoke(e.Key == Key.Down ? 1 : -1); else Jump(e.Key == Key.Down ? 1 : -1); e.Handled = true; }
    }
    async Task ShowMessage(string title, string message) { var dialog = MakeDialog(title, message); var button = Button("Close", () => dialog.Close(), primary: true); ((StackPanel)dialog.Content!).Children.Add(button); await dialog.ShowDialog(this); }
    Window MakeDialog(string title, string message) => new() { Title = title, Width = 540, SizeToContent = SizeToContent.Height, CanResize = false, Background = Raised, Foreground = Ink, FontFamily = Sans, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = new StackPanel { Margin = new Thickness(24), Spacing = 18, Children = { Text(title, 20, strong: true), new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = Muted, FontSize = 14 } } } };
}
