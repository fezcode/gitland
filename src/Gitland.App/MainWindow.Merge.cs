using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Gitland.Core;
using static Gitland.App.Palette;

namespace Gitland.App;

public sealed partial class MainWindow {
    MergeFile? _merge;
    bool _mergeDirty, _manualMerge, _settingResult;
    StackPanel _conflictList = new() { Spacing = 12 };
    TextBlock _mergeProgress = Text("", 12, Amber);
    MergeEditor _resultEditor = CreateResultEditor();
    static MergeEditor CreateResultEditor() => new() { FontSize = GitlandApplication.Preferences.CodeSize };
    Button? _saveMerge;
    Button? _magicButton, _undoMergeButton;
    Button? _acceptOurs, _acceptTheirs;
    MergeEditor? _oursEditor, _theirsEditor;
    bool _syncMergeScroll;
    int _activeConflict;
    readonly Stack<MergeUndo> _mergeUndo = new();
    sealed record MergeUndo(string Text, bool Manual, MergeDocument Document, Resolution[] Choices, int Active);

    async Task LoadMerge(GitChange file) {
        _merge = _repo == null ? _fixture!.Merge() : await _repo.ReadMergeAsync(file.Path);
        SmartMerge.Analyze(_merge.Document);
        _mergeDirty = false; _manualMerge = false;
        _mergeUndo.Clear(); _activeConflict = 0;
        _resultEditor.PropertyChanged -= ResultChanged;
        _resultEditor = CreateResultEditor();
        _conflictList = new StackPanel { Spacing = 12 };
        _mergeProgress = Text("", 12, Amber);
        _settingResult = true;
        _resultEditor.Text = _merge.Snapshot.Text;
        _settingResult = false;
        _resultEditor.PropertyChanged += ResultChanged;
        BuildMergeWorkspace(file);
    }
    void Choose(ConflictBlock block, Resolution resolution) {
        if (_merge == null || _manualMerge) return;
        RememberMerge(); _activeConflict = block.Id - 1;
        block.Choice = resolution; _settingResult = true;
        _resultEditor.Text = _merge.Document.Render(); _settingResult = false;
        _mergeDirty = true; RenderConflicts(); FocusConflict();
    }
    void ResultChanged(object? sender, AvaloniaPropertyChangedEventArgs e) {
        if (_settingResult || e.Property != TextBox.TextProperty) return;
        _mergeDirty = true; _manualMerge = _merge != null && _resultEditor.Text != _merge.Document.Render(); RenderConflicts();
    }
    void AcceptActive(Resolution choice) { if (_merge?.Document.Conflicts.Count > 0) Choose(_merge.Document.Conflicts[_activeConflict], choice); }
    void RememberMerge() { if (_merge != null) _mergeUndo.Push(new(_resultEditor.Text ?? "", _manualMerge, _merge.Document, _merge.Document.Conflicts.Select(c => c.Choice).ToArray(), _activeConflict)); }
    void UndoMerge() {
        if (_merge == null || _manualMerge || !_mergeUndo.TryPop(out var previous)) return;
        _merge = _merge with { Document = previous.Document }; _activeConflict = previous.Active;
        for (int i = 0; i < previous.Choices.Length; i++) _merge.Document.Conflicts[i].Choice = previous.Choices[i];
        _settingResult = true; _resultEditor.Text = previous.Text; _settingResult = false; _manualMerge = previous.Manual; _mergeDirty = true;
        RenderConflicts(); FocusConflict();
    }
    void NavigateConflict(int direction) {
        if (_merge == null || _merge.Document.Conflicts.Count == 0) return;
        _activeConflict = (_activeConflict + direction + _merge.Document.Conflicts.Count) % _merge.Document.Conflicts.Count; RenderConflicts(); FocusConflict();
    }
    void FocusConflict() {
        if (_merge == null || _merge.Document.Conflicts.Count == 0) return;
        var block = _merge.Document.Conflicts[_activeConflict]; _syncMergeScroll = true;
        // Navigate without replacing the semantic line colors with a block-wide selection.
        void Select(TextBox editor, int start, int length) { editor.CaretIndex = Math.Clamp(start, 0, editor.Text?.Length ?? 0); editor.SelectionStart = editor.SelectionEnd = editor.CaretIndex; }
        if (!_manualMerge) {
            var range = _merge.Document.Range(block.Id); Select(_resultEditor, range.Start, range.Length);
            var editor = _resultEditor; int conflict = block.Id;
            Dispatcher.UIThread.Post(() => {
                if (editor != _resultEditor || _manualMerge || _merge?.Document.Conflicts[_activeConflict].Id != conflict) return;
                var viewer = editor.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
                if (viewer == null || editor.Presenter == null) return;
                double y = 0;
                foreach (var line in editor.Presenter.TextLayout.TextLines) { if (line.FirstTextSourceIndex + line.Length > range.Start) break; y += line.Height; }
                viewer.Offset = new Vector(viewer.Offset.X, Math.Max(0, y - editor.LineHeight));
            }, DispatcherPriority.Loaded);
        }
        if (_fullMergeSources) {
            if (_oursEditor != null) Select(_oursEditor, Math.Max(0, _merge.Ours.IndexOf(block.Ours, StringComparison.Ordinal)), block.Ours.Length);
            if (_theirsEditor != null) Select(_theirsEditor, Math.Max(0, _merge.Theirs.IndexOf(block.Theirs, StringComparison.Ordinal)), block.Theirs.Length);
        }
        _syncMergeScroll = false;
    }
    void WireMergeScrolling() {
        var viewers = new[] { _oursEditor, _resultEditor, _theirsEditor }.Where(e => e != null).Select(e => e!.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault()).Where(v => v != null).Cast<ScrollViewer>().ToArray();
        var resultViewer = _resultEditor.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        foreach (var viewer in viewers) viewer.ScrollChanged += (_, e) => {
            if (!GitlandApplication.Preferences.SyncMergeScroll || _syncMergeScroll || e.OffsetDelta.Y == 0) return;
            _syncMergeScroll = true;
            double ratio = viewer.Offset.Y / Math.Max(1, viewer.Extent.Height - viewer.Viewport.Height);
            foreach (var other in viewers.Where(v => v != viewer && (_fullMergeSources || (v != resultViewer && viewer != resultViewer)))) other.Offset = new Vector(other.Offset.X, ratio * Math.Max(0, other.Extent.Height - other.Viewport.Height));
            _syncMergeScroll = false;
        };
    }
    async Task ResetManual() {
        if (_merge == null || !_manualMerge) return;
        if (!await Confirm("Reset manual edits?", "Your hand-edited result will be replaced with the current block choices. The repository file has not been changed.", "Reset edits")) return;
        RememberMerge();
        _settingResult = true; _resultEditor.Text = _merge.Document.Render(); _settingResult = false; _manualMerge = false; _mergeDirty = true; RenderConflicts();
    }
    async Task SaveMerge() {
        if (_merge == null) return;
        var result = _resultEditor.Text ?? "";
        if (MergeDocument.HasMarkers(result)) throw new InvalidOperationException("Resolve all conflict markers first.");
        if (_repo == null) {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Export merge result", SuggestedFileName = "merge.ts" });
            if (file == null) return;
            await using var stream = await file.OpenWriteAsync(); stream.SetLength(0); await using var writer = new StreamWriter(stream); await writer.WriteAsync(result); _mergeDirty = false; _status.Text = "Merge result exported."; return;
        }
        string backup = await _repo.SaveMergeAsync(_merge, result); _mergeDirty = false;
        try { await _repo.StageFileAsync(_merge.Path); }
        catch (Exception e) { _status.Text = "Result saved; staging failed. Backup: " + backup; throw new InvalidOperationException("The result was saved, but Git could not mark it resolved. Refresh and stage the file manually. " + e.Message); }
        _merge = null; await Refresh(); _status.Text = "Saved and marked resolved · Recovery copy: " + backup;
    }
    async Task<bool> MayLeaveMerge() {
        if (!_mergeDirty) return true;
        bool leave = await Confirm("Leave this merge result?", "The result contains unsaved changes. Leave to discard these in-memory edits, or keep editing to save them.", "Discard edits");
        if (leave) _mergeDirty = false; return leave;
    }
    async Task<bool> Confirm(string title, string detail, string action) {
        var dialog = MakeDialog(title, detail);
        ((StackPanel)dialog.Content!).Children.Add(Row(Button("Keep editing", () => dialog.Close(false)), Button(action, () => dialog.Close(true), primary: true)));
        return await dialog.ShowDialog<bool>(this);
    }

    // Used by the deterministic native preview harness; does not open or modify a repository.
    public async Task PreviewDiff() { _mergeDirty = false; _filter = "all"; await SetMode("changes"); }
    public async Task PreviewMerge() { _mergeDirty = false; await SetMode("merge"); }
}
