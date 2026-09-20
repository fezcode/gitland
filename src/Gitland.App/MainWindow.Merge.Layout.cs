using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Gitland.Core;
using static Gitland.App.Palette;

namespace Gitland.App;

public sealed partial class MainWindow {
    bool _fullMergeSources;
    TextBlock? _activeTitle, _decisionNote, _resultStatus, _oursLocation, _theirsLocation;
    StackPanel? _decisionActions;
    ProgressBar? _resolutionProgress;
    Button? _resetManualButton, _ancestorButton;

    void BuildMergeWorkspace(GitChange file) {
        _fullMergeSources = false;
        var grid = new Grid { Name = "MergeWorkspace", RowDefinitions = new RowDefinitions("Auto,Auto,0.65*,5,Auto,1.35*,Auto"), Background = Bar };
        grid.RowDefinitions[2].MinHeight = 158; grid.RowDefinitions[2].MaxHeight = 185;
        grid.RowDefinitions[5].MinHeight = 145;
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(20, 8, 20, 8) };
        string folder = System.IO.Path.GetDirectoryName(file.Path)?.Replace('\\', '/') ?? "Repository root";
        var identity = Col(Text(folder + "  /  MERGE", 10, Faint), Row(Icon("merge", Amber, 19), Text(System.IO.Path.GetFileName(file.Path), 21, strong: true))); identity.Spacing = 6;
        heading.Children.Add(identity);
        _undoMergeButton = Button("Undo resolution", UndoMerge, "refresh");
        _saveMerge = Button(_repo == null ? "Export result" : "Save & mark resolved", () => Run(SaveMerge), "save", true);
        Add(heading, Row(_undoMergeButton, _saveMerge), 0, 1); Add(grid, heading, 0);

        var navigator = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(18, 0, 18, 8), ColumnSpacing = 12 };
        _conflictList.Orientation = Orientation.Horizontal; _conflictList.Spacing = 6;
        navigator.Children.Add(new ScrollViewer { Content = _conflictList, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, MaxHeight = 50 });
        _magicButton = Button("Magic resolve", () => Run(ShowMagicResolve), "wand");
        Add(navigator, Row(_magicButton, IconButton("Previous conflict", () => NavigateConflict(-1), "up"), IconButton("Next conflict", () => NavigateConflict(1), "down")), 0, 1); Add(grid, navigator, 1);

        _oursEditor = new MergeEditor { IsReadOnly = true, FontSize = GitlandApplication.Preferences.CodeSize, Name = "MergeOurs" };
        _theirsEditor = new MergeEditor { IsReadOnly = true, FontSize = GitlandApplication.Preferences.CodeSize, Name = "MergeTheirs" };
        _resultEditor.Name = "MergeResult";
        _acceptOurs = IconButton("Keep ours", () => AcceptActive(Resolution.Ours), "arrow-down"); _acceptOurs.Content = Icon("arrow-down", OursInk, 18); ToolTip.SetTip(_acceptOurs, "Use ours in the merged result");
        _acceptTheirs = IconButton("Keep theirs", () => AcceptActive(Resolution.Theirs), "arrow-down"); _acceptTheirs.Content = Icon("arrow-down", TheirsInk, 18); ToolTip.SetTip(_acceptTheirs, "Use theirs in the merged result");
        _oursLocation = Text("", 10, Muted); _theirsLocation = Text("", 10, Muted);
        var sources = new Grid { ColumnDefinitions = new ColumnDefinitions("*,10,*"), Margin = new Thickness(18, 0) };
        Add(sources, SourcePane("Ours", "Current branch", _oursLocation, _oursEditor, OursInk, OursFill, _acceptOurs), 0);
        Add(sources, SourcePane("Theirs", "Incoming changes", _theirsLocation, _theirsEditor, TheirsInk, TheirsFill, _acceptTheirs), 0, 2);
        Add(grid, sources, 2);
        var resize = new GridSplitter { ResizeDirection = GridResizeDirection.Rows, HorizontalAlignment = HorizontalAlignment.Stretch, Background = Brushes.Transparent, Margin = new Thickness(18, 0) }; Add(grid, resize, 3);

        var decision = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(20, 6, 20, 6), ColumnSpacing = 12 };
        _activeTitle = Text("", 12, strong: true); _decisionNote = Text("", 11, Muted);
        var decisionText = Col(_activeTitle, _decisionNote); decisionText.Spacing = 3; decisionText.ClipToBounds = true; decision.Children.Add(decisionText);
        _decisionActions = Row(); Add(decision, _decisionActions, 0, 1); Add(grid, decision, 4);

        var output = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        var outputHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(14, 10) };
        var resultLabel = Row(Icon("diff", Green, 16), Text("Merged result", 15, strong: true), Badge("EDITABLE", Green)); outputHeader.Children.Add(resultLabel);
        _resetManualButton = Button("Reset manual edits", () => Run(ResetManual)); _resetManualButton.Classes.Add("quiet");
        var fullSources = new CheckBox { Content = "Full sources", FontSize = 11 };
        fullSources.IsCheckedChanged += (_, _) => { _fullMergeSources = fullSources.IsChecked == true; UpdateSourcePreviews(); FocusConflict(); };
        _ancestorButton = Button("Inspect ancestor", () => _ = ShowMergeAncestor(), "layers"); _ancestorButton.Classes.Add("quiet");
        bool expanded = false;
        var expand = IconButton("Expand result", () => { }, "expand");
        expand.Click += (_, _) => {
            expanded = !expanded; sources.IsVisible = !expanded; decision.IsVisible = !expanded; resize.IsVisible = !expanded;
            grid.RowDefinitions[2].MinHeight = expanded ? 0 : 158; grid.RowDefinitions[2].MaxHeight = expanded ? 0 : 185;
            grid.RowDefinitions[2].Height = expanded ? new GridLength(0) : new GridLength(.65, GridUnitType.Star);
            Avalonia.Automation.AutomationProperties.SetName(expand, expanded ? "Restore source previews" : "Expand result");
            ToolTip.SetTip(expand, expanded ? "Restore source previews" : "Expand result"); expand.Content = Icon(expanded ? "restore" : "expand", Muted, 14);
        };
        Add(outputHeader, Row(fullSources, _ancestorButton, _resetManualButton, expand), 0, 1);
        Add(output, new Border { Background = Raised, Child = outputHeader, BorderBrush = Hairline, BorderThickness = new Thickness(0, 0, 0, 1) }, 0);
        Add(output, CodePane(_resultEditor), 1);
        Add(grid, new Border { Name = "MergeOutputFrame", Child = output, Background = Ground, BorderBrush = Hairline, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), ClipToBounds = true, Margin = new Thickness(18, 0, 18, 0) }, 5);

        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 12, Margin = new Thickness(20, 9, 20, 10) };
        _resolutionProgress = new ProgressBar { Width = 62, MinWidth = 62, Height = 3, Minimum = 0, Maximum = Math.Max(1, _merge!.Document.Conflicts.Count), Foreground = Green, Background = Hairline, VerticalAlignment = VerticalAlignment.Center };
        footer.Children.Add(_resolutionProgress); _resultStatus = Text("", 11, Muted); Add(footer, _resultStatus, 0, 1);
        var legend = Row(Legend("Ours", OursInk), Legend("Base", Faint), Legend("Theirs", TheirsInk), Legend("Unresolved", Amber)); legend.Spacing = 12; Add(footer, legend, 0, 2); Add(grid, footer, 6);
        _workspace.Content = grid; RenderConflicts();
        Dispatcher.UIThread.Post(() => { WireMergeScrolling(); FocusConflict(); }, DispatcherPriority.Loaded);
        _status.Text = "Choose a conflict above. Compare the source versions, then build and review the result below.";
    }
    static Control Legend(string label, IBrush color) => Row(new Border { Width = 5, Height = 5, CornerRadius = new CornerRadius(1), Background = color }, Text(label, 10, Faint));
    Control SourcePane(string title, string subtitle, TextBlock location, MergeEditor editor, IBrush color, IBrush headerFill, Button accept) {
        var body = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(12, 8), ColumnSpacing = 6 };
        var label = Col(Row(Text(title, 14, color, true), Text(subtitle, 11, Muted)), location); label.Spacing = 3; label.ClipToBounds = true;
        header.Children.Add(label); Add(header, accept, 0, 1);
        Add(body, new Border { Background = headerFill, Child = header }, 0); Add(body, CodePane(editor), 1);
        return new Border { Child = body, Background = Ground, BorderBrush = Hairline, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), ClipToBounds = true };
    }
    static Control CodePane(MergeEditor editor) {
        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("40,*"), Background = Ground };
        content.Children.Add(new CodeGutter(editor)); Add(content, editor, 0, 1);
        ScrollViewer.SetHorizontalScrollBarVisibility(editor, ScrollBarVisibility.Auto); ScrollViewer.SetVerticalScrollBarVisibility(editor, ScrollBarVisibility.Auto);
        return content;
    }
    void RenderConflicts() {
        if (_merge == null) return;
        _resultEditor.SetHighlights(MergeHighlighting.Result(_resultEditor.Text ?? "", _manualMerge ? null : _merge.Document));
        _conflictList.Children.Clear();
        foreach (var block in _merge.Document.Conflicts) {
            bool active = _activeConflict == block.Id - 1; bool resolved = block.Choice != Resolution.Unresolved;
            var jump = Button($"Conflict {block.Id:00}", () => { _activeConflict = block.Id - 1; RenderConflicts(); FocusConflict(); });
            jump.Content = Row(Text(block.Id.ToString("00"), 11, active ? Ink : Faint, true), Text(resolved ? ChoiceLabel(block.Choice) : block.Suggestion != null ? "Can combine" : "Needs decision", 11, resolved ? Green : Amber), Icon(resolved ? "check" : "diff", resolved ? Green : Amber, 12));
            jump.Classes.Add("selection-item"); jump.Classes.Set("selected", active); jump.Background = active ? SelectedSurface : Brushes.Transparent; jump.BorderBrush = Brushes.Transparent; jump.BorderThickness = new Thickness(0); jump.Padding = new Thickness(11, 7); jump.MinHeight = 32;
            _conflictList.Children.Add(jump);
        }
        UpdateSourcePreviews(); RenderDecision(); UpdateMergeProgress();
    }
    static string ChoiceLabel(Resolution choice) => choice switch { Resolution.Ours => "Using ours", Resolution.Theirs => "Using theirs", Resolution.Both => "Keeping both", Resolution.Base => "Using base", Resolution.Smart => "Combined", _ => "Needs decision" };
    void RenderDecision() {
        if (_merge == null || _decisionActions == null) return;
        _decisionActions.Children.Clear();
        if (_merge.Document.Conflicts.Count == 0) { _activeTitle!.Text = "No conflict blocks"; _decisionNote!.Text = "Review the file below before saving."; return; }
        var block = _merge.Document.Conflicts[_activeConflict];
        _activeTitle!.Text = $"Conflict {block.Id:00}  /  {_merge.Document.Conflicts.Count:00}   ·   {(_manualMerge ? "Manual editing" : ChoiceLabel(block.Choice))}";
        _decisionNote!.Text = _manualMerge ? "Magic resolve can review remaining markers. Ctrl+Z restores text edits." : block.Choice != Resolution.Unresolved ? "Review your choice in the result below." : block.Suggestion != null ? block.Suggestion.Reason : "Both sides changed this section. Choose the version to keep.";
        foreach (var (choice, label) in new[] { (Resolution.Smart, "Apply smart"), (Resolution.Both, "Keep both"), (Resolution.Base, "Use base"), (Resolution.Unresolved, "Reset") }) {
            if (choice == Resolution.Smart && block.Suggestion == null || choice == Resolution.Base && block.Base == null) continue;
            var button = Button(label, () => Choose(block, choice), choice == Resolution.Smart ? "merge" : null, choice == Resolution.Smart);
            if (choice == Resolution.Smart) button.Content = Row(Icon("wand", OnPrimary, 13), Text("Combine edits", 12, OnPrimary));
            button.IsEnabled = !_manualMerge && (choice != Resolution.Unresolved || block.Choice != Resolution.Unresolved); _decisionActions.Children.Add(button);
        }
    }
    void UpdateSourcePreviews() {
        if (_merge == null || _oursEditor == null || _theirsEditor == null || _merge.Document.Conflicts.Count == 0) return;
        var block = _merge.Document.Conflicts[_activeConflict];
        void Source(MergeEditor editor, TextBlock label, string source, string fragment, MergeLineKind kind) {
            string text = _fullMergeSources ? source : fragment;
            editor.TextWrapping = _fullMergeSources ? TextWrapping.NoWrap : TextWrapping.Wrap;
            ScrollViewer.SetHorizontalScrollBarVisibility(editor, _fullMergeSources ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled);
            if (editor.Text != text) editor.Text = text;
            int offset = SourceOffset(source, block, kind);
            editor.LineNumberStart = _fullMergeSources || offset < 0 ? 1 : 1 + source.AsSpan(0, offset).Count('\n');
            label.Text = (_fullMergeSources ? "FULL FILE" : $"CONFLICT {block.Id:00}") + "  ·  READ ONLY" + (text.Length == 0 ? "  ·  No lines on this side" : !_fullMergeSources && offset >= 0 ? $"  ·  Line {editor.LineNumberStart}" : "");
            editor.SetHighlights(MergeHighlighting.Source(_fullMergeSources ? _merge.Base : block.Base ?? "", text, kind));
        }
        Source(_oursEditor, _oursLocation!, _merge.Ours, block.Ours, MergeLineKind.Ours); Source(_theirsEditor, _theirsLocation!, _merge.Theirs, block.Theirs, MergeLineKind.Theirs);
        if (!_manualMerge) { var range = _merge.Document.Range(block.Id); _resultEditor.SetActiveRange(range.Start, range.Length); }
        else _resultEditor.SetActiveRange(-1, 0);
    }
    int SourceOffset(string source, ConflictBlock target, MergeLineKind kind) {
        int cursor = 0;
        foreach (var block in _merge!.Document.Conflicts) {
            string text = kind == MergeLineKind.Ours ? block.Ours : block.Theirs;
            int found = text.Length == 0 ? -1 : source.IndexOf(text, cursor, StringComparison.Ordinal);
            if (block.Id == target.Id) return found;
            if (found >= 0) cursor = found + text.Length;
        }
        return -1;
    }
    void UpdateMergeProgress() {
        if (_merge == null) return;
        bool markers = MergeDocument.HasMarkers(_resultEditor.Text ?? "");
        int count = _merge.Document.Conflicts.Count, resolved = count - _merge.Document.Unresolved;
        _mergeProgress.Text = _manualMerge ? markers ? "Manual edits · markers remain" : "Manual result · ready to review" : $"{resolved} of {count} resolved";
        if (_resultStatus != null) { _resultStatus.Text = _mergeProgress.Text + (_mergeDirty ? "  ·  Unsaved" : "  ·  No file written"); _resultStatus.Foreground = markers ? Amber : Green; }
        if (_resolutionProgress != null) _resolutionProgress.Value = _manualMerge ? markers ? 0 : count : resolved;
        if (_saveMerge != null) _saveMerge.IsEnabled = !markers;
        int available = _merge.Document.Conflicts.Count(c => c.Choice == Resolution.Unresolved && c.Suggestion != null);
        if (_magicButton != null) {
            _magicButton.IsEnabled = markers;
            _magicButton.Content = Row(Icon("wand", Muted, 13), Text("Magic resolve" + (!_manualMerge && available > 0 ? $" · {available}" : ""), 12));
            ToolTip.SetTip(_magicButton, "Preview independent resolutions against the ancestor. Select which suggestions to apply; manual edits outside the markers are preserved.");
        }
        if (_undoMergeButton != null) _undoMergeButton.IsEnabled = _mergeUndo.Count > 0 && !_manualMerge;
        if (_acceptOurs != null) _acceptOurs.IsEnabled = !_manualMerge && count > 0;
        if (_acceptTheirs != null) _acceptTheirs.IsEnabled = !_manualMerge && count > 0;
        if (_resetManualButton != null) _resetManualButton.IsVisible = _manualMerge;
    }
    async Task ShowMergeAncestor() {
        if (_merge == null || _merge.Document.Conflicts.Count == 0 || OwnedWindows.Count > 0) return;
        var block = _merge.Document.Conflicts[_activeConflict];
        var dialog = new Window { Title = $"Common ancestor · Conflict {block.Id:00}", Width = 800, Height = 420, MinWidth = 500, MinHeight = 240, Background = Ground, Foreground = Ink, FontFamily = Sans, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var content = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        var title = Col(Text($"Common ancestor · Conflict {block.Id:00}", 18, strong: true), Text(block.Base == null ? "No matching ancestor fragment. Showing the complete base file." : "The version both sides started from · read only", 12, Muted)); title.Margin = new Thickness(20, 16); content.Children.Add(title);
        var editor = new MergeEditor { Text = block.Base ?? _merge.Base, IsReadOnly = true, FontSize = GitlandApplication.Preferences.CodeSize }; Add(content, CodePane(editor), 1);
        var done = Button("Close ancestor", () => dialog.Close()); done.HorizontalAlignment = HorizontalAlignment.Right; done.Margin = new Thickness(16); Add(content, done, 2); dialog.Content = content;
        await dialog.ShowDialog(this);
    }
}
