using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Gitland.Core;
using static Gitland.App.Palette;

namespace Gitland.App;

public sealed partial class MainWindow {
    readonly TextBlock _sidebarContext = Text("Changes", 12, Muted, true);
    readonly TextBlock _workspaceHint = Text("No repository", 11, Faint);
    readonly Grid _fileScopes = new() { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = 2 };
    Grid? _shell, _titlebar;
    Border? _workspaceFrame;
    Button? _maximizeButton;
    WindowState _beforeFullscreen = WindowState.Normal;
    Control BuildShell() {
        _shell = new Grid { RowDefinitions = new RowDefinitions("48,*,24"), Background = Bar, Name = "WindowShell" };
        var titlebar = _titlebar = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), Background = Bar };
        var appMenu = Button("Gitland menu", ShowAppMenu); appMenu.Classes.Add("quiet");
        appMenu.Content = Row(Logo(29), Text("gitland", 16, strong: true), Icon("down", Faint, 10)); appMenu.Margin = new Thickness(12, 0, 26, 0);
        titlebar.Children.Add(appMenu);
        var breadcrumb = Row(Icon("folder", Faint, 13), _repoName, Text("/", 12, Faint), Icon("branch", Accent, 13), _branch); breadcrumb.ClipToBounds = true; Add(titlebar, breadcrumb, 0, 1);
        _repoName.FontSize = 12; _repoName.FontWeight = FontWeight.Medium; _repoName.MaxWidth = 170; _branch.MaxWidth = 210; _branch.Foreground = Muted;
        var open = Button("Open repository", () => Run(PickRepository), "folder"); open.Classes.Add("quiet");
        var actions = Row(IconButton("New repository", () => Run(CreateRepositoryDialog), "plus"), open, IconButton("Refresh · F5", () => Run(Refresh), "refresh")); actions.Spacing = 3; actions.Margin = new Thickness(0, 0, 12, 0); Add(titlebar, actions, 0, 2);
        _maximizeButton = IconButton("Maximize or restore window", ToggleMaximize, "maximize");
        var windowActions = Row(IconButton("Minimize window", () => WindowState = WindowState.Minimized, "minimize"), _maximizeButton, IconButton("Close window", Close, "close")); windowActions.Spacing = 0;
        foreach (var button in windowActions.Children.OfType<Button>()) { button.Width = 44; button.Height = 48; button.CornerRadius = new CornerRadius(0); }
        Add(titlebar, windowActions, 0, 3);
        titlebar.PointerPressed += (_, e) => {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || IsButtonSource(e.Source)) return;
            if (e.ClickCount == 2) ToggleMaximize(); else BeginMoveDrag(e);
        };
        Add(_shell, titlebar, 0);
        var body = new Grid { Name = "WorkspaceColumns", ColumnDefinitions = new ColumnDefinitions("64,280,5,*") };
        double sidebarWidth = GitlandApplication.Preferences.SidebarWidth;
        body.ColumnDefinitions[1].MinWidth = 220;
        var rail = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Background = Ground };
        _navigation.Orientation = Orientation.Vertical; _navigation.Margin = new Thickness(5, 14); _navigation.Spacing = 9;
        rail.Children.Add(_navigation);
        var settings = IconButton("Settings", () => _ = ShowSettings(), "settings"); settings.Classes.Add("quiet"); settings.Margin = new Thickness(0, 12); settings.HorizontalAlignment = HorizontalAlignment.Center; Add(rail, settings, 1);
        body.Children.Add(rail);
        var sidebar = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,*,Auto,Auto"), Background = Bar };
        var filesHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 10, Margin = new Thickness(17, 19, 12, 14) };
        _sidebarContext.FontSize = 17; _sidebarContext.Foreground = Ink;
        filesHeader.Children.Add(_sidebarContext); Add(filesHeader, _fileCount, 0, 1);
        _stageAllButton = IconButton("Stage all changes", () => Run(async () => { if (_repo == null) return; await _repo.StageAllAsync(); await Refresh(); }), "plus");
        _stageAllButton.Classes.Add("quiet"); ToolTip.SetTip(_stageAllButton, "Stage all changes"); Add(filesHeader, _stageAllButton, 0, 2); Add(sidebar, filesHeader, 0);
        var scopes = new Border { Child = _fileScopes, Background = Ground, CornerRadius = new CornerRadius(7), Padding = new Thickness(3), Margin = new Thickness(12, 0, 12, 10) }; Add(sidebar, scopes, 1);
        _fileSearch.Margin = new Thickness(12, 0, 12, 8); _fileSearch.FontSize = 12; Add(sidebar, _fileSearch, 2);
        _changeFilters.Margin = new Thickness(12, 0, 8, 10); Add(sidebar, _changeFilters, 3);
        _fileList.Spacing = 1;
        Add(sidebar, new ScrollViewer { Content = _fileList, Margin = new Thickness(8, 0), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, 4);
        var local = Button("Compare files", () => Run(CompareLocalFiles), "compare"); local.Classes.Add("quiet"); local.HorizontalAlignment = HorizontalAlignment.Left;
        var foot = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(9, 7) }; foot.Children.Add(local);
        Add(sidebar, foot, 5);
        Add(sidebar, BuildCommitComposer(), 6);
        Add(body, sidebar, 0, 1);
        Add(body, new Border { Background = Hairline, Width = 1, HorizontalAlignment = HorizontalAlignment.Center, IsHitTestVisible = false }, 0, 2);
        var splitter = new GridSplitter { Name = "SidebarSplitter", ResizeDirection = GridResizeDirection.Columns, ResizeBehavior = GridResizeBehavior.PreviousAndNext, Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, Cursor = new Cursor(StandardCursorType.SizeWestEast), KeyboardIncrement = 10, DragIncrement = 1, Focusable = true };
        Avalonia.Automation.AutomationProperties.SetName(splitter, "Resize file sidebar");
        ToolTip.SetTip(splitter, "Drag to resize the file sidebar · Double-click to reset · Arrow keys when focused");
        void SaveSidebar() {
            sidebarWidth = body.ColumnDefinitions[1].Width.Value;
            var preferences = GitlandApplication.Preferences with { SidebarWidth = sidebarWidth };
            try { GitlandApplication.SettingsStore.Save(preferences); GitlandApplication.Preferences = preferences.Normalize(); }
            catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException) { _status.Text = "Sidebar resized; could not save width: " + e.Message; }
        }
        bool sidebarDragged = false;
        splitter.DragStarted += (_, _) => sidebarDragged = false;
        splitter.DragDelta += (_, e) => { if (Math.Abs(e.Vector.X) > 0) sidebarDragged = true; };
        splitter.DragCompleted += (_, _) => { if (sidebarDragged) SaveSidebar(); };
        splitter.KeyUp += (_, e) => { if (e.Key is Key.Left or Key.Right) SaveSidebar(); };
        Add(body, splitter, 0, 2);
        var content = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Background = Bar };
        _contextBar.Margin = new Thickness(22, 19); Add(content, _contextBar, 0);
        _workspaceFrame = new Border { Background = Ground, ClipToBounds = true, CornerRadius = new CornerRadius(4), BorderBrush = Hairline, BorderThickness = new Thickness(1), Child = _workspace, Margin = new Thickness(16, 0, 16, 16) };
        Add(content, _workspaceFrame, 1); Add(body, content, 0, 3); Add(_shell, body, 1);
        var status = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Background = Ground };
        _status.FontSize = 10; _status.Foreground = Faint; _status.Margin = new Thickness(14, 0); status.Children.Add(_status);
        var shortcuts = Text("Ctrl+Enter  Commit     Ctrl+,  Settings     F11  Full screen", 10, Faint); shortcuts.Margin = new Thickness(14, 0); Add(status, shortcuts, 0, 1);
        Add(_shell, new Border { Child = status, BorderBrush = Hairline, BorderThickness = new Thickness(0, 1, 0, 0) }, 2);
        void Density() {
            bool compact = Bounds.Width < 1180;
            body.ColumnDefinitions[0].Width = new GridLength(compact ? 52 : 64);
            double maximum = Math.Clamp(Bounds.Width - (compact ? 52 : 64) - 5 - 520, 220, 600);
            body.ColumnDefinitions[1].MaxWidth = maximum;
            body.ColumnDefinitions[1].Width = new GridLength(Math.Min(maximum, sidebarWidth > 0 ? sidebarWidth : compact ? 232 : 280));
            _workspaceFrame!.Margin = compact ? new Thickness(8, 0, 8, 8) : new Thickness(16, 0, 16, 16);
            UpdateCommitDensity();
            shortcuts.IsVisible = !compact; breadcrumb.IsVisible = Bounds.Width >= 1080;
        }
        splitter.DoubleTapped += (_, _) => { sidebarDragged = false; sidebarWidth = 0; SavePreferences(GitlandApplication.Preferences with { SidebarWidth = 0 }); Density(); };
        SizeChanged += (_, _) => Density(); Density();
        _workspace.SizeChanged += (_, _) => UpdatePageDensity();
        return _shell;
    }
    void UpdateWindowInsets() {
        // Windows maximizes custom-chrome windows beyond the work area by the resize-frame width.
        // Use the platform's logical-pixel inset; never guess an 8px padding across monitors/DPI.
        if (_shell == null) return;
        _shell.Margin = WindowState == WindowState.Maximized ? OffScreenMargin : new Thickness(0);
        if (_maximizeButton != null) _maximizeButton.Content = Icon(WindowState is WindowState.Maximized or WindowState.FullScreen ? "restore" : "maximize", Muted, 14);
    }
    void ToggleFullscreen() {
        if (WindowState == WindowState.FullScreen) WindowState = _beforeFullscreen;
        else { _beforeFullscreen = WindowState == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal; WindowState = WindowState.FullScreen; }
        UpdateMenus();
    }
    async Task SetFileScope(string scope) {
        string targetMode = scope == "conflicts" ? "merge" : "changes";
        if (!await MayLeaveMerge()) return;
        _fileScope = scope; _mode = targetMode; _filter = "all"; _externalLeft = null;
        var next = VisibleFiles().FirstOrDefault(f => f.Path == _selected?.Path) ?? VisibleFiles().FirstOrDefault();
        RenderNavigation(); RenderContext(); RenderFiles();
        if (next != null) await SelectFile(next);
        else { _selected = null; _merge = null; Empty(scope == "conflicts" ? "No conflicts to resolve" : "No files in this view", "Choose another file filter or refresh the repository."); }
        UpdateMenus();
    }
    void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    static bool IsButtonSource(object? source) => source is Visual visual && (visual is Button || visual.GetVisualAncestors().Any(v => v is Button));
    void RenderNavigation() {
        _navigation.Children.Clear();
        foreach (var (id, label, icon) in new[] { ("workspace", "Workspace", "layers"), ("changes", "Working changes", "diff"), ("compare", "Compare revisions", "compare"), ("merge", "Merge", "merge"), ("repository", "Repository", "branch"), ("tags", "Manage tags", "tag"), ("github", "GitHub & releases", "cloud") }) {
            var active = id == "merge" ? _mode is "merge" or "threeway" : id == "tags" ? _mode == "repository" && _repositoryTab == "Tags" : id == "repository" ? _mode == "repository" && _repositoryTab != "Tags" : id == _mode;
            var shortLabel = id switch { "workspace" => "Workspace", "changes" => "Changes", "compare" => "Compare", "tags" => "Tags", "merge" => "Merge", "repository" => "History", _ => "GitHub" };
            var glyph = new Grid { Height = 22 };
            glyph.Children.Add(Icon(icon, active ? Ink : Faint, 19));
            if (id == "merge" && _state.Changes.Any(c => c.IsConflict)) glyph.Children.Add(new Border { Width = 5, Height = 5, CornerRadius = new CornerRadius(3), Background = Amber, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top });
            var layout = Col(glyph, Text(shortLabel, 9, active ? Ink : Faint, active)); layout.Spacing = 7;
            foreach (var child in layout.Children) child.HorizontalAlignment = HorizontalAlignment.Center;
            var button = Button(label, () => Run(async () => { if (id is "tags" or "repository") { _repositoryTab = id == "tags" ? "Tags" : "History"; await SetMode("repository"); } else await SetMode(id == "merge" && !_state.Changes.Any(c => c.IsConflict) ? "threeway" : id); })); button.Content = layout; button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Center; button.Padding = new Thickness(0, 10); button.MinHeight = 59;
            button.Classes.Add("workspace-tab"); button.Classes.Add("selection-item"); button.Classes.Set("selected", active); button.Background = active ? SelectedSurface : Brushes.Transparent; button.BorderBrush = Brushes.Transparent; button.BorderThickness = new Thickness(0); button.CornerRadius = new CornerRadius(4);
            ToolTip.SetTip(button, label);
            _navigation.Children.Add(button);
        }
        _workspaceHint.Text = _repo == null ? "No repository" : "Local repository";
        UpdateCommitComposer();
    }
    void RenderFiles() {
        _fileList.Children.Clear(); var files = VisibleFiles().ToArray(); _fileCount.Text = files.Length.ToString();
        _sidebarContext.Text = _mode is "compare" or "threeway" ? "Comparison" : _fileScope == "conflicts" ? "Conflicts" : "Files";
        RenderChangeFilters();
        _fileScopes.Children.Clear();
        int scopeColumn = 0;
        foreach (var (id, label) in new[] { ("all", "All"), ("changed", "Changed"), ("conflicts", "Conflicts") }) {
            var tab = Button(id == "conflicts" ? "Conflicting files" : label + " files", () => Run(() => SetFileScope(id)));
            bool selectedScope = _fileScope == id && _mode is not ("compare" or "files" or "threeway");
            tab.Content = Text(label, 11, selectedScope ? Ink : Muted, selectedScope); tab.Classes.Add("segment");
            tab.HorizontalAlignment = HorizontalAlignment.Stretch; tab.HorizontalContentAlignment = HorizontalAlignment.Center; tab.Padding = new Thickness(4, 5);
            tab.Background = _fileScope == id && _mode is not ("compare" or "files" or "threeway") ? SelectedSurface : Brushes.Transparent;
            ToolTip.SetTip(tab, id == "all" ? "Tracked files and untracked files, excluding ignored files" : id == "changed" ? "All working tree and staged changes" : "Files that need conflict resolution");
            Add(_fileScopes, tab, 0, scopeColumn++);
        }
        void FileRow(GitChange file, bool? stagedView = null, bool showPath = false) {
            bool active = file.Path == _selected?.Path && (stagedView == null || _reviewStaged == stagedView);
            var color = file.IsConflict ? Amber : stagedView == true ? Green : file.Index is '?' or 'A' ? Green : file.Label == "Deleted" ? Red : Muted;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"), ColumnSpacing = 8 };
            // The tick changes only the selection; the row itself still opens the diff.
            var tick = new CheckBox { IsChecked = IsChecked(file, stagedView), MinWidth = 0, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center };
            Avalonia.Automation.AutomationProperties.SetName(tick, "Select " + file.Path);
            ToolTip.SetTip(tick, "Select this file for a multi-file action");
            tick.IsCheckedChanged += (_, _) => { if (tick.IsChecked != IsChecked(file, stagedView)) ToggleChecked(file, stagedView, tick.IsChecked == true); };
            row.Children.Add(tick);
            Add(row, Icon(file.IsConflict ? "merge" : "file", active ? Ink : Faint, 13), 0, 1);
            Control name = Text(System.IO.Path.GetFileName(file.Path), 12, active ? Ink : Muted);
            string folder = System.IO.Path.GetDirectoryName(file.Path)?.Replace('\\', '/') ?? "";
            if (showPath && folder.Length > 0) { var labels = Col(name, Text(folder, 10, Faint)); labels.Spacing = 3; name = labels; }
            Add(row, name, 0, 2);
            Add(row, file.IsConflict ? Text("!", 11, Amber) : (_mode is "changes" or "merge") && (stagedView == true || file.IsStaged && !file.IsUnstaged) ? Icon("check", Green, 12) : Text(!file.IsChanged ? "" : file.Index == '?' ? "A" : file.Label == "Deleted" ? "D" : "M", 10, color), 0, 3);
            var button = Button(file.Path, () => Run(() => SelectFile(file, stagedView))); button.Content = row; button.HorizontalContentAlignment = HorizontalAlignment.Stretch; button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.Classes.Add("selection-item"); button.Classes.Set("selected", active); button.Padding = new Thickness(12, 8); button.Background = active ? SelectedSurface : Brushes.Transparent; button.BorderBrush = Brushes.Transparent; button.BorderThickness = new Thickness(0); button.CornerRadius = new CornerRadius(3); ToolTip.SetTip(button, file.Path + " · " + (stagedView == true ? "Staged changes" : file.Label));
            // Every row in every scope gets the same menu, built from this row's group and state.
            button.ContextMenu = FileContextMenu(file, stagedView);
            _fileList.Children.Add(button);
        }
        if (_mode == "changes" && _fileScope == "changed") {
            void Group(string label, IEnumerable<GitChange> entries, IBrush color, bool staged) {
                var items = entries.ToArray();
                if (items.Length == 0) return;
                var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 8, Margin = new Thickness(13, 12, 12, 6) };
                int ticked = items.Count(f => IsChecked(f, staged));
                var all = new CheckBox { MinWidth = 0, Padding = new Thickness(0), IsThreeState = true, VerticalAlignment = VerticalAlignment.Center };
                all.IsChecked = ticked == 0 ? false : ticked == items.Length ? true : null;
                Avalonia.Automation.AutomationProperties.SetName(all, "Select all " + label.ToLowerInvariant());
                ToolTip.SetTip(all, ticked == items.Length ? "Clear this group" : "Select every file in this group");
                all.Click += (_, _) => {
                    bool selectAll = ticked < items.Length;
                    foreach (var entry in items) {
                        var key = new FileSelection(entry.Path, staged);
                        if (selectAll) _checked.Add(key); else _checked.Remove(key);
                    }
                    RenderFiles(); UpdateSelectionStatus();
                };
                header.Children.Add(all);
                Add(header, Text(label, 11, color, true), 0, 1);
                Add(header, Text(ticked > 0 ? $"{ticked}/{items.Length}" : items.Length.ToString(), 10, ticked > 0 ? Accent : Faint), 0, 2); _fileList.Children.Add(header);
                foreach (var file in items) FileRow(file, staged, true);
            }
            Group("Unstaged", files.Where(f => _filter != "staged" && !f.IsConflict && f.IsUnstaged), Muted, false);
            Group("Staged", files.Where(f => _filter != "unstaged" && !f.IsConflict && f.IsStaged), Green, true);
            Group("Conflicts", files.Where(f => f.IsConflict), Amber, false);
        } else foreach (var group in files.GroupBy(f => System.IO.Path.GetDirectoryName(f.Path)?.Replace('\\', '/') ?? "")) {
            if (group.Key.Length > 0) {
                var folder = Row(Icon("folder", Faint, 12), Text(group.Key, 11, Faint)); folder.Spacing = 7; folder.Margin = new Thickness(10, 12, 0, 7); _fileList.Children.Add(folder);
            }
            foreach (var file in group) FileRow(file);
        }
        if (files.Length == 0) { var empty = Text(_mode == "compare" ? "No comparison loaded" : "No matching files", 12, Faint); empty.Margin = new Thickness(10, 16); _fileList.Children.Add(empty); }
    }
    void RenderContext() {
        // Detach the stable revision toolbar from the previous view before reusing it.
        _pageControls?.Children.Clear();
        _contextBar.Children.Clear();
        _contextBar.IsVisible = true;
        if (_workspaceFrame != null && _mode == "merge") _workspaceFrame.Margin = new Thickness(8);
        string title = _mode switch { "workspace" => "Workspace", "compare" => "Compare revisions", "threeway" => "Merge", "merge" => "Merge", "repository" => "Repository", "github" => "GitHub & releases", "files" => "Local files", _ => "Working changes" };
        _pageHeading = Col(Text(title, 22, strong: true)); _contextBar.Children.Add(_pageHeading);
        _pageControls = new WrapPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        Add(_contextBar, _pageControls, 0, 1);
        if (_mode is "merge" or "threeway") {
            var conflicts = Button("Resolve conflicts", () => Run(() => SetMode("merge")));
            var compare = Button("Three-way comparison", () => Run(() => SetMode("threeway")));
            conflicts.Classes.Set("primary", _mode == "merge"); compare.Classes.Set("primary", _mode == "threeway");
            var modes = Segmented(conflicts, compare); modes.HorizontalAlignment = HorizontalAlignment.Left;
            _pageHeading = Col(modes); _contextBar.Children.Clear(); _contextBar.Children.Add(_pageHeading); Add(_contextBar, _pageControls, 0, 1);
        }
        if (_mode == "changes") {
            _pageHeading.Children.Add(Text(_fileScope == "all" ? $"{_state.AllFiles.Count} files in this repository" : $"{_state.Changes.Count} changed files · {_state.Changes.Count(c => c.IsStaged && !c.IsConflict)} staged for commit", 11, Faint));
            var next = Button("Review staged", () => Run(() => FilterChanges("staged")), "check"); next.Classes.Add("quiet");
            _pageControls.Children.Add(next);
        } else if (_mode == "compare") {
            _revisionControls ??= Row(_leftRef, Icon("compare", Faint, 14), _rightRef, Button("Compare revisions", () => Run(LoadComparison), primary: true));
            _pageControls.Children.Add(_revisionControls);
        }
        else if (_mode == "threeway") { _pageControls.Children.Add(Button("Choose three revisions", () => Run(ThreeWayDialog))); _pageControls.Children.Add(Button("Compare three files", () => Run(ThreeLocalFiles))); }
        else if (_mode == "repository") { var create = Button("New branch", () => Run(CreateBranchDialog), "branch"); create.IsEnabled = _repo != null; _pageControls.Children.Add(create); }
        else if (_mode == "merge") { int count = _state.Changes.Count(c => c.IsConflict); _pageControls.Children.Add(Text($"{count} conflicted {(count == 1 ? "file" : "files")}", 11, Amber)); }
        // The folder and the repository count are already the page's own heading; the context bar
        // adds only the one number that is not visible without reading every row.
        else if (_mode == "workspace") _pageControls.Children.Add(Text(_workspaceRows.Count == 0 ? "" : $"{_workspaceRows.Count(Unclean)} of {_workspaceRows.Count} need attention", 11, Faint));
        else if (_mode == "files") _pageControls.Children.Add(Text("Read only", 11, Faint));
        else if (_mode == "github") _pageControls.Children.Add(Text(_githubUser == null ? "Not connected" : "@" + _githubUser, 11, Faint));
        UpdatePageDensity();
    }
    void UpdatePageDensity() {
        if (_pageHeading == null || _pageControls == null) return;
        bool stacked = _mode == "compare" && _workspace.Bounds.Width < 900;
        Grid.SetColumnSpan(_pageHeading, stacked ? 2 : 1); Grid.SetRow(_pageControls, stacked ? 1 : 0); Grid.SetColumn(_pageControls, stacked ? 0 : 1); Grid.SetColumnSpan(_pageControls, stacked ? 2 : 1);
        _pageControls.HorizontalAlignment = stacked ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        _contextBar.Margin = new Thickness(20, _mode == "changes" && Bounds.Height >= 720 ? 18 : 10, 20, _mode == "changes" && Bounds.Height >= 720 ? 18 : 10);
    }
}
