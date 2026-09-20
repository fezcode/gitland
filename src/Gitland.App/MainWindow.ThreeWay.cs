using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Gitland.Core;
using static Gitland.App.Palette;

namespace Gitland.App;

public sealed partial class MainWindow {
    ThreeWayRevisions? _threeRevisions;
    ThreeWayFile? _threeFile;
    string[]? _threeLocalPaths;
    TextBox? _threeSearch;
    Action<int>? _threeNavigate;
    string _threeLeft = "HEAD", _threeRight = "main", _threeBase = "";
    bool _threeWhitespace, _threeFold, _threeDivergent;
    readonly List<DiffCanvas> _threeCanvases = [];
    async Task EnterThreeWay() {
        if (_threeLocalPaths != null) { await ShowThreeLocalFiles(_threeLocalPaths); return; }
        if (_repo == null) {
            if (_fixture == null) { Empty("Compare three versions", "Choose three local files, or open a repository to compare branches against their common ancestor."); return; }
            var merge = _fixture.Merge();
            _threeRevisions = new("Base", "Ours", "Theirs", [new("src/core/merge.ts", null, 'M', ' ')]);
            _threeFile = new("src/core/merge.ts", merge.Base, merge.Ours, merge.Theirs);
            RenderFiles(); RenderThreeWay(); return;
        }
        if (_threeRevisions == null && _threeFile != null) { RenderThreeWay(); return; }
        if (_threeRevisions == null) { Empty("Compare both sides against an ancestor", "Choose two branches, tags, or commits. Gitland finds their common ancestor, or you can specify a base. Three-file comparison also works outside Git."); return; }
        RenderFiles();
        if ((_threeRevisions.Files.FirstOrDefault(f => f.Path == _selected?.Path) ?? _threeRevisions.Files.FirstOrDefault()) is { } file) await SelectFile(file);
        else Empty("All three revisions match", "No files differ from the selected base.");
    }
    async Task ThreeWayDialog() {
        if (_repo == null) { await EnterThreeWay(); return; }
        var left = new TextBox { Text = _threeLeft }; var right = new TextBox { Text = _threeRight };
        var basis = new TextBox { Text = _threeBase, Watermark = "Automatic · common ancestor" };
        await FormDialog("Three-way comparison", [Field("Left revision", left), Field("Base revision", basis, "Leave empty to find the common ancestor."), Field("Right revision", right), Paragraph("Compare branches, tags, or commit hashes without changing your working files.")], "Compare three revisions", async () => {
            var revisions = await _repo.ThreeWayRevisionsAsync(left.Text ?? "", right.Text ?? "", basis.Text);
            _threeLeft = left.Text!; _threeRight = right.Text!; _threeBase = basis.Text ?? ""; _threeRevisions = revisions; _threeLocalPaths = null;
            _mode = "threeway"; _selected = null; RenderNavigation(); RenderContext(); await EnterThreeWay();
        });
    }
    async Task SelectThreeWay(GitChange file) {
        if (_repo != null) _threeFile = await _repo.ThreeWayFileAsync(_threeRevisions!, file.Path);
        _merge = null; _mergeDirty = false; RenderThreeWay();
    }
    async Task ThreeLocalFiles() {
        if (!await MayLeaveMerge()) return;
        var paths = new List<string>();
        foreach (var side in new[] { "Left", "Base", "Right" }) {
            var picks = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Choose " + side.ToLowerInvariant() + " file", AllowMultiple = false });
            if (picks.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
            paths.Add(path);
        }
        await ShowThreeLocalFiles(paths.ToArray());
    }
    async Task ShowThreeLocalFiles(string[] paths) {
        var texts = new List<string>();
        foreach (var path in paths) {
            var snapshot = await new GitRepository(System.IO.Path.GetDirectoryName(path)!).ReadWorkingFile(System.IO.Path.GetFileName(path));
            if (snapshot.Text.Contains('\0')) throw new InvalidOperationException("Choose text files for three-way comparison.");
            texts.Add(snapshot.Text);
        }
        _threeRevisions = null; _threeLocalPaths = paths; _selected = null; _mode = "threeway";
        _threeFile = new(string.Join(" ↔ ", paths.Select(System.IO.Path.GetFileName)), texts[1], texts[0], texts[2]);
        RenderNavigation(); RenderContext(); RenderFiles(); RenderThreeWay();
    }
    void RenderThreeWay() {
        if (_threeFile is not { } file) return;
        var result = ThreeWayDiff.Compare(file.Base, file.Left, file.Right, _threeWhitespace);
        var rows = result.Rows;
        if (_threeFold) {
            var keep = new HashSet<int>();
            for (int i = 0; i < rows.Count; i++) if (rows[i].Changed) for (int j = Math.Max(0, i - 3); j <= Math.Min(rows.Count - 1, i + 3); j++) keep.Add(j);
            var folded = new List<ThreeWayRow>();
            for (int i = 0; i < rows.Count;) {
                if (keep.Contains(i)) { folded.Add(rows[i++]); continue; }
                int start = i; while (i < rows.Count && !keep.Contains(i)) i++;
                folded.Add(new(null, null, null, null, null, null, false, false, i - start));
            }
            rows = folded;
        }
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto") };
        var heading = Col(Text(file.Path, 13, strong: true), Text($"{result.ChangeStarts.Count} change regions · {result.Rows.Count(r => r.Divergent)} lines changed differently on both sides · Read only", 11, Faint));
        heading.Margin = new Thickness(16, 12); Add(root, heading, 0);
        var scrolls = new List<ScrollViewer>(); _threeCanvases.Clear(); int current = -1;
        void Navigate(int direction, bool search = false, string query = "") {
            var matches = rows.Select((r, i) => (r, i)).Where(x => search ? query.Length > 0 && new[] { x.r.Left, x.r.Base, x.r.Right }.Any(t => t?.Contains(query, StringComparison.OrdinalIgnoreCase) == true) : (_threeDivergent ? x.r.Divergent : x.r.Changed) && (x.i == 0 || !(_threeDivergent ? rows[x.i - 1].Divergent : rows[x.i - 1].Changed))).Select(x => x.i).ToArray();
            if (matches.Length == 0) return;
            current = direction > 0 ? matches.FirstOrDefault(i => i > current, matches[0]) : matches.LastOrDefault(i => i < current, matches[^1]);
            foreach (var canvas in _threeCanvases) { canvas.ActiveRow = current; canvas.InvalidateVisual(); }
            if (scrolls.Count > 0) scrolls[0].Offset = new Vector(0, Math.Max(0, (current - 3) * DiffCanvas.LineHeight));
        }
        _threeNavigate = direction => Navigate(direction);
        var search = _threeSearch = new TextBox { Watermark = "Find in all three panes", Width = 190 };
        search.TextChanged += (_, _) => { foreach (var canvas in _threeCanvases) { canvas.Search = search.Text ?? ""; canvas.InvalidateVisual(); } };
        search.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) { Navigate(e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift) ? -1 : 1, true, search.Text ?? ""); e.Handled = true; } };
        var space = new CheckBox { Content = "Ignore whitespace", IsChecked = _threeWhitespace };
        space.IsCheckedChanged += (_, _) => { _threeWhitespace = space.IsChecked == true; RenderThreeWay(); };
        var fold = new CheckBox { Content = "Changes + context", IsChecked = _threeFold };
        fold.IsCheckedChanged += (_, _) => { _threeFold = fold.IsChecked == true; RenderThreeWay(); };
        var divergent = new CheckBox { Content = "Navigate divergent", IsChecked = _threeDivergent };
        divergent.IsCheckedChanged += (_, _) => _threeDivergent = divergent.IsChecked == true;
        var toolbar = WrapActions(IconButton("Previous three-way change", () => Navigate(-1), "up"), IconButton("Next three-way change", () => Navigate(1), "down"), search, space, fold, divergent); toolbar.Margin = new Thickness(12, 0, 12, 8); Add(root, toolbar, 1);
        var panes = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*") }; bool syncing = false;
        string[] titles = ["LEFT", "BASE · ANCESTOR", "RIGHT"];
        string[] refs = [_threeRevisions?.Left ?? "Local file", _threeRevisions?.Base ?? "Local file", _threeRevisions?.Right ?? "Local file"];
        string[] texts = [file.Left, file.Base, file.Right]; bool[] exists = [file.LeftExists, file.BaseExists, file.RightExists];
        for (int side = 0; side < 3; side++) {
            int column = side;
            var pane = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
            var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(12, 10) };
            header.Children.Add(Col(Text(titles[side], 11, strong: true), Text(exists[side] ? refs[side].Length > 12 ? Short(refs[side]) : refs[side] : "File absent in this revision", 10, Faint)));
            Add(header, IconButton("Copy " + titles[side].Split(' ')[0].ToLowerInvariant() + " source", () => Run(async () => { if (Clipboard != null) await Clipboard.SetTextAsync(texts[column]); }), "arrow-out"), 0, 1);
            Add(pane, new Border { Background = Bar, Child = header, BorderBrush = Hairline, BorderThickness = new Thickness(0, 0, 0, 1) }, 0);
            var canvas = new DiffCanvas { CodeSize = GitlandApplication.Preferences.CodeSize, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Top };
            canvas.ExpandRequested += () => { _threeFold = false; RenderThreeWay(); };
            canvas.SetAlignedRows(rows.Select(r => r.Hidden > 0 ? new DiffRow(null, null, null, null, ChangeKind.Fold, r.Hidden) : column switch {
                0 => new DiffRow(null, r.Left == null ? null : r.Base, r.LeftNumber, r.Left, r.LeftChanged && r.Left != null ? ChangeKind.Added : ChangeKind.Equal),
                1 => r.Changed && r.Base != null ? new DiffRow(r.BaseNumber, r.Base, null, r.Left ?? r.Right, ChangeKind.Removed) : new DiffRow(null, null, r.BaseNumber, r.Base, ChangeKind.Equal),
                _ => new DiffRow(null, r.Right == null ? null : r.Base, r.RightNumber, r.Right, r.RightChanged && r.Right != null ? ChangeKind.Added : ChangeKind.Equal)
            }).ToArray());
            _threeCanvases.Add(canvas);
            var scroll = new ScrollViewer { Content = canvas, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            scrolls.Add(scroll); Add(pane, scroll, 1);
            scroll.ScrollChanged += (_, _) => {
                canvas.ScrollTop = scroll.Offset.Y; canvas.ViewHeight = scroll.Viewport.Height; canvas.InvalidateVisual();
                if (syncing) return; syncing = true;
                foreach (var other in scrolls) if (other != scroll) other.Offset = new Vector(0, scroll.Offset.Y);
                syncing = false;
            };
            Add(panes, new Border { Child = pane, BorderBrush = Hairline, BorderThickness = new Thickness(side == 0 ? 0 : 1, 0, 0, 0) }, 0, side);
        }
        Add(root, panes, 2);
        int longest = rows.SelectMany(r => new[] { r.Left, r.Base, r.Right }).Select(t => t?.Length ?? 0).DefaultIfEmpty(0).Max();
        var horizontal = new ScrollBar { Orientation = Orientation.Horizontal, Minimum = 0, Maximum = Math.Max(0, longest * GitlandApplication.Preferences.CodeSize * .65), SmallChange = 30, LargeChange = 200 };
        horizontal.ValueChanged += (_, _) => { foreach (var canvas in _threeCanvases) { canvas.HorizontalOffset = horizontal.Value; canvas.InvalidateVisual(); } };
        Add(root, horizontal, 3); _workspace.Content = root;
        _status.Text = "Three-way comparison · Green: changed side · Red: changed ancestor · Empty cells align insertions and deletions";
    }
    public async Task PreviewThreeWay() { _mergeDirty = false; await SetMode("threeway"); }
}
