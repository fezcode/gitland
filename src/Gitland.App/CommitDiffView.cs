using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Gitland.Core;
using static Gitland.App.Palette;

namespace Gitland.App;

/// <summary>A read-only, per-file review of a pinned commit and one of its parents.</summary>
public sealed class CommitDiffView : Grid {
    readonly GitRepository _repository;
    readonly Func<GitCommit, Control> _actions;
    readonly TextBlock _subject = Text("Select a commit to review its changes", 15, strong: true);
    readonly TextBlock _metadata = Text("", 11, Faint), _path = Text("", 12), _totals = Text("", 11, Faint);
    readonly TextBlock _added = Text("", 11, Green), _removed = Text("", 11, Red);
    readonly TextBlock _before = Text("PARENT", 10, Faint), _after = Text("COMMIT", 10, Faint);
    readonly StackPanel _files = new() { Spacing = 2 };
    readonly ContentControl _actionHost = new(), _display = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    readonly ComboBox _parents = new() { Name = "CommitParent", MinWidth = 170, FontSize = 11 };
    readonly TextBox _filter = new() { Watermark = "Filter changed files", Margin = new Thickness(10, 8), FontSize = 11 };
    readonly DiffCanvas _canvas = new() { Name = "HistoryDiff", CodeSize = GitlandApplication.Preferences.CodeSize, VerticalAlignment = VerticalAlignment.Top };
    readonly ScrollViewer _scroll;
    readonly ScrollBar _horizontal = new() { Orientation = Orientation.Horizontal, Height = 13 };
    readonly CheckBox _context = new() { Content = "Changes only", FontSize = 11 }, _whitespace = new() { Content = "Ignore whitespace", FontSize = 11 };
    readonly TextBox _search = new() { Watermark = "Find in commit diff", Width = 160, FontSize = 11 };
    readonly Button _split, _unified;
    CommitComparison? _comparison;
    GitCommit? _commit;
    GitChange? _file;
    FileComparison? _source;
    DiffResult? _diff;
    int _generation;
    bool _changingParent, _isUnified;
    int _loadingDepth;
    public bool IsLoading => _loadingDepth > 0;
    readonly ProgressBar _loading = new() { Name = "CommitLoading", Height = 2, Minimum = 0, Maximum = 100, Value = 35, Foreground = Accent, Background = Brushes.Transparent, Opacity = 0 };
    void Loading(bool begin) {
        _loadingDepth += begin ? 1 : -1;
        _loading.Opacity = IsLoading ? 1 : 0;
        _loading.IsIndeterminate = IsLoading && !Motion.Reduced;
    }

    public CommitDiffView(GitRepository repository, Func<GitCommit, Control> actions) {
        _repository = repository; _actions = actions; Name = "CommitReview";
        RowDefinitions = new("Auto,2,*"); Background = Ground;
        Place(this, _loading, 1); Motion.Bind(_loading, reduced => _loading.IsIndeterminate = IsLoading && !reduced);
        Motion.PrepareReveal(_display);
        var heading = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new Thickness(14, 10), ColumnSpacing = 12 };
        var identity = Col(_subject, _metadata); identity.Spacing = 4; identity.ClipToBounds = true;
        _subject.TextTrimming = TextTrimming.CharacterEllipsis;
        heading.Children.Add(identity); Place(heading, Row(_parents, _actionHost), 0, 1);
        Children.Add(new Border { Child = heading, Background = Bar, BorderBrush = Hairline, BorderThickness = new Thickness(0, 1, 0, 1) });
        var body = new Grid { ColumnDefinitions = new("200,5,*") };
        body.ColumnDefinitions[0].MinWidth = 130; body.ColumnDefinitions[0].MaxWidth = 360; body.ColumnDefinitions[2].MinWidth = 260;
        var navigator = new Grid { RowDefinitions = new("Auto,Auto,*"), Background = Bar };
        _totals.Margin = new Thickness(12, 10, 10, 0); navigator.Children.Add(_totals); Place(navigator, _filter, 1);
        Place(navigator, new ScrollViewer { Content = _files, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(0) }, 2);
        body.Children.Add(navigator);
        Place(body, new GridSplitter { ResizeDirection = GridResizeDirection.Columns, ResizeBehavior = GridResizeBehavior.PreviousAndNext, Background = Hairline, HorizontalAlignment = HorizontalAlignment.Stretch }, 0, 1);
        var review = new Grid { RowDefinitions = new("Auto,Auto,Auto,*,Auto"), ClipToBounds = true };
        var title = Row(_path, _added, _removed); title.Margin = new Thickness(12, 8); review.Children.Add(title); _path.TextTrimming = TextTrimming.CharacterEllipsis;
        var toolbar = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 8, 6) };
        _split = Button("Commit diff side by side", () => SetUnified(false)); _split.Content = Text("Side by side", 11);
        _unified = Button("Commit diff unified", () => SetUnified(true)); _unified.Content = Text("Unified", 11);
        foreach (var item in new Control[] { Segmented(_split, _unified), _context, _whitespace, _search, IconButton("Previous commit change", () => Navigate(-1), "up"), IconButton("Next commit change", () => Navigate(1), "down") }) { item.Margin = new Thickness(4, 2); toolbar.Children.Add(item); }
        Place(review, toolbar, 1);
        var labels = new Grid { ColumnDefinitions = new("*,22,*"), Background = Raised, Height = 26 };
        _before.Margin = _after.Margin = new Thickness(12, 0); labels.Children.Add(_before); Place(labels, _after, 0, 2); Place(review, labels, 2);
        _scroll = new ScrollViewer { Content = _canvas, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        void Viewport() { _canvas.ScrollTop = _scroll.Offset.Y; _canvas.ViewHeight = _scroll.Viewport.Height; _canvas.InvalidateVisual(); }
        _scroll.ScrollChanged += (_, _) => Viewport(); _scroll.SizeChanged += (_, _) => { Viewport(); UpdateHorizontal(); };
        Place(review, _display, 3); Place(review, _horizontal, 4);
        _horizontal.ValueChanged += (_, _) => { _canvas.HorizontalOffset = _horizontal.Value; _canvas.InvalidateVisual(); };
        review.SizeChanged += (_, _) => _path.MaxWidth = Math.Max(100, review.Bounds.Width - 130);
        Place(body, review, 0, 2); Place(this, body, 2);
        _parents.SelectionChanged += async (_, _) => { if (!_changingParent && _commit != null && _parents.SelectedIndex >= 0) await LoadAsync(_commit, _parents.SelectedIndex); };
        _filter.TextChanged += (_, _) => RenderFiles();
        _context.IsCheckedChanged += (_, _) => RenderDiff();
        _whitespace.IsCheckedChanged += async (_, _) => await CalculateAsync();
        _canvas.ExpandRequested += () => _context.IsChecked = false;
        _search.TextChanged += (_, _) => { _context.IsChecked = false; _canvas.Search = _search.Text ?? ""; _canvas.InvalidateVisual(); };
        _search.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) { Navigate(e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift) ? -1 : 1, true); e.Handled = true; } };
        _split.Classes.Add("primary"); ShowNotice("Select a commit in the graph above.");
    }
    static void Place(Grid grid, Control child, int row, int column = 0) { Grid.SetRow(child, row); Grid.SetColumn(child, column); grid.Children.Add(child); }
    void ShowNotice(string message) { _display.Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = Muted, Margin = new Thickness(24), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 420 }; }

    public async Task LoadAsync(GitCommit commit, int parentIndex = 0) {
        int generation = ++_generation; string? previousPath = _file?.Path;
        if (_commit?.Hash != commit.Hash) _filter.Text = "";
        _commit = commit; _comparison = null; _source = null; _diff = null; _file = null; _files.Children.Clear(); _path.Text = _added.Text = _removed.Text = _totals.Text = "";
        _subject.Text = commit.Subject; _metadata.Text = commit.ShortHash + "  ·  " + commit.Author + "  ·  " + commit.Date;
        _parents.IsEnabled = false; _actionHost.Content = _actions(commit); ShowNotice("Loading commit changes…");
        Loading(true);
        try {
            var comparison = await _repository.ReadCommitComparisonAsync(commit.Hash, parentIndex);
            if (generation != _generation) return;
            _comparison = comparison; ToolTip.SetTip(_subject, comparison.Message);
            _changingParent = true;
            _parents.ItemsSource = comparison.Parents.Count == 0 ? new[] { "Initial commit · empty tree" } : comparison.Parents.Select((p, i) => $"Parent {i + 1} · {p[..8]}").ToArray();
            _parents.SelectedIndex = parentIndex; _parents.IsEnabled = comparison.Parents.Count > 1; _changingParent = false;
            _totals.Text = $"{comparison.Files.Count} changed {(comparison.Files.Count == 1 ? "file" : "files")}";
            _before.Text = comparison.Parent == null ? "EMPTY TREE" : "PARENT · " + comparison.Parent[..8];
            _after.Text = "COMMIT · " + comparison.Commit[..8];
            if ((comparison.Files.FirstOrDefault(f => f.Path == previousPath) ?? comparison.Files.FirstOrDefault()) is { } file) await SelectFileAsync(file);
            else { RenderFiles(); ShowNotice("This commit has no file changes relative to this parent."); }
        } catch (Exception e) { if (generation == _generation) ShowNotice(e.Message); }
        finally { Loading(false); }
    }
    void RenderFiles() {
        _files.Children.Clear();
        foreach (var file in _comparison?.Files.Where(f => f.Path.Contains(_filter.Text ?? "", StringComparison.OrdinalIgnoreCase)) ?? []) {
            var button = Button("Review committed file " + file.Path, async () => await SelectFileAsync(file));
            var label = Text(file.Path, 11, _file == file ? Ink : Muted); label.TextTrimming = TextTrimming.CharacterEllipsis;
            var row = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 8 };
            row.Children.Add(Text(file.Index.ToString(), 10, file.Index == 'D' ? Red : file.Index == 'A' ? Green : Faint)); Place(row, label, 0, 1);
            button.Content = row; button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            button.CornerRadius = new CornerRadius(0); button.Padding = new Thickness(12, 8);
            button.Classes.Add("selection-item"); button.Classes.Set("selected", _file == file); button.Background = _file == file ? SelectedSurface : Brushes.Transparent; button.BorderThickness = new Thickness(0);
            ToolTip.SetTip(button, file.OldPath == null ? file.Path : file.OldPath + " → " + file.Path); _files.Children.Add(button);
        }
        if (_comparison?.Files.Count > 0 && _files.Children.Count == 0) _files.Children.Add(Text("No matching files", 11, Faint));
    }
    async Task SelectFileAsync(GitChange file) {
        if (_comparison == null) return;
        int generation = ++_generation; var comparison = _comparison;
        _file = file; _source = null; _diff = null; _added.Text = _removed.Text = "";
        _path.Text = file.OldPath == null ? file.Path : file.OldPath + " → " + file.Path;
        ToolTip.SetTip(_path, _path.Text); RenderFiles(); ShowNotice("Loading diff…");
        Loading(true);
        try {
            var source = await _repository.ReadCommitFileAsync(comparison, file);
            if (generation != _generation) return;
            _source = source;
            if (source.Binary) { ShowNotice("Binary file changed. Text comparison is unavailable."); return; }
            await CalculateAsync();
        } catch (Exception e) { if (generation == _generation) ShowNotice(e.Message); }
        finally { Loading(false); }
    }
    async Task CalculateAsync() {
        if (_source is not { Binary: false } source) return;
        int generation = ++_generation; bool whitespace = _whitespace.IsChecked == true;
        var diff = await Task.Run(() => DiffEngine.Compare(source.Left, source.Right, whitespace));
        if (generation != _generation) return;
        _diff = diff; _added.Text = "+" + diff.Added; _removed.Text = "−" + diff.Removed;
        _scroll.Offset = default; _horizontal.Value = 0; RenderDiff(); Navigate(1); Motion.Reveal(_display);
    }
    public void FocusSearch() { _search.Focus(); _search.SelectAll(); }
    public void NavigateChange(int direction) => Navigate(direction);
    void SetUnified(bool unified) { _isUnified = unified; _split.Classes.Set("primary", !unified); _unified.Classes.Set("primary", unified); _after.IsVisible = !unified; RenderDiff(); }
    void RenderDiff() {
        if (_diff == null) return;
        if (_comparison != null) {
            string parent = _comparison.Parent == null ? "EMPTY TREE" : "PARENT · " + _comparison.Parent[..8];
            _before.Text = _isUnified ? parent + " → COMMIT · " + _comparison.Commit[..8] : parent;
            _after.IsVisible = !_isUnified; Grid.SetColumnSpan(_before, _isUnified ? 3 : 1);
        }
        _canvas.SetRows(_context.IsChecked == true ? DiffEngine.Fold(_diff.Rows) : _diff.Rows, _isUnified);
        _display.Content = _scroll; UpdateHorizontal();
    }
    void UpdateHorizontal() {
        int longest = _canvas.Rows.SelectMany(r => new[] { r.Left?.Length ?? 0, r.Right?.Length ?? 0 }).DefaultIfEmpty(0).Max();
        double paneWidth = _isUnified ? _scroll.Viewport.Width : (_scroll.Viewport.Width - 22) / 2;
        _horizontal.Maximum = Math.Max(0, longest * _canvas.CodeSize * .7 + 80 - paneWidth); _horizontal.ViewportSize = Math.Max(0, paneWidth);
    }
    void Navigate(int direction, bool search = false) {
        var rows = _canvas.Rows;
        var matches = Enumerable.Range(0, rows.Count).Where(i => search ? !string.IsNullOrEmpty(_search.Text) && ((rows[i].Left ?? "") + "\n" + rows[i].Right).Contains(_search.Text, StringComparison.OrdinalIgnoreCase) : rows[i].Kind is not (ChangeKind.Equal or ChangeKind.Fold) && (i == 0 || rows[i - 1].Kind is ChangeKind.Equal or ChangeKind.Fold)).ToArray();
        if (matches.Length == 0) return;
        int at = direction > 0 ? matches.FirstOrDefault(i => i > _canvas.ActiveRow, matches[0]) : matches.LastOrDefault(i => i < _canvas.ActiveRow, matches[^1]);
        _canvas.ActiveRow = at; _scroll.Offset = new Vector(0, Math.Max(0, (at - 2) * DiffCanvas.LineHeight)); _canvas.InvalidateVisual();
    }
}
