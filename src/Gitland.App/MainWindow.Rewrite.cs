using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Gitland.Core;
using static Gitland.App.Palette;

namespace Gitland.App;

public sealed partial class MainWindow {

    async Task PullDialog() {
        if (_repo == null) return;
        var mode = new ComboBox { ItemsSource = new[] { "Fast-forward only", "Merge", "Rebase" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var stash = new CheckBox { Content = "Stash my changes first, then restore them" };
        await FormDialog("Pull", [
            Field("How to integrate", mode, "Fast-forward only refuses to create a commit. Merge records one. Rebase replays your local commits on top."),
            stash,
            Paragraph("Without stashing, a pull needs a clean working tree.")
        ], "Pull", async () => {
            string picked = mode.SelectedIndex switch { 1 => "merge", 2 => "rebase", _ => "ff-only" };
            await OperationResult(await _repo.PullAsync(picked, stash.IsChecked == true));
        });
    }

    async Task HardResetDialog() {
        if (_repo == null || _management?.Head is not { } head) return;
        var target = new TextBox { Text = "HEAD~1", Watermark = "Branch, tag, or commit" };
        await FormDialog("Hard reset", [
            Paragraph("Move this branch to another commit and make the working tree match it exactly."),
            Field("Reset to", target),
            Paragraph("Uncommitted changes are replaced. Gitland records both the commit you are leaving and the current working tree under Recovery first, so each can be restored.", Amber)
        ], "Hard reset", async () => { await _repo.ResetHardAsync(target.Text ?? "HEAD", head); await LoadManagement(); _status.Text = "Hard reset complete · both the old commit and your working tree are under Recovery."; });
    }

    async Task AmendDialog() {
        if (_repo == null || _management?.Head is not { } head) return;
        string current = await _repo.CommitMessageAsync("HEAD");
        var message = new TextBox { Text = current.TrimEnd(), AcceptsReturn = true, MinHeight = 140, TextWrapping = TextWrapping.Wrap };
        var staged = _state.Changes.Count(c => c.IsStaged);
        var include = new CheckBox { Content = $"Also fold in the {staged} staged change{(staged == 1 ? "" : "s")}", IsEnabled = staged > 0 };
        await FormDialog("Amend last commit", [
            Field("Message", message), include,
            Paragraph("Amending rewrites the last commit. If it is already pushed, the branch will need a forced push.", Amber)
        ], "Amend commit", async () => { await _repo.AmendMessageAsync(message.Text ?? "", head, include.IsChecked == true); await LoadManagement(); _status.Text = "Last commit amended."; });
    }

    async Task CherryPickRangeDialog() {
        if (_repo == null || _management?.Head is not { } head) return;
        var list = new TextBox { Watermark = "One revision per line, oldest first", AcceptsReturn = true, MinHeight = 120 };
        await FormDialog("Cherry-pick a run", [
            Paragraph("Apply several commits onto this branch, in the order given."),
            Field("Revisions", list),
            Paragraph("The run stops at the first commit that conflicts; resolve it and Continue from Repository.")
        ], "Cherry-pick", async () => {
            var revisions = (list.Text ?? "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
            await OperationResult(await _repo.CherryPickRangeAsync(revisions, head));
        });
    }

    async Task RevertDialog() {
        if (_repo == null || _management?.Head is not { } head) return;
        var target = new TextBox { Text = "HEAD", Watermark = "Commit to reverse" };
        var mainline = new TextBox { Text = "1", Watermark = "1" };
        await FormDialog("Revert commit", [
            Paragraph("Create a new commit that undoes the selected one. History is not rewritten."),
            Field("Commit", target),
            Field("If it is a merge, keep parent", mainline, "1 is the branch the merge was made into. Ignored for ordinary commits.")
        ], "Revert", async () => {
            int parent = int.TryParse(mainline.Text, out int value) ? value : 1;
            await OperationResult(await _repo.RevertCommitAsync(target.Text ?? "HEAD", head, parent));
        });
    }

    /// <summary>Shows the commits about to be rebased as an editable plan: one row each, with an
    /// action and arrows to move it. Nothing runs until Start rebase.</summary>
    async Task InteractiveRebaseDialog() {
        if (_repo == null || _management?.Head is not { } head) return;
        var upstream = new TextBox { Text = "HEAD~3", Watermark = "Rebase commits after this revision" };
        var rows = new StackPanel { Spacing = 6 };
        var plan = new List<RebaseStep>();
        var hint = Paragraph("Load the commits between that revision and HEAD, then set what happens to each.");

        void Redraw() {
            rows.Children.Clear();
            for (int i = 0; i < plan.Count; i++) {
                int index = i;
                var step = plan[index];
                var action = new ComboBox { ItemsSource = new[] { "pick", "reword", "edit", "squash", "fixup", "drop" }, SelectedItem = step.Action, Width = 110 };
                action.SelectionChanged += (_, _) => plan[index] = plan[index] with { Action = (string?)action.SelectedItem ?? "pick" };
                var up = Button("↑", () => { if (index > 0) { (plan[index - 1], plan[index]) = (plan[index], plan[index - 1]); Redraw(); } });
                var down = Button("↓", () => { if (index < plan.Count - 1) { (plan[index + 1], plan[index]) = (plan[index], plan[index + 1]); Redraw(); } });
                var subject = Text($"{step.Hash[..8]}  {step.Subject}", 12);
                subject.VerticalAlignment = VerticalAlignment.Center;
                var row = Row(action, up, down, subject);
                rows.Children.Add(row);
            }
        }

        var load = Button("Load commits", () => Run(async () => {
            plan.Clear();
            plan.AddRange(await _repo.ReadRebasePlanAsync(upstream.Text ?? "HEAD~3"));
            hint.Text = $"{plan.Count} commit{(plan.Count == 1 ? "" : "s")}, oldest first. Reorder with the arrows; squash and fixup combine into the row above.";
            Redraw();
        }));

        await FormDialog("Interactive rebase", [
            Field("Rebase onto", upstream), load, hint, rows,
            Paragraph("A reworded commit takes the subject you leave in place — Gitland applies it without opening an editor. Rewritten commits need a forced push.", Amber)
        ], "Start rebase", async () => {
            if (plan.Count == 0) throw new InvalidOperationException("Load the commits first.");
            await OperationResult(await _repo.RebaseInteractiveAsync(upstream.Text ?? "HEAD~3", plan.ToArray(), head));
        });
    }

    async Task BisectDialog() {
        if (_repo == null) return;
        var bad = new TextBox { Text = "HEAD", Watermark = "A commit where the problem exists" };
        var good = new TextBox { Watermark = "A commit where it did not" };
        await FormDialog("Start bisect", [
            Paragraph("Git checks out a commit between the two. Test it, then mark it good or bad from the Advanced tab; Gitland reports the first bad commit when the range closes."),
            Field("Bad commit", bad), Field("Good commit", good),
            Paragraph("Your working tree must be clean.")
        ], "Start bisect", async () => {
            string at = await _repo.StartBisectAsync(bad.Text ?? "HEAD", good.Text ?? "");
            await LoadManagement();
            _status.Text = at.Length > 0 ? "Bisecting · testing " + at[..8] : "Bisect started.";
        });
    }

    async Task MarkBisect(string kind) {
        if (_repo == null) return;
        string found = await _repo.MarkBisectAsync(kind);
        await LoadManagement();
        _status.Text = found.Length > 0 ? "First bad commit: " + found[..8] + " · end the bisect to return" : "Marked " + kind + " · testing the next commit.";
    }

    async Task AddSubmoduleDialog() {
        if (_repo == null) return;
        var url = new TextBox { Watermark = "https://github.com/owner/repo.git" };
        var path = new TextBox { Watermark = "vendor/library" };
        await FormDialog("Add submodule", [Field("Repository URL", url), Field("Folder inside this repository", path)], "Add submodule",
            async () => { await _repo.AddSubmoduleAsync(url.Text ?? "", path.Text ?? ""); await LoadManagement(); _status.Text = "Submodule added."; });
    }

    async Task LfsDialog() {
        if (_repo == null) return;
        var pattern = new TextBox { Watermark = "*.psd" };
        await FormDialog("Track with Git LFS", [
            Field("File pattern", pattern),
            Paragraph("Requires git-lfs on PATH. Tracking writes the pattern into .gitattributes; existing committed files are not rewritten.")
        ], "Track pattern", async () => { await _repo.TrackLfsAsync(pattern.Text ?? ""); _status.Text = "Tracking " + pattern.Text + " with LFS."; });
    }

    async Task ExportPatchDialog(string revision) {
        if (_repo == null) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Export patch", SuggestedFileName = "changes.patch", DefaultExtension = "patch" });
        if (file?.TryGetLocalPath() is not { } path) return;
        string written = await _repo.ExportPatchAsync(revision, path);
        _status.Text = "Patch written to " + written;
    }

    async Task ApplyPatchDialog() {
        if (_repo == null) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Apply patch", AllowMultiple = false });
        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
        await _repo.ApplyPatchAsync(path);
        await LoadManagement();
        _status.Text = "Patch applied to the working tree.";
    }

    async Task ArchiveDialog() {
        if (_repo == null) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Archive revision", SuggestedFileName = "source.zip", DefaultExtension = "zip" });
        if (file?.TryGetLocalPath() is not { } path) return;
        string written = await _repo.ArchiveAsync("HEAD", path);
        _status.Text = "Archive written to " + written;
    }
}
