using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Gitland.Core;
using static Gitland.App.Palette;

namespace Gitland.App;

public sealed partial class MainWindow {
    async Task ShowMagicResolve() {
        if (_merge == null) return;
        var sourceMerge = _merge;
        var plan = SmartMerge.Prepare(_resultEditor.Text ?? "", sourceMerge.Document);
        var suggestions = plan.Suggestions;
        int manual = plan.Document.Conflicts.Count - suggestions.Count;
        var dialog = new Window { Title = "Magic resolve", Width = 860, Height = 650, MinWidth = 570, MinHeight = 380, Background = Ground, Foreground = Ink, FontFamily = Sans, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(22) };
        var heading = Col(Text("Magic resolve", 22, strong: true), Paragraph($"{suggestions.Count} can be combined · {manual} need your decision", suggestions.Count > 0 ? Green : Amber), Paragraph("Review the suggested code, then apply the resolutions you want. Your file is written only when you save the merge.", Faint)); heading.Spacing = 8; heading.Margin = new Thickness(0, 0, 0, 18); Add(root, heading, 0);
        var list = new StackPanel { Spacing = 14 }; var selected = new Dictionary<int, CheckBox>();
        foreach (var block in suggestions) {
            var check = new CheckBox { Content = $"Conflict {block.Id:00}", IsChecked = true };
            selected.Add(block.Id, check);
            var editor = new MergeEditor { Text = block.Suggestion!.Text, IsReadOnly = true, FontSize = GitlandApplication.Preferences.CodeSize, MinHeight = 65, MaxHeight = 190 };
            editor.SetHighlights(MergeHighlighting.Source(block.Base ?? "", block.Suggestion.Text, MergeLineKind.Resolved));
            var body = Col(check, Paragraph(block.Suggestion.Reason, Muted), CodePane(editor)); body.Spacing = 9;
            list.Children.Add(new Border { Child = body, Background = Bar, BorderBrush = Hairline, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(14) });
        }
        foreach (var block in plan.Document.Conflicts.Where(c => c.Suggestion == null)) {
            string reason = block.Base == null ? "No unambiguous ancestor is available for this block." : "The edits overlap or cannot be combined reliably.";
            list.Children.Add(Section($"Conflict {block.Id:00} · Needs decision", Paragraph(reason + " Choose a side or edit the result in the merge workspace.", Faint)));
        }
        if (plan.Document.Conflicts.Count == 0) list.Children.Add(Paragraph("No complete conflict blocks remain. Review any leftover markers in the result."));
        Add(root, new ScrollViewer { Content = list, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }, 1);
        var apply = Button("Apply selected resolutions", () => dialog.Close(true), primary: true);
        void UpdateSelection() { int count = selected.Values.Count(c => c.IsChecked == true); apply.IsEnabled = count > 0; apply.Content = Text($"Apply {count} " + (count == 1 ? "resolution" : "resolutions"), 12, OnPrimary, true); }
        foreach (var check in selected.Values) check.IsCheckedChanged += (_, _) => UpdateSelection();
        var footer = Row(Button("Cancel magic resolve", () => dialog.Close(false)), apply); footer.HorizontalAlignment = HorizontalAlignment.Right; footer.Margin = new Thickness(0, 16, 0, 0); Add(root, footer, 2); UpdateSelection(); dialog.Content = root;
        if (!await dialog.ShowDialog<bool>(this)) return;
        if (_merge != sourceMerge || _resultEditor.Text != plan.Source) throw new InvalidOperationException("The merge result changed. Open Magic resolve again to review updated suggestions.");
        int[] ids = selected.Where(pair => pair.Value.IsChecked == true).Select(pair => pair.Key).ToArray();
        string result = plan.Apply(ids);
        RememberMerge();
        // Rebase the conflict model on the text reviewed in the dialog, retaining hand edits.
        _merge = sourceMerge with { Document = plan.Document };
        _settingResult = true; _resultEditor.Text = result; _settingResult = false;
        _manualMerge = false; _mergeDirty = true;
        _activeConflict = Math.Max(0, plan.Document.Conflicts.FindIndex(c => c.Choice == Resolution.Unresolved));
        RenderConflicts(); FocusConflict();
        _status.Text = $"Magic resolve applied {ids.Length} resolutions · {plan.Document.Unresolved} unresolved · Review, then save. Undo resolution restores your previous text.";
    }
}
