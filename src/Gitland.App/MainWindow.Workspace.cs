using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Gitland.Core;
using static Gitland.App.Palette;

namespace Gitland.App;

public sealed partial class MainWindow {
    IReadOnlyList<RepoSummary> _workspaceRows = [];
    string _workspaceShow = "all";
    int _workspaceScanId;
    bool _workspaceScanning;
    FileSystemWatcher? _workspaceWatcher;
    DispatcherTimer? _workspaceTimer;
    readonly HashSet<string> _workspaceTouched = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The folder whose repositories the Workspace view lists. Before one is chosen, the
    /// folder holding the open repository is the obvious guess, so the view opens with content.</summary>
    string? WorkspaceRoot => GitlandApplication.Preferences.WorkspaceRoot ?? (_repo == null ? null : System.IO.Path.GetDirectoryName(_repo.Root));

    /// <summary>Scans a folder and shows its repositories. Public so the offscreen validation host
    /// can reach the view without driving a native folder picker.</summary>
    public async Task OpenWorkspace(string root) {
        SavePreferences(GitlandApplication.Preferences with { WorkspaceRoot = root });
        await SetMode("workspace");
    }

    async Task EnterWorkspace() {
        RenderWorkspace();
        await ScanWorkspace();
        WatchWorkspace(WorkspaceRoot);
    }

    async Task ChooseWorkspaceFolder() {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose a folder that holds your repositories", AllowMultiple = false });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path) await OpenWorkspace(path);
    }

    /// <summary>Reads every repository in the folder. A scan started earlier is abandoned rather
    /// than allowed to overwrite a newer one, the same way file loads use a generation counter.</summary>
    async Task ScanWorkspace() {
        int id = ++_workspaceScanId;
        string? root = WorkspaceRoot;
        // Until the scan answers, the table would claim the folder is empty; say it is being read.
        _workspaceScanning = root != null;
        if (root != null && _mode == "workspace") { RenderWorkspace(); _status.Text = "Scanning " + root + "…"; }
        IReadOnlyList<RepoSummary> rows;
        try { rows = root == null ? [] : await WorkspaceScan.ScanAsync(root); }
        finally { if (id == _workspaceScanId) _workspaceScanning = false; }
        if (id != _workspaceScanId) return;
        _workspaceRows = rows;
        if (_mode == "workspace") { RenderWorkspace(); RenderContext(); }
        _status.Text = root == null ? "Choose a folder to see the repositories inside it." : $"{rows.Count} {(rows.Count == 1 ? "repository" : "repositories")} in {root}";
    }

    /// <summary>Re-reads only the repositories the watcher saw activity in. A repository appearing
    /// or disappearing changes the shape of the table, so that falls back to a full scan.</summary>
    async Task RescanTouched(IReadOnlyList<string> touched) {
        string? root = WorkspaceRoot;
        if (root == null) return;
        var known = _workspaceRows.Select(r => r.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var present = WorkspaceScan.FindRepositories(root);
        if (present.Count != _workspaceRows.Count || present.Any(path => !known.Contains(path))) { await ScanWorkspace(); return; }
        int id = _workspaceScanId;
        var rows = _workspaceRows.ToArray();
        foreach (string path in touched) {
            int index = Array.FindIndex(rows, r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));
            if (index < 0) continue;
            var fresh = await WorkspaceScan.ReadAsync(path);
            if (id != _workspaceScanId) return;
            rows[index] = fresh;
        }
        _workspaceRows = rows;
        if (_mode == "workspace") RenderWorkspace();
    }

    /// <summary>Watches the whole workspace folder while the view is open, so a commit made in an
    /// editor shows up in the table. Events are coalesced and mapped back to the repository they
    /// happened in, so one save re-reads one row rather than the whole folder. Passing null stops
    /// watching, which is what leaving the view does: the cost is only paid while it is on screen.</summary>
    void WatchWorkspace(string? root) {
        _workspaceWatcher?.Dispose(); _workspaceWatcher = null;
        _workspaceTimer?.Stop(); _workspaceTimer = null;
        lock (_workspaceTouched) _workspaceTouched.Clear();
        if (root == null || !Directory.Exists(root)) return;
        // The offscreen validation harness writes repository files itself and asserts on the table
        // it scanned; a background rescan would race those assertions.
        if (Environment.GetEnvironmentVariable("GITLAND_DISABLE_WATCH") == "1") return;

        _workspaceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _workspaceTimer.Tick += async (_, _) => {
            string[] touched;
            lock (_workspaceTouched) {
                if (_workspaceTouched.Count == 0) return;
                // Never rescan underneath a dialog, a running action, or an unsaved merge result.
                if (_busy || _mergeDirty || OwnedWindows.Count > 0 || _mode != "workspace") return;
                touched = [.. _workspaceTouched]; _workspaceTouched.Clear();
            }
            _workspaceTimer!.Stop();
            try { await RescanTouched(touched); } catch (Exception) { /* a half-written index recovers on the next tick */ }
            _workspaceTimer.Start();
        };

        try {
            _workspaceWatcher = new FileSystemWatcher(root) { IncludeSubdirectories = true, InternalBufferSize = 64 * 1024 };
            _workspaceWatcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size;
            void Changed(object? sender, FileSystemEventArgs e) {
                string relative = System.IO.Path.GetRelativePath(root, e.FullPath);
                if (relative.StartsWith("..", StringComparison.Ordinal) || System.IO.Path.IsPathRooted(relative)) return;
                int slash = relative.IndexOfAny(['/', '\\']);
                string child = slash < 0 ? relative : relative[..slash];
                if (child.Length == 0 || child == ".") return;
                string inside = slash < 0 ? "" : relative[(slash + 1)..].Replace('\\', '/');
                // .git churns constantly; only the refs and index that change what a row says count.
                if (inside.StartsWith(".git/", StringComparison.Ordinal)) {
                    bool interesting = inside is ".git/HEAD" or ".git/index" or ".git/MERGE_HEAD" || inside.StartsWith(".git/refs/", StringComparison.Ordinal);
                    if (!interesting) return;
                }
                lock (_workspaceTouched) _workspaceTouched.Add(System.IO.Path.Combine(root, child));
            }
            _workspaceWatcher.Changed += Changed; _workspaceWatcher.Created += Changed; _workspaceWatcher.Deleted += Changed;
            _workspaceWatcher.Renamed += (s, e) => Changed(s, e);
            // A dropped buffer means edits were missed, so rescan rather than show stale rows.
            _workspaceWatcher.Error += (_, _) => { lock (_workspaceTouched) foreach (var row in _workspaceRows) _workspaceTouched.Add(row.Path); };
            _workspaceWatcher.EnableRaisingEvents = true;
            _workspaceTimer.Start();
        } catch (Exception) {
            // A folder on a share or a path the platform will not watch simply stays manual.
            _workspaceWatcher?.Dispose(); _workspaceWatcher = null; _workspaceTimer.Stop(); _workspaceTimer = null;
        }
    }

    static bool Unclean(RepoSummary row) => row.IsDirty || row.Ahead > 0 || row.Behind > 0 || row.Error != null;

    void RenderWorkspace() {
        string? root = WorkspaceRoot;
        var page = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*") };

        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto"), ColumnSpacing = 18, Margin = new Thickness(20, 18, 20, 14) };
        string summaryLine = root == null ? "Choose a folder that holds your repositories."
            : _workspaceScanning ? "Scanning for repositories…"
            : $"{_workspaceRows.Count} {(_workspaceRows.Count == 1 ? "repository" : "repositories")} directly inside this folder";
        var title = Col(Text(root ?? "No folder chosen", 14, strong: true), Text(summaryLine, 11, Faint));
        title.Spacing = 6; heading.Children.Add(title);
        var choose = Button(root == null ? "Choose folder" : "Change folder", () => Run(ChooseWorkspaceFolder), "folder");
        var rescan = Button("Rescan", () => Run(ScanWorkspace), "refresh"); rescan.IsEnabled = root != null && !_workspaceScanning;
        var fetchAll = Button("Fetch all", () => Run(() => WorkspaceBulk("fetch")), "down");
        var pullAll = Button("Pull all", () => Run(() => WorkspaceBulk("pull")));
        var pushAll = Button("Push all", () => Run(() => WorkspaceBulk("push")), "up");
        foreach (var bulk in new[] { fetchAll, pullAll, pushAll }) bulk.IsEnabled = root != null && _workspaceRows.Count > 0 && !_workspaceScanning;
        var actions = WrapActions(choose, rescan, fetchAll, pullAll, pushAll);
        Add(heading, actions, 0, 1);
        Add(page, heading, 0);

        var search = new TextBox { Watermark = "Filter repositories by name", Name = "WorkspaceFilter", FontSize = 12 };
        var scopes = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto"), ColumnSpacing = 2 };
        var filters = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 14, Margin = new Thickness(20, 0, 20, 14) };
        filters.Children.Add(search); Add(filters, new Border { Child = scopes, Background = Ground, CornerRadius = new CornerRadius(7), Padding = new Thickness(3) }, 0, 1);
        Add(page, filters, 1);

        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions(RowColumns), ColumnSpacing = 14, Margin = new Thickness(20, 0, 20, 8) };
        foreach (var (index, label) in new[] { "REPOSITORY", "BRANCH", "CHANGES", "SYNC", "REMOTES" }.Index()) Add(columns, Text(label, 10, Faint, true), 0, index);
        Add(page, columns, 2);

        var list = new StackPanel();
        var rows = new List<(Control Row, RepoSummary Summary)>();
        var tables = new List<Grid> { columns };
        foreach (var summary in _workspaceRows) { var row = WorkspaceRow(summary, out var cells); rows.Add((row, summary)); tables.Add(cells); }
        foreach (var (control, _) in rows) list.Children.Add(control);

        void Apply() {
            string needle = search.Text ?? "";
            int shown = 0;
            foreach (var (control, summary) in rows) {
                bool matches = summary.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)
                    && _workspaceShow switch { "unclean" => Unclean(summary), "clean" => !Unclean(summary), _ => true };
                control.IsVisible = matches; if (matches) shown++;
            }
            if (rows.Count > 0 && shown == 0) _status.Text = "No repository here matches that filter.";
        }
        int scopeColumn = 0;
        var scopeButtons = new List<(Button Button, string Id)>();
        foreach (var (id, label) in new[] { ("all", "All"), ("unclean", "Unclean"), ("clean", "Clean") }) {
            var tab = Button(label + " repositories", () => { });
            tab.Content = Text(label, 11, Muted); tab.Classes.Add("segment"); tab.Padding = new Thickness(11, 5);
            ToolTip.SetTip(tab, id switch { "all" => "Every repository in this folder", "unclean" => "Uncommitted work, or commits not yet exchanged with the remote", _ => "Committed and in sync with the remote" });
            scopeButtons.Add((tab, id)); Add(scopes, tab, 0, scopeColumn++);
        }
        void Restyle() {
            foreach (var (tab, id) in scopeButtons) {
                bool selected = _workspaceShow == id;
                tab.Content = Text(id == "all" ? "All" : id == "unclean" ? "Unclean" : "Clean", 11, selected ? Ink : Muted, selected);
                tab.Background = selected ? SelectedSurface : Brushes.Transparent;
            }
        }
        foreach (var (tab, id) in scopeButtons) tab.Click += (_, _) => { _workspaceShow = id; Restyle(); Apply(); };
        Restyle();
        search.TextChanged += (_, _) => Apply();
        Apply();

        if (_workspaceScanning && _workspaceRows.Count == 0) {
            var notice = WorkspaceNotice("Scanning for repositories", "Reading every folder directly inside " + root + ". Large folders take a moment.");
            ((StackPanel)notice).Children.Insert(0, new ProgressBar { IsIndeterminate = true, Width = 160, Height = 3, MinHeight = 3, Foreground = Accent, Background = Hairline, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 6) });
            list.Children.Add(notice);
        }
        else if (root == null) list.Children.Add(WorkspaceNotice("No folder chosen", "Choose a folder such as D:\\Projects and every Git repository directly inside it appears here."));
        else if (_workspaceRows.Count == 0) list.Children.Add(WorkspaceNotice("No repositories here", "Nothing directly inside " + root + " is a Git repository. Repositories nested deeper are not listed."));
        Add(page, new ScrollViewer { Content = list, Margin = new Thickness(20, 0, 20, 12), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, 3);

        // A narrow window cannot fit five fixed columns: the actions drop below the folder name,
        // and the least important columns go first, so the repository name always has room.
        double laidOut = -1;
        page.SizeChanged += (_, _) => {
            double width = page.Bounds.Width;
            if (Math.Abs(width - laidOut) < 1) return;
            laidOut = width;
            bool stacked = width < 820;
            Grid.SetRow(actions, stacked ? 1 : 0); Grid.SetColumn(actions, stacked ? 0 : 1); Grid.SetColumnSpan(actions, stacked ? 2 : 1);
            actions.Margin = new Thickness(0, stacked ? 12 : 0, 0, 0);
            var widths = width >= 760 ? FullColumns : width >= 580 ? new[] { 130d, 120, 70, 0 } : new[] { 110d, 100, 0, 0 };
            foreach (var table in tables) {
                for (int i = 0; i < widths.Length; i++) table.ColumnDefinitions[i + 1].Width = new GridLength(widths[i]);
                foreach (var cell in table.Children) { int column = Grid.GetColumn(cell); if (column > 0) cell.IsVisible = widths[column - 1] > 0; }
            }
        };
        _workspace.Content = page;
    }

    const string RowColumns = "*,150,150,92,78";
    static readonly double[] FullColumns = [150, 150, 92, 78];

    static Control WorkspaceNotice(string title, string detail) {
        var notice = Col(Text(title, 14, strong: true), new TextBlock { Text = detail, Foreground = Faint, FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxWidth = 460 });
        notice.Spacing = 9; notice.Margin = new Thickness(0, 34); notice.HorizontalAlignment = HorizontalAlignment.Center;
        foreach (var child in notice.Children) child.HorizontalAlignment = HorizontalAlignment.Center;
        return notice;
    }

    Button WorkspaceRow(RepoSummary summary, out Grid grid) {
        grid = new Grid { ColumnDefinitions = new ColumnDefinitions(RowColumns), ColumnSpacing = 14 };
        // A grid rather than a horizontal stack, so a long name is trimmed instead of spilling over.
        var name = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8 };
        name.Children.Add(Icon(summary.Error != null ? "close" : "folder", summary.Error != null ? Red : Faint, 14));
        Add(name, Text(summary.Name, 12, summary.Error != null ? Muted : Ink, true), 0, 1);
        grid.Children.Add(name);
        Add(grid, Text(summary.Branch.Length > 0 ? summary.Branch : "—", 11, Muted), 0, 1);

        Control changes;
        if (summary.Error != null) changes = Text(summary.Error.Split('\n')[0], 11, Red);
        else if (!summary.IsDirty) changes = Text("clean", 11, Green);
        else {
            var parts = Row(); parts.Spacing = 7;
            if (summary.Added > 0) parts.Children.Add(Text("+" + summary.Added, 11, Green));
            if (summary.Modified > 0) parts.Children.Add(Text("~" + summary.Modified, 11, Accent));
            if (summary.Deleted > 0) parts.Children.Add(Text("−" + summary.Deleted, 11, Red));
            if (summary.Conflicts > 0) parts.Children.Add(Text("!" + summary.Conflicts, 11, Amber));
            changes = parts;
        }
        Add(grid, changes, 0, 2);

        var sync = Row(); sync.Spacing = 7;
        if (summary.Ahead > 0) sync.Children.Add(Text("↑" + summary.Ahead, 11, Accent));
        if (summary.Behind > 0) sync.Children.Add(Text("↓" + summary.Behind, 11, Amber));
        if (sync.Children.Count == 0) sync.Children.Add(Text("—", 11, Faint));
        Add(grid, sync, 0, 3);
        Add(grid, Text(summary.Remotes == 0 ? "local" : summary.Remotes.ToString(), 11, summary.Remotes == 0 ? Faint : Muted), 0, 4);

        var row = Button(summary.Name, () => Run(() => OpenRepository(summary.Path)));
        row.Content = grid; row.HorizontalContentAlignment = HorizontalAlignment.Stretch; row.HorizontalAlignment = HorizontalAlignment.Stretch;
        row.Padding = new Thickness(0, 13); row.Background = Brushes.Transparent; row.CornerRadius = new CornerRadius(0);
        row.BorderBrush = Hairline; row.BorderThickness = new Thickness(0, 0, 0, 1);
        row.Classes.Add("selection-item");
        ToolTip.SetTip(row, summary.Error ?? summary.Path);

        var menu = new ContextMenu();
        menu.Items.Add(MenuAction("Open", () => OpenRepository(summary.Path)));
        menu.Items.Add(MenuAction("Reveal in File Explorer", () => Reveal(summary.Path)));
        menu.Items.Add(MenuAction("Copy path", () => CopyText(summary.Path, "Path copied.")));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuAction("Fetch this repository", () => FetchOne(summary), summary.Remotes > 0));
        row.ContextMenu = menu;
        return row;
    }

    async Task FetchOne(RepoSummary summary) {
        try { await new GitRepository(summary.Path).Git("fetch", "--all"); _status.Text = "Fetched " + summary.Name; }
        catch (Exception e) when (e is InvalidOperationException or TimeoutException) { _status.Text = summary.Name + ": " + e.Message.Split('\n')[0]; return; }
        await ScanWorkspace();
    }

    /// <summary>Runs one Git operation across the repositories currently listed. Fetch only reads,
    /// so it just runs; pull and push change history or publish it, so they name every repository
    /// they would touch and do nothing at all unless that is confirmed.</summary>
    async Task WorkspaceBulk(string operation) {
        var listed = _workspaceRows.Where(r => r.Error == null && r.Remotes > 0).ToArray();
        var targets = operation switch {
            "push" => listed.Where(r => r.Ahead > 0).ToArray(),
            "pull" => listed.Where(r => !r.IsDirty).ToArray(),
            _ => listed,
        };
        int skipped = operation == "pull" ? listed.Length - targets.Length : 0;
        if (targets.Length == 0) {
            _status.Text = operation switch {
                "push" => "Nothing to push: no repository here has commits waiting for a remote.",
                "pull" => listed.Length == 0 ? "Nothing to pull: no repository here has a remote." : "Nothing to pull: every repository with a remote has uncommitted changes.",
                _ => "Nothing to fetch: no repository here has a remote.",
            };
            return;
        }
        if (operation != "fetch") {
            string names = string.Join("\n", targets.Select(r => "  " + r.Name + (operation == "push" ? $"  ↑{r.Ahead}" : r.Behind > 0 ? $"  ↓{r.Behind}" : "")));
            string caution = operation == "push"
                ? "Each branch is pushed to its own upstream. Nothing is forced, so a push that would overwrite commits the remote gained is refused."
                : "Each repository fast-forwards, merges, or stops — whatever its own Git configuration says. A merge can leave conflicts to resolve.";
            string note = skipped > 0 ? $"\n\n{skipped} {(skipped == 1 ? "repository is" : "repositories are")} skipped for having uncommitted changes." : "";
            if (!await ReviewAction(operation == "push" ? "Push these repositories?" : "Pull into these repositories?",
                $"{targets.Length} {(targets.Length == 1 ? "repository" : "repositories")}:\n\n{names}{note}\n\n{caution}",
                operation == "push" ? "Push all" : "Pull all")) { _status.Text = "Nothing was " + (operation == "push" ? "pushed." : "pulled."); return; }
        }
        int done = 0; var failed = new List<string>();
        foreach (var target in targets) {
            try {
                await new GitRepository(target.Path).Git(operation == "fetch" ? "fetch" : operation, operation == "fetch" ? "--all" : "--verbose");
                done++;
            } catch (Exception e) when (e is InvalidOperationException or TimeoutException) { failed.Add(target.Name + ": " + e.Message.Split('\n')[0]); }
        }
        await ScanWorkspace();
        string verb = operation == "fetch" ? "Fetched" : operation == "pull" ? "Pulled" : "Pushed";
        _status.Text = failed.Count == 0 ? $"{verb} {done} of {targets.Length}." : $"{verb} {done} of {targets.Length} · {failed.Count} failed · {failed[0]}";
        if (failed.Count > 0) await ShowMessage(verb + " " + done + " of " + targets.Length, string.Join("\n", failed));
    }
}
