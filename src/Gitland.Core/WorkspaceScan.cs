namespace Gitland.Core;

/// <summary>One repository's line in the Workspace table.</summary>
public sealed record RepoSummary(string Name, string Path, string Branch, int Added, int Modified, int Deleted, int Conflicts, int Ahead, int Behind, int Remotes, string? Error = null) {
    public bool IsDirty => Added > 0 || Modified > 0 || Deleted > 0 || Conflicts > 0;
}

/// <summary>Reads the state of every repository sitting directly inside one folder, so a whole
/// workspace can be reviewed without opening each repository in turn.</summary>
public static class WorkspaceScan {
    /// <summary>The direct subfolders that are Git repositories, ordered by name. A linked worktree
    /// and a submodule checkout carry <c>.git</c> as a file rather than a folder, so both count.</summary>
    public static IReadOnlyList<string> FindRepositories(string root) {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return [];
        try {
            return Directory.EnumerateDirectories(root)
                .Where(child => Directory.Exists(System.IO.Path.Combine(child, ".git")) || File.Exists(System.IO.Path.Combine(child, ".git")))
                .Order(StringComparer.OrdinalIgnoreCase).ToArray();
        } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }

    /// <summary>Reads one repository. A repository that cannot be read comes back as a row carrying
    /// the reason rather than as an exception, so one bad folder never blanks the table.</summary>
    public static async Task<RepoSummary> ReadAsync(string path, ICommandRunner? runner = null) {
        string name = System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(path));
        try {
            var repo = new GitRepository(path, runner);
            // --no-optional-locks keeps a read-only scan from rewriting the index of a repository the
            // user may have open in another tool.
            string status = await repo.Git("--no-optional-locks", "status", "--branch", "--porcelain=v1", "-z", "--untracked-files=all");
            string remotes = await repo.Git("remote");

            int end = status.IndexOf('\0');
            string header = end < 0 ? status : status[..end];
            var (branch, ahead, behind) = ParseBranch(header.StartsWith("## ", StringComparison.Ordinal) ? header[3..] : "");
            int added = 0, modified = 0, deleted = 0, conflicts = 0;
            foreach (var change in GitRepository.ParseStatus(end < 0 ? "" : status[(end + 1)..])) {
                if (change.IsConflict) conflicts++;
                else switch (change.Label) {
                    case "New": added++; break;
                    case "Deleted": deleted++; break;
                    case "Unchanged": break;
                    default: modified++; break;
                }
            }
            int remoteCount = remotes.Split('\n', StringSplitOptions.RemoveEmptyEntries).Count(line => line.Trim().Length > 0);
            return new(name, path, branch, added, modified, deleted, conflicts, ahead, behind, remoteCount);
        } catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException or TimeoutException) {
            return new(name, path, "", 0, 0, 0, 0, 0, 0, 0, e.Message.Trim());
        }
    }

    /// <summary>Reads every repository in the folder, a few at a time. Scanning forty repositories
    /// at once would start eighty git processes; the rows come back in name order regardless.</summary>
    public static async Task<IReadOnlyList<RepoSummary>> ScanAsync(string root, ICommandRunner? runner = null, int parallelism = 4) {
        var paths = FindRepositories(root);
        var rows = new RepoSummary[paths.Count];
        using var gate = new SemaphoreSlim(Math.Max(1, parallelism));
        await Task.WhenAll(paths.Select(async (path, index) => {
            await gate.WaitAsync();
            try { rows[index] = await ReadAsync(path, runner); } finally { gate.Release(); }
        }));
        return rows;
    }

    /// <summary>Reads the <c>## …</c> line of a porcelain status. Git writes five shapes here:
    /// <c>main...origin/main [ahead 2, behind 1]</c>, the same without the bracket, a bare
    /// <c>solo</c> for a branch with no upstream, <c>No commits yet on main</c>, and
    /// <c>HEAD (no branch)</c> when the checkout is detached.</summary>
    static (string Branch, int Ahead, int Behind) ParseBranch(string header) {
        if (header == "HEAD (no branch)") return ("HEAD (detached)", 0, 0);
        const string fresh = "No commits yet on ";
        if (header.StartsWith(fresh, StringComparison.Ordinal)) return (header[fresh.Length..], 0, 0);
        int track = header.IndexOf("...", StringComparison.Ordinal);
        if (track < 0) return (header, 0, 0);
        string branch = header[..track];
        int open = header.IndexOf('[', track);
        if (open < 0 || !header.EndsWith(']')) return (branch, 0, 0);
        int ahead = 0, behind = 0;
        // The bracket also carries "gone" for a deleted upstream, which counts as neither.
        foreach (string part in header[(open + 1)..^1].Split(", ")) {
            if (part.StartsWith("ahead ", StringComparison.Ordinal)) int.TryParse(part[6..], out ahead);
            else if (part.StartsWith("behind ", StringComparison.Ordinal)) int.TryParse(part[7..], out behind);
        }
        return (branch, ahead, behind);
    }
}
