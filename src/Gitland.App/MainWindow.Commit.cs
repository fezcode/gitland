using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using static Gitland.App.Palette;

namespace Gitland.App;

public sealed partial class MainWindow {
    readonly Dictionary<string, string> _commitDrafts = new(StringComparer.OrdinalIgnoreCase);
    readonly TextBox _commitSummary = new() { Name = "CommitSummary", Watermark = "Summary of your changes", TextWrapping = TextWrapping.Wrap, Height = 56 };
    readonly TextBox _commitBody = new() { Name = "CommitDescription", Watermark = "Description (optional)", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 76 };
    readonly TextBlock _commitHint = Paragraph("Stage changes to prepare a commit.", Faint);
    readonly TextBlock _commitCounter = Text("0 / 72", 10, Faint);
    readonly TextBlock _commitBranch = Text("", 11, Muted);
    readonly TextBlock _commitReady = Text("", 10, Green);
    readonly StackPanel _changeFilters = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    Border? _commitPanel;
    Button? _commitButton, _stageAllButton;
    StackPanel? _commitForm;
    Control? _commitDestination;
    Button? _descriptionToggle;
    bool _expandCommitDescription;
    string _commitFeedback = "";
    bool _commitFailed, _settingDraft;
    string DraftKey => _repo?.Root ?? "<no-repository>";
    bool CanCommit => _repo != null && !_state.Changes.Any(c => c.IsConflict) && _management is { IndexTree.Length: > 0 } &&
        (_state.Changes.Any(c => c.IsStaged) || _management.MergeInProgress) && !string.IsNullOrWhiteSpace(_commitSummary.Text);

    Control BuildCommitComposer() {
        AutomationProperties.SetName(_commitSummary, "Commit summary");
        AutomationProperties.SetName(_commitBody, "Commit description");
        _commitSummary.TextChanged += (_, _) => CaptureCommitDraft();
        _commitBody.TextChanged += (_, _) => CaptureCommitDraft();
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        heading.Children.Add(Text("New commit", 17, strong: true));
        var expand = IconButton("Open commit editor in window", () => Run(ShowCommitWindow), "expand");
        Add(heading, Row(_commitReady, expand), 0, 1);
        var label = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        label.Children.Add(Text("MESSAGE", 10, Faint, true)); Add(label, _commitCounter, 0, 1);
        _commitButton = Button("Commit staged", () => Run(CommitCurrent), "check", true);
        _commitButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        _commitButton.HorizontalContentAlignment = HorizontalAlignment.Center;
        _commitButton.Height = 36;
        ToolTip.SetTip(_commitButton, "Commit the staged changes · Ctrl+Enter");
        var destination = _commitDestination = Row(Icon("branch", Faint, 12), _commitBranch);
        _commitBranch.MaxWidth = 205;
        _commitHint.FontSize = 11; _commitHint.LineHeight = 16;
        _descriptionToggle = Button("Toggle commit description", () => { _expandCommitDescription = !_expandCommitDescription; UpdateCommitDensity(); });
        _descriptionToggle.Classes.Add("quiet"); _descriptionToggle.Padding = new Thickness(0); _descriptionToggle.MinHeight = 20; _descriptionToggle.HorizontalAlignment = HorizontalAlignment.Left;
        var form = _commitForm = Col(heading, destination, label, _commitSummary, _descriptionToggle, _commitBody, _commitButton, _commitHint);
        form.Spacing = 9;
        _commitPanel = new Border { Name = "CommitComposer", Child = form, Background = Raised, Padding = new Thickness(15), BorderBrush = Hairline, BorderThickness = new Thickness(0, 1, 0, 0) };
        return _commitPanel;
    }
    void UpdateCommitDensity() {
        if (_commitForm == null || _descriptionToggle == null || _commitDestination == null) return;
        bool compact = Bounds.Height < 760;
        _commitForm.Spacing = compact ? 7 : 9;
        _commitSummary.Height = compact ? 38 : 56;
        _commitDestination.IsVisible = !compact;
        _commitBody.Height = compact ? 54 : 76;
        _commitBody.IsVisible = !compact || _expandCommitDescription;
        _descriptionToggle.IsVisible = compact;
        _descriptionToggle.Content = Text(_expandCommitDescription ? "Hide description ↑" : string.IsNullOrEmpty(_commitBody.Text) ? "+ Add description" : "Edit description ↓", 11, Muted);
    }
    void CaptureCommitDraft() {
        if (_settingDraft) return;
        _commitDraft = (_commitSummary.Text ?? "").Trim() + (string.IsNullOrWhiteSpace(_commitBody.Text) ? "" : "\n\n" + _commitBody.Text.TrimEnd());
        _commitDrafts[DraftKey] = _commitDraft;
        _commitFeedback = ""; _commitFailed = false; UpdateCommitComposer();
        UpdateCommitDensity();
    }
    void RestoreCommitDraft(string draft) {
        _settingDraft = true;
        var parts = draft.Split('\n', 2);
        _commitDraft = draft; _commitSummary.Text = parts[0]; _commitBody.Text = parts.Length > 1 ? parts[1].TrimStart('\r', '\n') : "";
        _settingDraft = false; _commitDrafts[DraftKey] = draft;
        _commitFeedback = ""; _commitFailed = false; UpdateCommitComposer();
        UpdateCommitDensity();
    }
    void UpdateCommitComposer() {
        if (_commitPanel == null || _commitButton == null) return;
        _commitPanel.IsVisible = _mode == "changes" && (_repo != null || _fixture != null);
        int staged = _state.Changes.Count(c => c.IsStaged && !c.IsConflict), conflicts = _state.Changes.Count(c => c.IsConflict);
        _commitReady.Text = $"{staged} STAGED"; _commitBranch.Text = _state.Branch;
        _commitCounter.Text = $"{_commitSummary.Text?.Length ?? 0} / 72";
        _commitCounter.Foreground = (_commitSummary.Text?.Length ?? 0) > 72 ? Amber : Faint;
        string label = _management?.MergeInProgress == true ? "Commit merge" : "Commit staged";
        _commitButton.Content = Row(Icon("check", OnPrimary, 14), Text(label, 12, OnPrimary, true), Text("Ctrl+↵", 10, OnPrimary));
        AutomationProperties.SetName(_commitButton, label);
        _commitButton.IsEnabled = CanCommit && !_busy && OwnedWindows.Count == 0;
        _commitSummary.IsReadOnly = _commitBody.IsReadOnly = _busy;
        if (_stageAllButton != null) { _stageAllButton.IsVisible = _mode == "changes"; _stageAllButton.IsEnabled = _repo != null && !_busy && conflicts == 0 && _state.Changes.Any(c => c.IsUnstaged); }
        _commitHint.Text = _commitFeedback.Length > 0 ? _commitFeedback : _repo == null ? "Open a repository to stage and commit." : conflicts > 0 ? $"Resolve {conflicts} conflicted {(conflicts == 1 ? "file" : "files")} before committing." : staged == 0 && _management?.MergeInProgress != true ? "Stage changes to prepare a commit." : "Only staged changes will be committed.";
        _commitHint.Foreground = _commitFailed ? Red : conflicts > 0 ? Amber : Faint;
    }
    async Task CommitCurrent() {
        if (!CanCommit || _management == null || _repo == null) return;
        await CommitDraft(_management.IndexTree, _management.Head);
    }
    async Task<bool> CommitDraft(string expectedTree, string? expectedHead, string? expectedBranch = null) {
        if (_repo == null || string.IsNullOrWhiteSpace(_commitSummary.Text)) return false;
        string hash;
        try { hash = await _repo.CommitAsync(_commitDraft, expectedTree, expectedHead, expectedBranch); }
        catch (Exception e) { _commitFeedback = e.Message; _commitFailed = true; _status.Text = "Commit failed · Your message has been kept."; UpdateCommitComposer(); return false; }
        RestoreCommitDraft("");
        _commitFeedback = "Committed " + Short(hash) + " · saved locally";
        await Refresh(); _status.Text = _commitFeedback; UpdateCommitComposer();
        return true;
    }
    async Task FilterChanges(string filter) {
        if (!await MayLeaveMerge()) return;
        _mode = "changes"; _fileScope = "changed"; _filter = filter;
        RenderNavigation(); RenderContext(); RenderFiles();
        if (VisibleFiles().FirstOrDefault() is { } file) await SelectFile(file);
        else { _selected = null; Empty(filter == "staged" ? "Nothing staged yet" : "No changes here", "Stage a file or an individual hunk to add it to your next commit."); }
        UpdateMenus();
    }
    void RenderChangeFilters() {
        _changeFilters.Children.Clear(); _changeFilters.IsVisible = _mode == "changes" && _fileScope == "changed";
        foreach (var (id, label, count) in new[] { ("all", "Together", _state.Changes.Count), ("unstaged", "Unstaged", _state.Changes.Count(c => c.IsUnstaged && !c.IsConflict)), ("staged", "Staged", _state.Changes.Count(c => c.IsStaged && !c.IsConflict)) }) {
            var button = Button(label, () => Run(() => FilterChanges(id)));
            button.Content = Row(Text(label, 10, _filter == id ? Ink : Faint), Text(count.ToString(), 10, id == "staged" ? Green : Faint));
            button.Classes.Add("segment"); button.Background = _filter == id ? SelectedSurface : Brushes.Transparent; button.Padding = new Thickness(5, 5);
            _changeFilters.Children.Add(button);
        }
        UpdateCommitComposer();
    }
}
