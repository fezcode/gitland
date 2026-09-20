using System.Diagnostics;
using Avalonia.Controls;
using Gitland.Core;
using static Gitland.App.Palette;

namespace Gitland.App;

public sealed partial class MainWindow {
    /// <summary>A context-menu entry. Disabled entries stay visible so the menu keeps a stable
    /// shape; entries that make no sense for the row are left out by the caller instead.</summary>
    static MenuItem MenuAction(string label, Func<Task> action, bool enabled = true) {
        var item = new MenuItem { Header = label, IsEnabled = enabled };
        Avalonia.Automation.AutomationProperties.SetName(item, label);
        item.Click += (_, _) => { if (item.IsEnabled) _ = action(); };
        return item;
    }

    static Task Do(Action action) { action(); return Task.CompletedTask; }

    async Task CopyText(string text, string confirmation) {
        if (Clipboard == null) return;
        await Clipboard.SetTextAsync(text);
        _status.Text = confirmation;
    }

    /// <summary>Opens File Explorer with the item selected, or the folder itself for a directory.</summary>
    Task Reveal(string fullPath) {
        try {
            bool directory = Directory.Exists(fullPath);
            if (!directory && !File.Exists(fullPath)) { _status.Text = "That path no longer exists."; return Task.CompletedTask; }
            Process.Start(new ProcessStartInfo("explorer.exe", directory ? $"\"{fullPath}\"" : $"/select,\"{fullPath}\"") { UseShellExecute = true });
        } catch (Exception e) { _status.Text = "Could not open File Explorer: " + e.Message; }
        return Task.CompletedTask;
    }

    /// <summary>Builds the menu for one file row. The row's group decides what is offered:
    /// a staged row unstages, an unstaged row stages, an untracked row deletes rather than discards,
    /// and a conflict row resolves. Actions every file supports form the closing section.</summary>
    ContextMenu FileContextMenu(GitChange file, bool? stagedView) {
        var menu = new ContextMenu();
        bool live = _repo != null;
        bool untracked = file.Index == '?';
        // A plain list has no group, so fall back to the file's own staged/unstaged state.
        bool staged = stagedView ?? (!file.IsUnstaged && file.IsStaged);

        if (file.IsConflict) {
            menu.Items.Add(MenuAction("Open merge editor", () => OpenMergeFor(file), live));
            menu.Items.Add(MenuAction("Take ours (keep this branch)", () => TakeSide(file, true), live));
            menu.Items.Add(MenuAction("Take theirs (keep incoming)", () => TakeSide(file, false), live));
        } else if (untracked) {
            menu.Items.Add(MenuAction("Stage file", () => StageFrom(file), live));
            menu.Items.Add(MenuAction("Delete file…", () => DiscardFrom(file, stagedView), live));
            menu.Items.Add(MenuAction("Add to .gitignore", () => IgnoreFrom(file), live));
        } else if (staged) {
            menu.Items.Add(MenuAction("Unstage file", () => UnstageFrom(file), live && file.IsChanged));
            menu.Items.Add(MenuAction("Discard all changes…", () => DiscardFrom(file, true), live && file.IsChanged));
        } else {
            menu.Items.Add(MenuAction("Stage file", () => StageFrom(file), live && file.IsChanged));
            menu.Items.Add(MenuAction("Discard changes…", () => DiscardFrom(file, false), live && file.IsChanged));
        }

        menu.Items.Add(new Separator());
        menu.Items.Add(MenuAction("Blame", () => BlameFor(file), live));
        menu.Items.Add(MenuAction("History of this file", () => FileHistoryFor(file), live));
        menu.Items.Add(MenuAction("Rename or move…", () => MoveFileDialog(file), live && !untracked));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuAction("Copy path", () => CopyText(file.Path, "Path copied.")));
        menu.Items.Add(MenuAction("Copy file name", () => CopyText(System.IO.Path.GetFileName(file.Path), "File name copied.")));
        menu.Items.Add(MenuAction("Reveal in File Explorer", () => Reveal(System.IO.Path.Combine(_repo?.Root ?? "", file.Path.Replace('/', System.IO.Path.DirectorySeparatorChar))), live));
        return menu;
    }

    // Each entry selects the row first, so the diff on screen matches what the action is about to
    // change and the confirmation names the same file the user is looking at.
    async Task StageFrom(GitChange file) { await SelectFile(file, false); await StageSelected(); }
    async Task UnstageFrom(GitChange file) { await SelectFile(file, true); await StageSelected(); }
    async Task DiscardFrom(GitChange file, bool? stagedView) { await SelectFile(file, stagedView); await DiscardSelected(); }
    async Task OpenMergeFor(GitChange file) { await SelectFile(file); await OpenSelectedMerge(); }
    async Task BlameFor(GitChange file) { await SelectFile(file); await BlameSelected(); }
    async Task FileHistoryFor(GitChange file) { await SelectFile(file); await FileHistorySelected(); }

    async Task TakeSide(GitChange file, bool ours) {
        if (_repo == null) return;
        string side = ours ? "ours" : "theirs";
        if (!await ReviewAction($"Take {side}?", $"Resolve {file.Path} by keeping the {(ours ? "version on this branch" : "incoming version")} in full, discarding the other side's changes to this file. The file is then staged as resolved.", $"Take {side}")) return;
        await _repo.TakeSideAsync(file.Path, ours);
        await Refresh();
        _status.Text = $"{file.Path} resolved by taking {side}.";
    }

    async Task IgnoreFrom(GitChange file) {
        if (_repo == null) return;
        var pattern = new TextBox { Text = file.Path };
        await FormDialog("Add to .gitignore", [
            Field("Pattern", pattern, "Written to .gitignore in the repository root. Use a trailing slash for a folder, or a wildcard such as *.log."),
            Paragraph("An already-tracked file keeps being tracked; ignoring only affects untracked files.")
        ], "Add pattern", async () => { await _repo.IgnoreAsync(pattern.Text ?? ""); await Refresh(); _status.Text = "Added to .gitignore: " + pattern.Text; });
    }

    async Task MoveFileDialog(GitChange file) {
        if (_repo == null) return;
        var destination = new TextBox { Text = file.Path };
        await FormDialog("Rename or move", [
            Field("New path", destination, "Relative to the repository root. Folders are created as needed."),
            Paragraph("Git records this as a rename, so the file keeps its history.")
        ], "Rename file", async () => { await _repo.MoveFileAsync(file.Path, destination.Text ?? ""); await Refresh(); _status.Text = $"{file.Path} renamed to {destination.Text}."; });
    }
}
