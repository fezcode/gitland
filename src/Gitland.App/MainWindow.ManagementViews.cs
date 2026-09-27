using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Input;
using Gitland.Core;
using static Gitland.App.Palette;

namespace Gitland.App;

public sealed partial class MainWindow {
    CommitDiffView? _historyReview;
    string? _historySelectedHash, _historySelectedRoot;
    GitCommit? SelectedHistoryCommit => _management?.Commits.FirstOrDefault(c => _historySelectedRoot == _repo?.Root && c.Hash == _historySelectedHash) ?? _management?.Commits.FirstOrDefault();
    readonly Dictionary<string, Button> _historyButtons = new();
    readonly Dictionary<string, HistoryGraphCell> _historyTracks = new();
    void RenderManagement() {
        if (_management == null) return;
        if (_repositoryTab != "History") { RenderRepositoryTools(); return; }
        var model = _management;
        var page = new Grid { Name = "HistoryWorkspace", RowDefinitions = new RowDefinitions(".8*,7,1.4*") };
        page.RowDefinitions[0].MinHeight = 120; page.RowDefinitions[2].MinHeight = 180;
        _historyButtons.Clear(); _historyTracks.Clear();
        var history = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*") };
        var search = new TextBox { Name = "HistorySearch", Watermark = "Search commits, authors, or refs", MinWidth = 100, FontSize = 11 };
        var allBranches = new CheckBox { Content = "All branches", IsChecked = _historyAll, FontSize = 11 };
        allBranches.IsCheckedChanged += (_, _) => Run(async () => { _historyAll = allBranches.IsChecked == true; await LoadManagement(); });
        var repositoryActions = Button("Repository actions", () => { }, "down");
        var historyHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12, Margin = new Thickness(12, 8) };
        historyHeader.Children.Add(search); Add(historyHeader, Row(allBranches, repositoryActions), 0, 1); Add(history, historyHeader, 0);
        var columnTitles = new Grid { ColumnDefinitions = new ColumnDefinitions("*,140,84,34"), ColumnSpacing = 12, Margin = new Thickness(24, 2, 18, 8) };
        var historyCount = Text($"{model.Commits.Count} commits", 10, Faint); columnTitles.Children.Add(historyCount);
        Add(columnTitles, Text("AUTHOR", 9, Faint), 0, 1); Add(columnTitles, Text("DATE", 9, Faint), 0, 2); Add(history, columnTitles, 1);
        var entries = new StackPanel { Spacing = 0 };
        var historyRows = new List<(Control Row, string Text)>();
        var graph = HistoryGraph.Layout(model.Commits); int graphIndex = 0;
        int graphWidth = Math.Max(20, graph.Select(r => r.Width).DefaultIfEmpty(1).Max() * HistoryGraphCell.LaneSpacing + 12);
        var graphCells = new List<HistoryGraphCell>();
        foreach (var entry in model.Commits) {
            var graphRow = graph[graphIndex++];
            var track = new HistoryGraphCell(graphRow); graphCells.Add(track); _historyTracks[entry.Hash] = track;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions($"{graphWidth},*,140,84,34"), ColumnSpacing = 12, Margin = new Thickness(18, 0) };
            row.Children.Add(track);
            var subject = Text(entry.Subject, 12, strong: true); subject.TextTrimming = TextTrimming.CharacterEllipsis;
            var references = Row(Text(entry.ShortHash, 10, Faint)); references.Spacing = 7; references.ClipToBounds = true;
            foreach (string reference in entry.Decorations.Split(", ", StringSplitOptions.RemoveEmptyEntries).Take(3)) {
                bool tag = reference.StartsWith("tag: "); string caption = reference.Replace("HEAD -> ", "HEAD · ").Replace("HEAD → ", "HEAD · ").Replace("tag: ", "");
                var badge = new Border { Child = Row(Icon(tag ? "tag" : "branch", tag ? Amber : HistoryGraphCell.LaneBrush(graphRow.Lane), 10), Text(caption, 10, tag ? Amber : HistoryGraphCell.LaneBrush(graphRow.Lane))), Background = Raised, CornerRadius = new CornerRadius(3), Padding = new Thickness(5, 2), MaxWidth = 220, ClipToBounds = true };
                ToolTip.SetTip(badge, reference); references.Children.Add(badge);
            }
            if (track.IsMerge) references.Children.Add(Text("merge", 10, Faint));
            var labels = Col(subject, references); labels.Spacing = 4; labels.ClipToBounds = true; labels.VerticalAlignment = VerticalAlignment.Center; Add(row, labels, 0, 1);
            var author = Text(entry.Author, 11, Muted); author.TextTrimming = TextTrimming.CharacterEllipsis; ToolTip.SetTip(author, entry.Author); Add(row, author, 0, 2);
            Add(row, Text(entry.Date[..Math.Min(10, entry.Date.Length)], 10, Faint), 0, 3);
            var inspect = Button("Inspect commit " + entry.ShortHash, async () => await InspectCommit(entry)); inspect.Content = row;
            inspect.Classes.Add("selection-item"); inspect.Classes.Add("history-row"); _historyButtons[entry.Hash] = inspect;
            inspect.Background = entry.Hash == _historySelectedHash ? SelectedSurface : Brushes.Transparent;
            inspect.BorderThickness = new Thickness(0); inspect.CornerRadius = new CornerRadius(0); inspect.Padding = new Thickness(0);
            inspect.HorizontalAlignment = HorizontalAlignment.Stretch; inspect.HorizontalContentAlignment = HorizontalAlignment.Stretch; inspect.VerticalContentAlignment = VerticalAlignment.Stretch; inspect.Height = 54; inspect.IsEnabled = _repo != null;
            ToolTip.SetTip(inspect, entry.Subject + "\n" + entry.Hash);
            inspect.PointerEntered += (_, _) => { if (entry.Hash != _historySelectedHash) track.Emphasis = .45; };
            inspect.PointerExited += (_, _) => track.Emphasis = entry.Hash == _historySelectedHash ? 1 : 0;
            var item = new Grid { Height = 54 }; item.Children.Add(inspect);
            var tagButton = IconButton("Tag commit", () => Run(() => TagDialog(entry.Hash)), "tag"); tagButton.IsEnabled = _repo != null; tagButton.HorizontalAlignment = HorizontalAlignment.Right; tagButton.Margin = new Thickness(0, 0, 18, 0); tagButton.Opacity = .55; item.Children.Add(tagButton);
            item.PointerEntered += (_, _) => tagButton.Opacity = 1; item.PointerExited += (_, _) => tagButton.Opacity = .55;
            entries.Children.Add(item); historyRows.Add((item, entry.Subject + " " + entry.Author + " " + entry.Hash + " " + entry.Decorations));

        }
        if (model.Commits.Count == 0) { var empty = Paragraph("No commits yet. Stage your changes and write a commit message to get started."); empty.Margin = new Thickness(20); entries.Children.Add(empty); }
        if (model.Commits.Count >= _historyLimit && _historyLimit < 2000) entries.Children.Add(RepoAction("Load more commits", async () => { _historyLimit += 200; await LoadManagement(); }));
        search.TextChanged += (_, _) => { foreach (var item in historyRows) item.Row.IsVisible = item.Text.Contains(search.Text ?? "", StringComparison.OrdinalIgnoreCase); foreach (var cell in graphCells) cell.IsVisible = string.IsNullOrEmpty(search.Text); historyCount.Text = $"{historyRows.Count(r => r.Row.IsVisible)} of {model.Commits.Count} commits"; };
        entries.KeyDown += async (_, e) => {
            if (e.KeyModifiers != KeyModifiers.None || e.Key is not (Key.Up or Key.Down or Key.Home or Key.End)) return;
            var visible = model.Commits.Where(c => _historyButtons[c.Hash].Parent is Control parent && parent.IsVisible).ToArray(); if (visible.Length == 0) return;
            int current = Array.FindIndex(visible, c => c.Hash == _historySelectedHash);
            int target = e.Key == Key.Home ? 0 : e.Key == Key.End ? visible.Length - 1 : Math.Clamp(current + (e.Key == Key.Down ? 1 : -1), 0, visible.Length - 1);
            e.Handled = true; var commit = visible[target]; _historyButtons[commit.Hash].Focus(); _historyButtons[commit.Hash].BringIntoView(); await InspectCommit(commit);
        };
        Add(history, new ScrollViewer { Content = entries }, 2); page.Children.Add(history);

        var inspector = new StackPanel();
        var writeCommit = Button("Write a commit", () => Run(async () => { await SetMode("changes"); _commitSummary.Focus(); }), "check", true);
        inspector.Children.Add(Section("Next commit", Paragraph("Review your files, stage the changes you want, and write a message in Working changes.", Faint), writeCommit));
        var remote = new ComboBox { ItemsSource = model.Remotes.Select(r => r.Name).ToArray(), SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var fetch = Button("Fetch", () => Run(async () => { await _repo!.FetchAsync((string)remote.SelectedItem!); await LoadManagement(); _status.Text = "Fetched remote updates."; }), "down");
        var pull = Button("Pull · fast-forward", () => Run(async () => { await _repo!.PullAsync(); await LoadManagement(); _status.Text = "Branch updated."; }));
        var push = Button("Push current branch", () => Run(() => PushBranch((string)remote.SelectedItem!)), "up");
        fetch.IsEnabled = pull.IsEnabled = _repo != null && model.Remotes.Count > 0; push.IsEnabled = fetch.IsEnabled && model.Head != null;
        var syncFields = new List<Control>();
        if (model.Remotes.Count > 0) { syncFields.Add(remote); syncFields.Add(Paragraph(model.Remotes.First().Url, Faint)); }
        else syncFields.Add(Paragraph("No remote configured. Publish this repository from GitHub & releases.", Faint));
        syncFields.Add(WrapActions(fetch, pull, push)); inspector.Children.Add(Section("Remotes", syncFields.ToArray()));
        var branches = new StackPanel { Spacing = 4 };
        foreach (var branch in model.Branches) {
            if (branch.Current) { var current = Row(Icon("branch", Accent, 13), Text(branch.Name, 12), Badge("current")); current.Margin = new Thickness(0, 5); branches.Children.Add(current); }
            else { var select = Button(branch.Name, () => Run(async () => { await _repo!.SwitchBranchAsync(branch.Name); await LoadManagement(); }), "branch"); select.Classes.Add("quiet"); select.IsEnabled = _repo != null; branches.Children.Add(select); }
        }
        if (model.Branches.Count == 0) branches.Children.Add(Paragraph(_state.Branch + " · awaiting first commit", Faint));
        inspector.Children.Add(Section("Branches", branches));
        var tags = new StackPanel { Spacing = 16 };
        foreach (var tag in model.Tags) {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") }; row.Children.Add(Col(Row(Icon("tag", Faint, 13), Text(tag.Name, 12)), Text(Short(tag.Commit), 10, Faint)));
            var publish = IconButton("Push tag", () => Run(() => PushTag(tag)), "up"); publish.IsEnabled = _repo != null && model.Remotes.Any(r => r.Name == "origin"); Add(row, publish, 0, 1); tags.Children.Add(row);
        }
        if (model.Tags.Count == 0) tags.Children.Add(Paragraph("Use the tag button beside a commit to mark a version.", Faint));
        inspector.Children.Add(Section("Tags", tags));
        var inspectorBorder = new Border { BorderBrush = Hairline, BorderThickness = new Thickness(1, 0, 0, 0), Background = Bar, Child = new ScrollViewer { Content = inspector } };
        var flyout = new Flyout { Content = inspectorBorder }; inspectorBorder.Width = 320; inspectorBorder.MaxHeight = 520;
        repositoryActions.Click += (_, _) => flyout.ShowAt(repositoryActions);
        // Keep the common commit action directly available without opening the repository menu.
        var quickCommit = Button("Write a commit", () => Run(async () => { await SetMode("changes"); _commitSummary.Focus(); }), "plus");
        ((StackPanel)historyHeader.Children[1]).Children.Add(quickCommit);
        Add(page, new GridSplitter { ResizeDirection = GridResizeDirection.Rows, ResizeBehavior = GridResizeBehavior.PreviousAndNext, Background = Hairline, HorizontalAlignment = HorizontalAlignment.Stretch, Cursor = new Cursor(StandardCursorType.SizeNorthSouth) }, 1);
        _historyReview = _repo == null ? null : new CommitDiffView(_repo, HistoryCommitActions);
        Add(page, _historyReview is null ? new TextBlock { Text = "Select a commit in a repository to review its changes.", Foreground = Muted, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } : _historyReview, 2);
        RepositoryPage(page);
    }
    void RenderGitHub() {
        var origin = _management?.Remotes.FirstOrDefault(r => r.Name == "origin"); string? full = GitHubRepository;
        if (_releasesFor != full) _releases = [];
        var page = new StackPanel { Spacing = 0, Margin = new Thickness(30, 24), MaxWidth = 1040, HorizontalAlignment = HorizontalAlignment.Stretch };
        var intro = Col(Text("GitHub", 23, strong: true), Text("Repository publishing and releases", 12, Faint)); intro.Spacing = 7; intro.Margin = new Thickness(0, 0, 0, 28); page.Children.Add(intro);
        var connect = Button("Check connection", () => Run(async () => { _githubUser = await _github.ConnectedUserAsync(_repo?.Root ?? Environment.CurrentDirectory); if (_repo != null && GitHubRepository != null) await ReloadReleases(); RenderGitHub(); RenderContext(); _status.Text = "Connected to GitHub as " + _githubUser; }), "refresh");
        var signin = Button("Sign in with GitHub", () => Run(async () => { var start = new ProcessStartInfo("gh") { UseShellExecute = true }; foreach (var arg in new[] { "auth", "login", "--hostname", "github.com", "--web", "--git-protocol", "https" }) start.ArgumentList.Add(arg); Process.Start(start); _status.Text = "Finish sign-in, then select Check connection."; await Task.CompletedTask; }), "cloud");
        var account = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 20, Margin = new Thickness(0, 18) };
        account.Children.Add(Col(Text("Account", 13, strong: true), Text(_githubUser == null ? "Connect using GitHub CLI" : "Signed in as " + _githubUser, 12, Faint)));
        Add(account, WrapActions(signin, connect), 0, 1); page.Children.Add(Rule(account));
        var repo = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 24, Margin = new Thickness(0, 22) };
        var repoInfo = Col(Text(full ?? "Repository", 13, strong: true), Paragraph(origin?.Url ?? "This workspace has not been published to GitHub.", Faint)); repoInfo.Spacing = 8; repo.Children.Add(repoInfo);
        Control repoActions;
        if (origin == null) {
            var publish = Button("Publish repository…", () => Run(PublishDialog), "cloud", true); publish.IsEnabled = _repo != null; repoActions = publish;
            repoInfo.Children.Add(Text("New repositories default to private.", 11, Faint));
        } else {
            var push = Button("Push current branch", () => Run(() => PushBranch("origin")), "up"); push.IsEnabled = _repo != null && _management?.Head != null;
            var open = Button("Open on GitHub", () => OpenUrl("https://github.com/" + full), "cloud"); open.IsEnabled = full != null; repoActions = WrapActions(push, open);
        }
        Add(repo, repoActions, 0, 1); page.Children.Add(Rule(repo));
        var releaseHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 26, 0, 20) };
        releaseHeader.Children.Add(Text("Releases", 15, strong: true));
        var create = Button("Create release…", () => Run(ReleaseDialog), "plus", true); create.IsEnabled = _repo != null && full != null && _management!.Tags.Count > 0;
        var refresh = IconButton("Refresh releases", () => Run(async () => { await ReloadReleases(); RenderGitHub(); }), "refresh"); refresh.IsEnabled = _repo != null && full != null;
        Add(releaseHeader, Row(refresh, create), 0, 1); page.Children.Add(releaseHeader);
        foreach (var release in _releases) {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 16) };
            row.Children.Add(Col(Row(Icon("tag", Faint, 14), Text(release.Name.Length > 0 ? release.Name : release.Tag, 13), Badge(release.Draft ? "Draft" : release.Prerelease ? "Pre-release" : "Published", release.Draft ? Amber : Green)), Text(release.Tag, 11, Faint)));
            Add(row, Text(release.PublishedAt ?? "Unpublished", 11, Faint), 0, 1); page.Children.Add(Rule(row));
        }
        if (_releases.Count == 0) {
            var empty = Col(Icon("tag", Faint, 26), Text("No releases loaded", 14), Paragraph("Refresh to load releases, or create one from a tag you've pushed to GitHub.", Faint)); empty.Spacing = 13; empty.Margin = new Thickness(0, 44); empty.MaxWidth = 360; empty.HorizontalAlignment = HorizontalAlignment.Center;
            foreach (var child in empty.Children) child.HorizontalAlignment = HorizontalAlignment.Center;
            ((TextBlock)empty.Children[2]).TextAlignment = TextAlignment.Center; page.Children.Add(empty);
        }
        page.SizeChanged += (_, _) => {
            bool small = page.Bounds.Width < 800;
            account.ColumnDefinitions[1].Width = small ? new GridLength(180) : GridLength.Auto;
            repo.ColumnDefinitions[1].Width = small ? new GridLength(180) : GridLength.Auto;
        };
        _workspace.Content = new ScrollViewer { Content = page };
    }
    static Border Rule(Control content) => new() { Child = content, BorderBrush = Hairline, BorderThickness = new Thickness(0, 0, 0, 1) };
}
