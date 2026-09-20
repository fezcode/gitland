using System.Diagnostics;
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
    ManagementState? _management;
    RepositoryTools? _tools;
    string _repositoryTab = "History";
    bool _historyAll;
    int _historyLimit = 200;
    readonly GitHubService _github = new();
    string? _githubUser;
    string _commitDraft = "";
    IReadOnlyList<GitHubRelease> _releases = [];
    string? _releasesFor;
    string? GitHubRepository => GitHubService.RepositoryFromRemote(_management?.Remotes.FirstOrDefault(r => r.Name == "origin")?.Url ?? "");
    static TextBlock Paragraph(string text, IBrush? ink = null) => new() { Text = text, Foreground = ink ?? Muted, FontSize = 13, TextWrapping = TextWrapping.Wrap };
    static WrapPanel WrapActions(params Control[] controls) { var row = new WrapPanel { Orientation = Orientation.Horizontal }; foreach (var control in controls) { control.Margin = new Thickness(0, 0, 8, 6); row.Children.Add(control); } return row; }
    static Control Section(string title, params Control[] content) {
        var body = Col(Text(title, 12, strong: true)); body.Spacing = 12;
        foreach (var item in content) body.Children.Add(item);
        return new Border { Child = body, Padding = new Thickness(18), BorderBrush = Hairline, BorderThickness = new Thickness(0, 0, 0, 1) };
    }
    static Control Field(string label, Control field, string? hint = null) {
        var stack = Col(Text(label, 12, Muted), field); stack.Spacing = 6;
        if (hint != null) stack.Children.Add(Paragraph(hint, Faint)); return stack;
    }
    static string Short(string? sha) => string.IsNullOrEmpty(sha) ? "No commits yet" : sha[..Math.Min(8, sha.Length)];
    async Task LoadManagement() {
        if (_repo != null) {
            _management = await _repo.ReadManagementAsync(_historyAll, _historyLimit); _state = await _repo.ReadStateAsync();
            _tools = await _repo.ReadToolsAsync();
            _branch.Text = _state.Branch;
        } else _management = _fixture?.Management ?? new([], [], [], [], "", null, false);
        RenderNavigation(); RenderContext(); RenderFiles();
        if (_mode == "github") RenderGitHub(); else if (_mode == "repository") RenderManagement(); else await Refresh();
    }
    async Task PushBranch(string remote, bool force = false) {
        if (_repo == null || _management?.Head == null) return;
        var url = _management.Remotes.Single(r => r.Name == remote).Url;
        string summary = $"Repository: {_repo.Root}\nRemote: {remote} · {url}\nBranch: {_state.Branch}\nCommit: {_management.Head}\n\n";
        string detail = force
            ? summary + "This replaces the remote branch with your local one, which is what an amend or rebase needs. Gitland uses --force-with-lease, so the push is refused if the remote gained commits since your last fetch."
            : summary + "This sends committed history on this branch to the remote.";
        if (!await ReviewAction(force ? "Force push current branch" : "Push current branch", detail, force ? "Force push" : "Push branch")) return;
        await _repo.PushBranchAsync(remote, _state.Branch, _management.Head, force); await LoadManagement(); _status.Text = force ? "Branch force-pushed." : "Branch pushed.";
    }
    async Task PushTag(GitTag tag) {
        if (_repo == null) return;
        var origin = _management!.Remotes.FirstOrDefault(r => r.Name == "origin") ?? _management.Remotes.FirstOrDefault() ?? throw new InvalidOperationException("Add a remote before pushing a tag.");
        if (!await ReviewAction("Push tag", $"Tag: {tag.Name}\nCommit: {tag.Commit}\nRemote: {origin.Url}", "Push tag")) return;
        await _repo.PushTagAsync(origin.Name, tag.Name, tag.Commit); _status.Text = "Tag pushed: " + tag.Name;
    }
    async Task CreateRepositoryDialog() {
        if (!await MayLeaveMerge()) return;
        var location = new TextBox { Watermark = "Full path to a new or existing folder" };
        var browse = Button("Choose folder", async () => { var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Repository location" }); if (folders.FirstOrDefault()?.TryGetLocalPath() is { } path) location.Text = path; }, "folder");
        var branch = new TextBox { Text = "main" };
        var readme = new CheckBox { Content = "Create README.md if missing", IsChecked = true };
        var ignore = new ComboBox { ItemsSource = new[] { "none", "dotnet", "node" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        await FormDialog("Create repository", [Field("Location", location), browse, Field("Initial branch", branch), Field(".gitignore template", ignore), readme, Paragraph("Existing files are preserved. The new repository starts with an empty index so you can review what to commit.")], "Create repository", async () => {
            var repo = await GitRepository.InitializeAsync(new(location.Text ?? "", branch.Text ?? "main", readme.IsChecked == true, (string?)ignore.SelectedItem ?? "none"));
            await OpenRepository(repo.Root); await SetMode("repository");
        });
    }
    async Task CreateBranchDialog() {
        if (_repo == null) return;
        var name = new TextBox { Watermark = "feature/my-change" }; var start = new TextBox { Text = "HEAD" }; var checkout = new CheckBox { Content = "Switch to the new branch", IsChecked = false };
        await FormDialog("Create branch", [Field("Branch name", name), Field("Start from revision", start), checkout], "Create branch", async () => { await _repo.CreateBranchAsync(name.Text ?? "", start.Text ?? "HEAD", checkout.IsChecked == true); await LoadManagement(); });
    }
    async Task TagDialog(string commit) {
        if (_repo == null) return;
        var name = new TextBox { Watermark = "v1.0.0" }; var note = new TextBox { Watermark = "What does this version mark?", AcceptsReturn = true, MinHeight = 90, TextWrapping = TextWrapping.Wrap };
        await FormDialog("Tag commit", [Field("Commit", Paragraph(commit, Accent)), Field("Tag name", name), Field("Annotation", note), Paragraph("Creates a local annotated tag. You can push it from Repository → Tags.")], "Create local tag", async () => { await _repo.CreateTagAsync(name.Text ?? "", commit, note.Text ?? ""); await LoadManagement(); });
    }
    async Task FormDialog(string title, IEnumerable<Control> fields, string action, Func<Task> submit) {
        var dialog = new Window { Title = title, Width = 630, MaxHeight = 850, SizeToContent = SizeToContent.Height, CanResize = false, Background = Ground, Foreground = Ink, FontFamily = Sans, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var body = new StackPanel { Spacing = 18, Margin = new Thickness(26) }; body.Children.Add(Text(title, 21, strong: true));
        foreach (var field in fields) body.Children.Add(field);
        var error = Paragraph("", Red); error.IsVisible = false; body.Children.Add(error);
        bool submitting = false;
        var cancel = Button("Cancel", () => dialog.Close());
        var apply = Button(action, () => { }, primary: true);
        apply.Click += async (_, _) => {
            if (submitting) return; submitting = true; apply.IsEnabled = cancel.IsEnabled = false; error.IsVisible = false;
            try { await submit(); submitting = false; dialog.Close(); }
            catch (Exception e) { error.Text = e.Message; error.IsVisible = true; }
            finally { submitting = false; apply.IsEnabled = cancel.IsEnabled = true; }
        };
        dialog.Closing += (_, e) => { if (submitting) e.Cancel = true; };
        var footer = Row(cancel, apply); footer.HorizontalAlignment = HorizontalAlignment.Right; footer.Margin = new Thickness(0, 6, 0, 0);
        body.Children.Add(footer); dialog.Content = new ScrollViewer { Content = body, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        await dialog.ShowDialog(this);
    }
    /// <summary>Shows fixed-width rows - blame and file history are columnar and must stay aligned.</summary>
    async Task ShowListDialog(string title, IReadOnlyList<string> rows) {
        var dialog = new Window { Title = title, Width = 980, Height = 640, CanResize = true, Background = Ground, Foreground = Ink, FontFamily = Sans, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var list = new SelectableTextBlock {
            Text = string.Join(Environment.NewLine, rows), FontFamily = Mono, FontSize = 12,
            Foreground = Ink, Margin = new Thickness(20), TextWrapping = TextWrapping.NoWrap,
        };
        var close = Button("Close", () => dialog.Close(), primary: true);
        close.HorizontalAlignment = HorizontalAlignment.Right; close.Margin = new Thickness(0, 0, 20, 16);
        var body = new DockPanel();
        DockPanel.SetDock(close, Dock.Bottom); body.Children.Add(close);
        body.Children.Add(new ScrollViewer { Content = list, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        dialog.Content = body;
        await dialog.ShowDialog(this);
    }
    async Task<bool> ReviewAction(string title, string details, string action) {
        var dialog = MakeDialog(title, details);
        ((StackPanel)dialog.Content!).Children.Add(Row(Button("Cancel", () => dialog.Close(false)), Button(action, () => dialog.Close(true), primary: true)));
        return await dialog.ShowDialog<bool>(this);
    }
    async Task PublishDialog() {
        if (_repo == null) return;
        _management = await _repo.ReadManagementAsync(); _state = await _repo.ReadStateAsync();
        _githubUser ??= await _github.ConnectedUserAsync(_repo.Root);
        var owner = new TextBox { Text = _githubUser }; var name = new TextBox { Text = System.IO.Path.GetFileName(_repo.Root) }; var description = new TextBox { Watermark = "Repository description" };
        var visibility = new ComboBox { ItemsSource = new[] { "Private", "Public" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var push = new CheckBox { Content = "Push the current branch after creation", IsChecked = _management.Head != null, IsEnabled = _management.Head != null };
        var head = _management.Head; var branch = _state.Branch;
        await FormDialog("Publish repository to GitHub", [Paragraph(_repo.Root), Field("Owner or organization", owner), Field("Repository name", name), Field("Description", description), Field("Visibility", visibility), push, Paragraph($"Branch: {branch}\nCommit: {head ?? "No commits yet"}\nOnly committed history is pushed. Uncommitted files remain local.", Accent)], "Publish repository", async () => {
            var result = await _github.PublishAsync(_repo, new(owner.Text ?? "", name.Text ?? "", description.Text ?? "", visibility.SelectedIndex == 0, push.IsChecked == true, branch, head));
            await LoadManagement(); _status.Text = "Repository created: " + result.Url;
        });
    }
    async Task ReleaseDialog() {
        if (_repo == null || GitHubRepository == null) return;
        string repository = GitHubRepository;
        var tags = _management!.Tags;
        var tag = new ComboBox { ItemsSource = tags.Select(t => t.Name).ToArray(), SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var title = new TextBox { Watermark = "Release title", Text = tags[0].Name };
        var notes = new TextBox { Watermark = "What's new, fixes, and upgrade notes…", AcceptsReturn = true, MinHeight = 140, TextWrapping = TextWrapping.Wrap };
        var draft = new CheckBox { Content = "Save as draft", IsChecked = true }; var prerelease = new CheckBox { Content = "Mark as pre-release" }; var generate = new CheckBox { Content = "Include GitHub-generated release notes" };
        var target = Text(tags[0].Commit, 12, Accent); tag.SelectionChanged += (_, _) => target.Text = tags.FirstOrDefault(t => t.Name == (string?)tag.SelectedItem)?.Commit;
        await FormDialog("Create GitHub release", [Paragraph(repository, Accent), Field("Existing tag", tag), Field("Tagged commit", target), Field("Title", title), Field("Release notes", notes), Row(draft, prerelease), generate, Paragraph("The tag must already exist on GitHub and match this commit. Clear Save as draft to publish the release immediately.")], "Create release", async () => {
            var chosen = tags.Single(t => t.Name == (string)tag.SelectedItem!);
            string url = await _github.CreateReleaseAsync(_repo, new(repository, chosen.Name, chosen.Commit, title.Text ?? "", notes.Text ?? "", draft.IsChecked == true, prerelease.IsChecked == true, generate.IsChecked == true));
            _status.Text = (draft.IsChecked == true ? "Draft release created: " : "Release published: ") + url;
            try { await ReloadReleases(); } catch (Exception e) { _status.Text += " · Refresh failed: " + e.Message; }
            RenderGitHub();
        });
    }
    async Task ReloadReleases() {
        if (_repo == null || GitHubRepository is not { } repository) return;
        var releases = await _github.ListReleasesAsync(_repo.Root, repository); _releases = releases; _releasesFor = repository;
    }
    static void OpenUrl(string url) { if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == "github.com") Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
    public async Task PreviewManagement() { _mergeDirty = false; await SetMode("repository"); }
    public async Task PreviewGitHub() { _mergeDirty = false; await SetMode("github"); }
    public async Task PreviewPublishForm() { _githubUser = "preview-user"; await PublishDialog(); }
}
