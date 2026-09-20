using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Gitland.Core;
using static Gitland.App.Palette;

namespace Gitland.App;

public sealed partial class MainWindow {
    void RenderManagement() {
        if (_management == null) return;
        if (_repositoryTab != "History") { RenderRepositoryTools(); return; }
        var model = _management;
        var page = new Grid { ColumnDefinitions = new ColumnDefinitions("*,310") };
        var history = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*") };
        var historyHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(20, 20, 20, 24) };
        historyHeader.Children.Add(Col(Text("Commit history", 21, strong: true), Text(_state.Branch, 12, Muted)));
        var allBranches = new CheckBox { Content = "All branches", IsChecked = _historyAll };
        allBranches.IsCheckedChanged += (_, _) => Run(async () => { _historyAll = allBranches.IsChecked == true; await LoadManagement(); });
        Add(historyHeader, Row(allBranches, Badge(Short(model.Head))), 0, 1); Add(history, historyHeader, 0);
        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,120,30"), Margin = new Thickness(49, 0, 18, 10) };
        columns.Children.Add(Text("Commit", 11, Faint)); Add(columns, Text("Committed", 11, Faint), 0, 1); Add(history, columns, 1);
        var search = new TextBox { Watermark = "Filter history by message, author, hash, or reference", Margin = new Thickness(18, 0, 18, 12) };
        Add(history, search, 2);
        var entries = new StackPanel { Spacing = 0 };
        var historyRows = new List<(Control Row, string Text)>();
        var graph = HistoryGraph.Layout(model.Commits); int graphIndex = 0;
        int graphWidth = Math.Max(20, graph.Select(r => r.Width).DefaultIfEmpty(1).Max() * 14 + 10);
        var graphCells = new List<HistoryGraphCell>();
        foreach (var entry in model.Commits) {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions($"{graphWidth},*,120,30"), ColumnSpacing = 10, Margin = new Thickness(18, 0) };
            var track = new HistoryGraphCell(graph[graphIndex++]); graphCells.Add(track); row.Children.Add(track);
            var labels = Col(Text(entry.Subject, 12), Row(Text(entry.ShortHash, 10, Faint), Text(entry.Author, 10, Faint))); labels.Spacing = 7;
            if (entry.Decorations.Length > 0) labels.Children.Add(Text(entry.Decorations, 10, Muted));
            var inspect = Button("Inspect commit " + entry.ShortHash, () => Run(() => InspectCommit(entry))); inspect.Content = labels; inspect.Classes.Add("quiet"); inspect.HorizontalContentAlignment = HorizontalAlignment.Left; inspect.Padding = new Thickness(0, 14); inspect.IsEnabled = _repo != null; Add(row, inspect, 0, 1);
            Add(row, Text(entry.Date[..Math.Min(10, entry.Date.Length)], 11, Faint), 0, 2);
            var tag = IconButton("Tag commit", () => Run(() => TagDialog(entry.Hash)), "tag"); tag.IsEnabled = _repo != null; Add(row, tag, 0, 3);
            var item = new Border { Child = row, BorderBrush = Hairline, BorderThickness = new Thickness(0, 0, 0, 1) };
            entries.Children.Add(item); historyRows.Add((item, entry.Subject + " " + entry.Author + " " + entry.Hash + " " + entry.Decorations));
        }
        if (model.Commits.Count == 0) { var empty = Paragraph("No commits yet. Stage your changes and write a commit message to get started."); empty.Margin = new Thickness(20); entries.Children.Add(empty); }
        if (model.Commits.Count >= _historyLimit && _historyLimit < 2000) entries.Children.Add(RepoAction("Load more commits", async () => { _historyLimit += 200; await LoadManagement(); }));
        search.TextChanged += (_, _) => { foreach (var item in historyRows) item.Row.IsVisible = item.Text.Contains(search.Text ?? "", StringComparison.OrdinalIgnoreCase); foreach (var cell in graphCells) cell.IsVisible = string.IsNullOrEmpty(search.Text); };
        Add(history, new ScrollViewer { Content = entries }, 3); page.Children.Add(history);

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
        Add(page, inspectorBorder, 0, 1); RepositoryPage(page);
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
