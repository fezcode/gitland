namespace Gitland.Core;

public sealed record CommitComparison(string Commit, IReadOnlyList<string> Parents, string? Parent, IReadOnlyList<GitChange> Files, string Message);

public sealed partial class GitRepository {
    public async Task<CommitComparison> ReadCommitComparisonAsync(string revision, int parentIndex = 0) {
        string commit = await ResolveRef(revision);
        var ancestry = (await Git("rev-list", "--parents", "-n", "1", commit, "--")).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var parents = ancestry.Skip(1).ToArray();
        if (parentIndex < 0 || parentIndex >= Math.Max(1, parents.Length)) throw new ArgumentOutOfRangeException(nameof(parentIndex));
        string? parent = parents.Length == 0 ? null : parents[parentIndex];
        IReadOnlyList<GitChange> files = parent == null
            ? (await Git("ls-tree", "-r", "--name-only", "-z", commit)).Split('\0', StringSplitOptions.RemoveEmptyEntries).Select(p => new GitChange(p, null, 'A', ' ')).ToArray()
            : await CompareChangesAsync(parent, commit);
        return new(commit, parents, parent, files, await CommitMessageAsync(commit));
    }

    public async Task<FileComparison> ReadCommitFileAsync(CommitComparison comparison, GitChange file) {
        if (!comparison.Files.Contains(file)) throw new InvalidOperationException("This file is not part of the selected commit comparison.");
        string left = comparison.Parent == null || file.Index == 'A' ? "" : await ReadObject(comparison.Parent + ":" + (file.OldPath ?? file.Path));
        string right = file.Index == 'D' ? "" : await ReadObject(comparison.Commit + ":" + file.Path);
        return new(file.Path, left, right, comparison.Parent ?? "Empty tree", comparison.Commit, Binary: left.Contains('\0') || right.Contains('\0'));
    }
}
