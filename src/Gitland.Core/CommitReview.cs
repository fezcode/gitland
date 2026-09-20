namespace Gitland.Core;

public sealed record CommitFileSummary(string Path, string? OldPath, char Status, int? Added, int? Removed) {
    public string Label => Status switch { 'A' => "Added", 'D' => "Deleted", 'R' => "Renamed", 'T' => "Type changed", _ => "Modified" };
    public bool Binary => Added == null || Removed == null;
}
public sealed record CommitReview(string Branch, string? Head, string IndexTree, bool MergeInProgress, IReadOnlyList<CommitFileSummary> Files, int Unstaged, int Conflicts) {
    public string MessageSummary() {
        static string Escape(string text) => text.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
        return "Changed files (staged)\n\n" + string.Join('\n', Files.Select(f => $"- {f.Label}: {Escape(f.OldPath == null ? f.Path : f.OldPath + " → " + f.Path)} ({(f.Binary ? "binary" : $"+{f.Added} / -{f.Removed}")})"));
    }
}
public sealed partial class GitRepository {
    public async Task<CommitReview> ReadCommitReviewAsync() {
        var management = await ReadManagementAsync();
        var state = await ReadStateAsync();
        var files = new List<CommitFileSummary>();
        if (management.IndexTree.Length > 0) {
            string basis = management.Head ?? (await RunAsync(["mktree"], "")).Trim();
            var statuses = (await Git("diff", "--name-status", "-z", "--find-renames", basis, management.IndexTree, "--")).Split('\0');
            var stats = (await Git("diff", "--numstat", "-z", "--find-renames", "--no-ext-diff", "--no-textconv", basis, management.IndexTree, "--")).Split('\0');
            var totals = new Dictionary<string, (int? Added, int? Removed)>(StringComparer.Ordinal);
            for (int i = 0; i < stats.Length && stats[i].Length > 0; i++) {
                var parts = stats[i].Split('\t', 3); if (parts.Length != 3) throw new InvalidOperationException("Could not read staged file statistics.");
                string path = parts[2];
                if (path.Length == 0) { i++; path = stats[++i]; }
                totals[path] = (int.TryParse(parts[0], out int added) ? added : null, int.TryParse(parts[1], out int removed) ? removed : null);
            }
            for (int i = 0; i + 1 < statuses.Length && statuses[i].Length > 0;) {
                char status = statuses[i++][0]; string path = statuses[i++]; string? old = null;
                if (status is 'R' or 'C') { old = path; path = statuses[i++]; }
                var total = totals.GetValueOrDefault(path);
                files.Add(new(path, old, status, total.Added, total.Removed));
            }
        }
        return new(state.Branch, management.Head, management.IndexTree, management.MergeInProgress, files, state.Changes.Count(c => c.IsUnstaged && !c.IsConflict), state.Changes.Count(c => c.IsConflict));
    }
}
