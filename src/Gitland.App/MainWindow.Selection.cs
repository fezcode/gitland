using Gitland.Core;

namespace Gitland.App;

/// <summary>One ticked row. A partially staged file is drawn in both the Unstaged and Staged groups
/// and each row opens a different version, so the group is part of the identity: ticking app.ts under
/// Unstaged and under Staged are two separate selections.</summary>
/// <param name="Staged">true for a Staged-group row, false for Unstaged, null for an ungrouped list.</param>
public readonly record struct FileSelection(string Path, bool? Staged);

public sealed partial class MainWindow {
    readonly HashSet<FileSelection> _checked = [];

    bool IsChecked(GitChange file, bool? stagedView) => _checked.Contains(new(file.Path, stagedView));

    void ToggleChecked(GitChange file, bool? stagedView, bool ticked) {
        var key = new FileSelection(file.Path, stagedView);
        if (ticked) _checked.Add(key); else _checked.Remove(key);
        RenderFiles();
        UpdateSelectionStatus();
    }

    void ClearChecked() {
        if (_checked.Count == 0) return;
        _checked.Clear();
        RenderFiles();
        UpdateSelectionStatus();
    }

    /// <summary>Drops ticks whose row no longer exists. A background refresh must not silently wipe a
    /// selection, but it must not leave it pointing at files that have gone either.</summary>
    void PruneChecked() {
        if (_checked.Count == 0) return;
        var live = _state.AllFiles.Select(f => f.Path).ToHashSet(StringComparer.Ordinal);
        _checked.RemoveWhere(entry => !live.Contains(entry.Path));
    }

    void UpdateSelectionStatus() {
        if (_checked.Count > 0) _status.Text = $"{_checked.Count} file{(_checked.Count == 1 ? "" : "s")} selected · right-click for actions";
    }

    /// <summary>The rows a menu should act on: the whole selection when the clicked row is part of it,
    /// otherwise just the clicked row, which is what every file manager does.</summary>
    IReadOnlyList<GitChange> MenuTargets(GitChange file, bool? stagedView) {
        if (!IsChecked(file, stagedView)) return [file];
        var byPath = _state.AllFiles.ToDictionary(f => f.Path, StringComparer.Ordinal);
        return _checked.Select(entry => byPath.GetValueOrDefault(entry.Path)).OfType<GitChange>()
            .DistinctBy(f => f.Path, StringComparer.Ordinal).ToArray();
    }

    /// <summary>The ticked rows themselves, so an action can honour each row's own group.</summary>
    IReadOnlyList<FileSelection> MenuRows(GitChange file, bool? stagedView) =>
        IsChecked(file, stagedView) ? _checked.ToArray() : [new(file.Path, stagedView)];

    static string Count(int number, string singular, string plural) =>
        number == 1 ? singular : $"{number} {plural}";

    // ---- multi-file actions -------------------------------------------------

    async Task StageMany(IReadOnlyList<GitChange> files) {
        if (_repo == null || files.Count == 0) return;
        await _repo.StageFilesAsync(files.Select(f => f.Path).ToArray());
        ClearChecked();
        await Refresh();
        _status.Text = $"Staged {Count(files.Count, files[0].Path, "files")}.";
    }

    async Task UnstageMany(IReadOnlyList<GitChange> files) {
        if (_repo == null || files.Count == 0) return;
        await _repo.UnstageFilesAsync(files.Select(f => f.Path).ToArray());
        ClearChecked();
        await Refresh();
        _status.Text = $"Unstaged {Count(files.Count, files[0].Path, "files")}.";
    }

    /// <summary>Discards a set of rows, each by the rule its own group implies: an unstaged row loses
    /// only its unstaged edits, a staged row returns to its last commit, an untracked file is deleted.
    /// The confirmation spells that split out, because one word cannot mean three things silently.</summary>
    async Task DiscardMany(IReadOnlyList<FileSelection> rows) {
        if (_repo == null || rows.Count == 0) return;
        var byPath = _state.AllFiles.ToDictionary(f => f.Path, StringComparer.Ordinal);
        var unstaged = new List<string>(); var whole = new List<string>(); var untracked = new List<string>();
        foreach (var row in rows) {
            if (!byPath.TryGetValue(row.Path, out var file) || !file.IsChanged) continue;
            if (file.Index == '?') untracked.Add(file.Path);
            else if (row.Staged ?? (!file.IsUnstaged && file.IsStaged)) whole.Add(file.Path);
            else unstaged.Add(file.Path);
        }
        int total = unstaged.Count + whole.Count + untracked.Count;
        if (total == 0) { _status.Text = "Those files have no changes to discard."; return; }

        var parts = new List<string>();
        if (unstaged.Count > 0) parts.Add($"{Count(unstaged.Count, "1 file", "files")} lose their unstaged edits");
        if (whole.Count > 0) parts.Add($"{Count(whole.Count, "1 file", "files")} return to the last commit");
        if (untracked.Count > 0) parts.Add($"{Count(untracked.Count, "1 untracked file", "untracked files")} are deleted");
        string detail = char.ToUpper(parts[0][0]) + parts[0][1..] + (parts.Count > 1 ? ", and " + string.Join(", and ", parts.Skip(1)) : "") + ".";

        var (confirmed, permanent) = await ReviewActionWithOption(
            $"Discard {Count(total, "this change", "changes")}?",
            detail + " Gitland saves a snapshot under Recovery first, and copies deleted untracked files into the recovery folder.",
            $"Discard {Count(total, "change", "changes")}", "Discard permanently",
            "Discard permanently - keep no recovery snapshot or copies",
            "Nothing is saved anywhere. Every change discarded here is gone for good.");
        if (!confirmed) return;

        if (unstaged.Count > 0) await _repo.DiscardUnstagedAsync(unstaged, !permanent);
        if (whole.Count > 0) await _repo.DiscardFileAsync(whole, !permanent);
        if (untracked.Count > 0) await _repo.CleanUntrackedAsync(untracked, !permanent);
        ClearChecked();
        await Refresh();
        _status.Text = permanent
            ? $"{Count(total, "1 change", "changes")} removed permanently · nothing was kept"
            : $"{Count(total, "1 change", "changes")} discarded · recoverable from Repository → Recovery";
    }

    async Task TakeSideMany(IReadOnlyList<GitChange> files, bool ours) {
        if (_repo == null || files.Count == 0) return;
        string side = ours ? "ours" : "theirs";
        if (!await ReviewAction($"Take {side} for {Count(files.Count, "this file", "files")}?",
            $"Resolve {Count(files.Count, files[0].Path, "files")} by keeping the {(ours ? "version on this branch" : "incoming version")} in full, discarding the other side's changes. Each file is then staged as resolved.",
            $"Take {side}")) return;
        foreach (var file in files) await _repo.TakeSideAsync(file.Path, ours);
        ClearChecked();
        await Refresh();
        _status.Text = $"Resolved {Count(files.Count, files[0].Path, "files")} by taking {side}.";
    }

    async Task IgnoreMany(IReadOnlyList<GitChange> files) {
        if (_repo == null || files.Count == 0) return;
        if (files.Count == 1) { await IgnoreFrom(files[0]); return; }
        if (!await ReviewAction($"Add {files.Count} patterns to .gitignore?",
            "Each selected file's path is appended to .gitignore in the repository root. Already-tracked files keep being tracked.", "Add patterns")) return;
        foreach (var file in files) await _repo.IgnoreAsync(file.Path);
        ClearChecked();
        await Refresh();
        _status.Text = $"Added {files.Count} patterns to .gitignore.";
    }

    async Task CopyPaths(IReadOnlyList<GitChange> files, bool namesOnly) {
        var lines = files.Select(f => namesOnly ? System.IO.Path.GetFileName(f.Path) : f.Path).ToArray();
        await CopyText(string.Join(Environment.NewLine, lines),
            $"{Count(lines.Length, namesOnly ? "File name" : "Path", namesOnly ? "file names" : "paths")} copied.");
    }
}
