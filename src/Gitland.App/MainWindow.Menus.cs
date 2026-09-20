using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Gitland.Core;

namespace Gitland.App;

public sealed partial class MainWindow {
    HoswlClient? _hoswl;
    readonly Dictionary<string, (Func<Task> Action, bool Enabled)> _menuActions = new();
    void InitializeHoswl() {
        _hoswl = new HoswlClient(typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "0.6.1");
        _hoswl.Clicked += id => Dispatcher.UIThread.Post(() => DispatchMenu(id));
        _hoswl.ConnectionChanged += _ => Dispatcher.UIThread.Post(UpdateIntegrationStatus);
        Closed += async (_, _) => await _hoswl.DisposeAsync();
        UpdateMenus();
    }
    IReadOnlyList<HoswlItem> BuildMenus() {
        _menuActions.Clear();
        HoswlItem Item(string id, string label, Func<Task> action, string? key = null, bool enabled = true, bool? check = null) {
            enabled &= !_busy && OwnedWindows.Count == 0;
            _menuActions[id] = (action, enabled); return new(id, label, key, enabled, check);
        }
        Task Do(Action action) { action(); return Task.CompletedTask; }
        HoswlItem Group(string id, string label, params HoswlItem[] items) => new(id, label, Items: items);
        var themes = Palette.Themes.Select(t => Item("theme." + t.Id, t.Name, () => Do(() => SavePreferences(GitlandApplication.Preferences with { Theme = t.Id })), check: Palette.Current.Id == t.Id)).ToArray();
        return [
            Group("file", "File", Item("file.open", "Open repository…", PickRepository, "Ctrl+O"), Item("file.clone", "Clone repository…", CloneDialog), Item("file.new", "Create repository…", CreateRepositoryDialog), Item("file.compare", "Compare local files…", CompareLocalFiles), new(Sep: true), Item("file.refresh", "Refresh", Refresh, "F5"), Item("file.close", "Close window", () => Do(Close), "Alt+F4")),
            Group("view", "View", Item("view.all", "All files", () => SetFileScope("all"), check: _fileScope == "all"), Item("view.changed", "Changed files", () => SetFileScope("changed"), check: _fileScope == "changed"), Item("view.conflicts", "Conflicting files", () => SetFileScope("conflicts"), check: _fileScope == "conflicts"), new(Sep: true), Item("view.split", "Side by side", () => Do(() => SetUnified(false)), check: !_unified), Item("view.unified", "Unified diff", () => Do(() => SetUnified(true)), check: _unified), Group("view.themes", "Color theme", themes), Item("view.fullscreen", "Full screen", () => Do(ToggleFullscreen), "F11", check: WindowState == WindowState.FullScreen)),
            Group("git", "Git", Item("git.history", "Repository & history", () => SetMode("repository")), Item("git.compare", "Compare revisions", () => SetMode("compare")), Item("git.threeway", "Three-way comparison", () => SetMode("threeway")), Item("git.conflicts", "Resolve conflicts", () => SetMode("merge"), enabled: _state.Changes.Any(c => c.IsConflict)), new(Sep: true), Item("git.stage", _comparison?.RightLabel == "Index · staged" ? "Unstage selected file" : "Stage selected file", StageSelected, enabled: _repo != null && _mode == "changes" && _selected is { IsChanged: true, IsConflict: false } && _stageButton.IsEnabled), Item("git.message", "Commit message window…", ShowCommitWindow), Item("git.commit", "Commit staged changes", CommitCurrent, "Ctrl+Enter", enabled: _mode == "changes" && CanCommit), Item("git.branch", "New branch…", CreateBranchDialog, enabled: _repo != null), Item("git.github", "GitHub & releases", () => SetMode("github"))),
            Group("app", "Settings", Item("app.settings", "Settings…", ShowSettings, "Ctrl+,"))
        ];
    }
    void UpdateMenus() { UpdateCommitComposer(); _hoswl?.Update(BuildMenus(), GitlandApplication.Preferences.HoswlEnabled); }
    void DispatchMenu(string id) {
        BuildMenus();
        if (!_menuActions.TryGetValue(id, out var item) || !item.Enabled) return;
        if (id == "app.settings") { _ = ShowSettings(); return; }
        if (id == "file.close") { Close(); return; }
        Run(item.Action);
    }
    void ShowAppMenu() {
        var button = this.GetVisualDescendants().OfType<Button>().First(b => AutomationProperties.GetName(b) == "Gitland menu");
        Control ConvertItem(HoswlItem item) {
            if (item.Sep == true) return new Separator();
            var menu = new MenuItem { Header = (item.Check == true ? "✓  " : "") + item.Label, IsEnabled = item.Enabled != false };
            if (item.Items != null) menu.ItemsSource = item.Items.Select(ConvertItem).ToArray();
            else menu.Click += (_, _) => { if (item.Id != null) DispatchMenu(item.Id); };
            return menu;
        }
        var context = new ContextMenu { ItemsSource = BuildMenus().Select(ConvertItem).ToArray() }; button.ContextMenu = context; context.Open(button);
    }
}
