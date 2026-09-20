namespace Gitland.Core;

/// <summary>What a destructive working-tree operation removed, and where it was saved first.</summary>
/// <param name="RecoveryRef">The ref holding the tracked snapshot, or "" when there was nothing tracked to save.</param>
/// <param name="BackupDirectory">The folder holding copies of deleted untracked files, or "" when none were deleted.</param>
/// <remarks>Both are "" when the caller asked for a permanent removal, because nothing was kept.</remarks>
public sealed record DiscardResult(string RecoveryRef, string BackupDirectory, int Files);

public sealed partial class GitRepository {
    // Gitland's rule is that no operation loses work without leaving a way back. History
    // operations point a recovery ref at the old commit; a working-tree change has no commit
    // to point at, so one is made first. `stash create` writes a commit holding the index and
    // worktree without touching either, or the stash ref - exactly the snapshot needed here.
    async Task<string> SnapshotTrackedAsync(string reason) {
        string snapshot = (await Git("stash", "create", "Gitland " + reason)).Trim();
        return snapshot.Length == 0 ? "" : await RecoveryRef(snapshot, reason);
    }

    // `stash create` covers tracked content only, so untracked files about to be deleted are
    // copied out separately, beside the merge backups SaveMergeAsync already writes.
    async Task<string> BackupUntrackedAsync(IReadOnlyList<string> paths) {
        if (paths.Count == 0) return "";
        string gitDir = (await Git("rev-parse", "--absolute-git-dir")).Trim();
        string root = Path.Combine(gitDir, "gitland-backups");
        Directory.CreateDirectory(root);
        if (File.GetAttributes(root).HasFlag(FileAttributes.ReparsePoint)) throw new InvalidOperationException("The recovery directory must not be a symbolic link.");
        string folder = Path.Combine(root, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8] + "-discard");
        Directory.CreateDirectory(folder);
        foreach (string path in paths) {
            string full = ValidatePath(path);
            if (!File.Exists(full)) continue;
            string destination = Path.Combine(folder, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(full, destination, true);
        }
        return folder;
    }

    /// <summary>Splits the requested paths by whether Git is tracking them.</summary>
    async Task<(List<string> Tracked, List<string> Untracked)> PartitionAsync(IReadOnlyList<string> paths) {
        var tracked = new List<string>(); var untracked = new List<string>();
        var state = await ReadStateAsync();
        var known = state.Changes.ToDictionary(c => c.Path, StringComparer.Ordinal);
        foreach (string path in paths) {
            ValidatePath(path);
            if (known.TryGetValue(path, out var change) && change.Index == '?') untracked.Add(path); else tracked.Add(path);
        }
        return (tracked, untracked);
    }

    static void RequirePaths(IReadOnlyList<string> paths) {
        if (paths.Count == 0) throw new InvalidOperationException("Select at least one file to discard.");
    }

    /// <summary>Throws away edits that were never staged, keeping whatever is already in the index.
    /// With <paramref name="keepRecovery"/> false nothing is saved first and the work is unrecoverable.</summary>
    public async Task<DiscardResult> DiscardUnstagedAsync(IReadOnlyList<string> paths, bool keepRecovery = true) {
        RequirePaths(paths);
        var (tracked, untracked) = await PartitionAsync(paths);
        string recovery = keepRecovery ? await SnapshotTrackedAsync("discard") : "";
        string backup = keepRecovery ? await BackupUntrackedAsync(untracked) : "";
        if (tracked.Count > 0) await RunAsync(["restore", "--worktree", "--", .. tracked]);
        if (untracked.Count > 0) await RunAsync(["clean", "-f", "--", .. untracked]);
        return new(recovery, backup, paths.Count);
    }

    /// <summary>Returns the files to their committed state, discarding staged and unstaged work alike.
    /// With <paramref name="keepRecovery"/> false nothing is saved first and the work is unrecoverable.</summary>
    public async Task<DiscardResult> DiscardFileAsync(IReadOnlyList<string> paths, bool keepRecovery = true) {
        RequirePaths(paths);
        var (tracked, untracked) = await PartitionAsync(paths);
        string recovery = keepRecovery ? await SnapshotTrackedAsync("discard") : "";
        string backup = keepRecovery ? await BackupUntrackedAsync(untracked) : "";
        if (tracked.Count > 0) {
            // Without a commit there is nothing to restore from, so staged additions are simply
            // removed from the index and left in the working tree for the untracked sweep below.
            if (await HasHead()) await RunAsync(["restore", "--source=HEAD", "--staged", "--worktree", "--", .. tracked]);
            else { await RunAsync(["rm", "--cached", "-f", "--", .. tracked]); await RunAsync(["clean", "-f", "--", .. tracked]); }
        }
        if (untracked.Count > 0) await RunAsync(["clean", "-f", "--", .. untracked]);
        return new(recovery, backup, paths.Count);
    }

    /// <summary>Deletes untracked files, copying them out first unless a permanent removal was asked for.</summary>
    public async Task<DiscardResult> CleanUntrackedAsync(IReadOnlyList<string> paths, bool keepRecovery = true) {
        RequirePaths(paths);
        var (tracked, untracked) = await PartitionAsync(paths);
        if (tracked.Count > 0) throw new InvalidOperationException("Only untracked files can be deleted this way. Discard tracked files instead.");
        string backup = keepRecovery ? await BackupUntrackedAsync(untracked) : "";
        await RunAsync(["clean", "-f", "--", .. untracked]);
        return new("", backup, untracked.Count);
    }

    /// <summary>Reverses a single hunk out of the working tree, leaving the rest of the file alone.
    /// With <paramref name="keepRecovery"/> false nothing is saved first and the hunk is unrecoverable.</summary>
    public async Task<DiscardResult> DiscardHunkAsync(string path, string expectedPatch, int index, bool keepRecovery = true) {
        ValidatePath(path);
        if (await ReadPatchAsync(path, false) != expectedPatch) throw new InvalidOperationException("The file or index changed. Refresh before discarding this hunk.");
        var hunks = ParseHunks(expectedPatch);
        if (index < 0 || index >= hunks.Count) throw new InvalidOperationException("This hunk is no longer available.");
        string recovery = keepRecovery ? await SnapshotTrackedAsync("discard-hunk") : "";
        // --check first so a hunk that no longer applies fails before anything is written.
        await RunAsync(["apply", "--reverse", "--check", "--whitespace=nowarn", "-"], hunks[index].Patch);
        await RunAsync(["apply", "--reverse", "--whitespace=nowarn", "-"], hunks[index].Patch);
        return new(recovery, "", 1);
    }

    /// <summary>Returns the whole working tree to HEAD, optionally sweeping untracked files too.
    /// With <paramref name="keepRecovery"/> false nothing is saved first and the work is unrecoverable.</summary>
    public async Task<DiscardResult> DiscardEverythingAsync(bool includeUntracked, bool keepRecovery = true) {
        var state = await ReadStateAsync();
        var untracked = state.Changes.Where(c => c.Index == '?').Select(c => c.Path).ToArray();
        int files = state.Changes.Count(c => c.Index != '?') + (includeUntracked ? untracked.Length : 0);
        if (files == 0) throw new InvalidOperationException("There is nothing to discard.");
        string recovery = keepRecovery ? await SnapshotTrackedAsync("discard-all") : "";
        string backup = includeUntracked && keepRecovery ? await BackupUntrackedAsync(untracked) : "";
        if (await HasHead()) await RunAsync(["reset", "-q", "--hard", "HEAD", "--"]);
        else await RunAsync(["rm", "-q", "--cached", "-r", "-f", "--", "."]);
        if (includeUntracked) await RunAsync(["clean", "-f", "-d"]);
        return new(recovery, backup, files);
    }

    /// <summary>Moves the branch to another commit and matches the working tree to it.</summary>
    public async Task<string> ResetHardAsync(string revision, string expectedHead) {
        await CheckHead(expectedHead);
        if (await OperationAsync() != "") throw new InvalidOperationException("Finish the current operation first.");
        string target = await ResolveRef(revision);
        // Two refs: the commit being left behind, and the working-tree state being overwritten.
        string recovery = await RecoveryRef(expectedHead, "reset-hard");
        await SnapshotTrackedAsync("reset-hard");
        await Git("reset", "--hard", target, "--");
        return recovery;
    }
}
