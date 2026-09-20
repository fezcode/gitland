using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Gitland.Core;
using static Gitland.App.Palette;

namespace Gitland.App;

public sealed partial class MainWindow {
    async Task ShowCommitWindow() {
        if (OwnedWindows.Count > 0) return;
        CommitReview review;
        async Task<CommitReview> ReadReview() {
            if (_repo != null) return await _repo.ReadCommitReviewAsync();
            var files = _state.Changes.Where(c => c.IsStaged && !c.IsConflict).Select(c => {
                var comparison = _fixture!.Compare(c.Path, true); var diff = DiffEngine.Compare(comparison.Left, comparison.Right);
                return new CommitFileSummary(c.Path, c.OldPath, c.Index, diff.Added, diff.Removed);
            }).ToArray();
            return new(_state.Branch, null, "", false, files, _state.Changes.Count(c => c.IsUnstaged), _state.Changes.Count(c => c.IsConflict));
        }
        review = await ReadReview();
        var dialog = new Window { Title = "Write commit message · " + review.Branch, Width = 1040, Height = 760, MinWidth = 760, MinHeight = 540, CanResize = true, Background = Ground, Foreground = Ink, FontFamily = Sans, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(24) };
        var heading = Col(Text("Write commit message", 23, strong: true), Paragraph((_repo == null ? "No repository" : System.IO.Path.GetFileName(_repo.Root)) + "  /  " + review.Branch, Faint)); heading.Spacing = 8; heading.Margin = new Thickness(0, 0, 0, 24); Add(root, heading, 0);
        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("*,310"), ColumnSpacing = 24 };
        var message = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*") };
        var counter = Text("", 11, Faint);
        var summaryLabel = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 8) };
        summaryLabel.Children.Add(Text("Summary", 13, strong: true)); Add(summaryLabel, counter, 0, 1); Add(message, summaryLabel, 0);
        var summary = new TextBox { Name = "WindowCommitSummary", Text = _commitSummary.Text, Watermark = "Describe the change in one sentence", Height = 62, FontSize = 15, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        Add(message, summary, 1);
        var body = new TextBox { Name = "WindowCommitDescription", Text = _commitBody.Text, Watermark = "Explain what changed, why, and anything reviewers should know…", AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 14, VerticalAlignment = VerticalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top };
        ScrollViewer.SetVerticalScrollBarVisibility(body, ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(body, ScrollBarVisibility.Disabled);
        var insert = Button("Insert changed files summary", () => {
            if (review.Files.Count == 0) return;
            string text = review.MessageSummary();
            if ((body.Text ?? "").Contains(text, StringComparison.Ordinal)) { body.Focus(); return; }
            body.Text = (body.Text ?? "").TrimEnd() + (string.IsNullOrWhiteSpace(body.Text) ? "" : "\n\n") + text;
            body.CaretIndex = body.Text.Length; body.Focus();
        }); insert.Classes.Add("quiet");
        var descriptionLabel = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 16, 0, 8) };
        descriptionLabel.Children.Add(Text("Description", 13, strong: true)); Add(descriptionLabel, insert, 0, 1); Add(message, descriptionLabel, 2); Add(message, body, 3); content.Children.Add(message);
        var filesPanel = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"), Background = Bar };
        var totals = new StackPanel { Spacing = 8, Margin = new Thickness(16) };
        Add(filesPanel, totals, 0);
        var fileList = new StackPanel { Spacing = 0 };
        Add(filesPanel, new ScrollViewer { Content = fileList, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, 2);
        var excluded = Paragraph("", Faint); excluded.FontSize = 11; excluded.Margin = new Thickness(16); Add(filesPanel, excluded, 3);
        Add(content, new Border { Child = filesPanel, BorderBrush = Hairline, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), ClipToBounds = true }, 0, 1); Add(root, content, 1);
        var feedback = Paragraph("Your draft stays in the sidebar when you close this window.", Faint); feedback.FontSize = 11;
        bool submitting = false;
        var done = Button("Keep draft and close", () => dialog.Close());
        var commit = Button("Commit from message window", () => { }, "check", true);
        var refresh = Button("Refresh changed files summary", () => { }, "refresh"); refresh.Classes.Add("quiet"); refresh.HorizontalAlignment = HorizontalAlignment.Left; refresh.Margin = new Thickness(8, 0, 8, 8); Add(filesPanel, refresh, 1);
        void Update() {
            counter.Text = $"{summary.Text?.Length ?? 0} / 72"; counter.Foreground = (summary.Text?.Length ?? 0) > 72 ? Amber : Faint;
            commit.IsEnabled = !submitting && _repo != null && review.Conflicts == 0 && review.IndexTree.Length > 0 && (review.Files.Count > 0 || review.MergeInProgress) && !string.IsNullOrWhiteSpace(summary.Text);
            commit.Content = Row(Icon("check", OnPrimary, 14), Text(review.MergeInProgress ? "Commit merge" : "Commit staged", 12, OnPrimary, true), Text("Ctrl+↵", 10, OnPrimary));
            insert.IsEnabled = !submitting && review.Files.Count > 0;
            done.IsEnabled = refresh.IsEnabled = !submitting; summary.IsReadOnly = body.IsReadOnly = submitting;
        }
        void RenderReview() {
            totals.Children.Clear(); totals.Children.Add(Text("In this commit", 15, strong: true));
            totals.Children.Add(Row(Text($"{review.Files.Count} " + (review.Files.Count == 1 ? "file" : "files"), 12, Muted), Text($"+{review.Files.Sum(f => f.Added ?? 0)}", 12, Green), Text($"−{review.Files.Sum(f => f.Removed ?? 0)}", 12, Red)));
            fileList.Children.Clear();
            foreach (var file in review.Files) {
                var name = Paragraph(file.Path, Ink); name.FontSize = 12;
                var lines = Col(name, Row(Text(file.Label, 10, Faint), file.Binary ? Text("Binary", 10, Muted) : Row(Text($"+{file.Added}", 10, Green), Text($"−{file.Removed}", 10, Red)))); lines.Spacing = 5;
                if (file.OldPath != null) { var old = Paragraph("From " + file.OldPath, Faint); old.FontSize = 10; lines.Children.Add(old); }
                fileList.Children.Add(new Border { Child = lines, Padding = new Thickness(16, 12), BorderBrush = Hairline, BorderThickness = new Thickness(0, 0, 0, 1) });
            }
            if (review.Files.Count == 0) { var empty = Paragraph(review.Conflicts > 0 ? "Resolve and stage conflicts before committing." : review.MergeInProgress ? "The merge is resolved with no file changes relative to the current commit." : "No staged file changes. Stage files or hunks in Working changes.", Faint); empty.Margin = new Thickness(16); fileList.Children.Add(empty); }
            excluded.Text = review.Conflicts > 0 ? $"{review.Conflicts} conflicts must be resolved first." : $"{review.Unstaged} " + (review.Unstaged == 1 ? "file has" : "files have") + " unstaged changes. Only the staged versions are included.";
            Update();
        }
        void DraftChanged() { _commitSummary.Text = summary.Text; _commitBody.Text = body.Text; feedback.Text = "Draft kept in the sidebar · Ctrl+Enter to commit"; feedback.Foreground = Faint; Update(); }
        summary.TextChanged += (_, _) => DraftChanged(); body.TextChanged += (_, _) => DraftChanged();
        refresh.Click += async (_, _) => {
            if (submitting) return; submitting = true; Update();
            try { review = await ReadReview(); RenderReview(); feedback.Text = "File summary refreshed. Your message has been kept."; feedback.Foreground = Faint; }
            catch (Exception e) { feedback.Text = e.Message; feedback.Foreground = Red; }
            finally { submitting = false; Update(); }
        };
        async Task Submit() {
            if (!commit.IsEnabled || submitting) return;
            submitting = true; Update(); feedback.Text = "Committing staged changes…";
            try {
                if (await CommitDraft(review.IndexTree, review.Head, review.Branch)) { submitting = false; dialog.Close(); return; }
                feedback.Text = _commitFeedback; feedback.Foreground = Red;
            } catch (Exception e) { feedback.Text = e.Message; feedback.Foreground = Red; }
            finally { submitting = false; Update(); }
        }
        commit.Click += async (_, _) => await Submit();
        dialog.KeyDown += async (_, e) => { if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { e.Handled = true; await Submit(); } };
        dialog.Closing += (_, e) => { if (submitting) e.Cancel = true; };
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 18, Margin = new Thickness(0, 18, 0, 0) }; footer.Children.Add(feedback); Add(footer, Row(done, commit), 0, 1); Add(root, footer, 2);
        dialog.Content = root; RenderReview(); await dialog.ShowDialog(this);
    }
}
