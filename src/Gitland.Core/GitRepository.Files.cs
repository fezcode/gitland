using System.Text;

namespace Gitland.Core;

public sealed partial class GitRepository {
    /// <summary>Renames or moves a tracked file, keeping Git's rename detection intact.</summary>
    public async Task MoveFileAsync(string from, string to) {
        ValidatePath(from); string destination = ValidatePath(to);
        if (string.Equals(from, to, StringComparison.Ordinal)) throw new InvalidOperationException("Choose a different name.");
        if (File.Exists(destination) || Directory.Exists(destination)) throw new InvalidOperationException("Something already exists at that path.");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await RunAsync(["mv", "--", from, to]);
    }

    /// <summary>Appends a pattern to the repository's .gitignore, creating it when absent.</summary>
    public async Task IgnoreAsync(string pattern) {
        pattern = pattern.Trim();
        if (pattern.Length == 0 || pattern.Contains('\n') || pattern.Contains('\0')) throw new InvalidOperationException("Write a single ignore pattern.");
        string full = Path.Combine(Root, ".gitignore");
        // Read as bytes so an existing file's encoding and trailing newline are preserved exactly.
        string existing = File.Exists(full) ? await File.ReadAllTextAsync(full) : "";
        if (existing.Split('\n').Any(l => l.TrimEnd('\r').Trim() == pattern)) return;
        string newline = existing.Contains("\r\n") ? "\r\n" : "\n";
        string prefix = existing.Length == 0 || existing.EndsWith('\n') ? "" : newline;
        await File.AppendAllTextAsync(full, prefix + pattern + newline, new UTF8Encoding(false));
    }

    /// <summary>Applies a run of commits in order, stopping at the first one that conflicts.</summary>
    public async Task<GitOperationResult> CherryPickRangeAsync(IReadOnlyList<string> revisions, string expectedHead) {
        if (revisions.Count == 0) throw new InvalidOperationException("Select at least one commit.");
        await RequireClean(); await CheckHead(expectedHead);
        var targets = new List<string>();
        foreach (string revision in revisions) targets.Add(await ResolveRef(revision));
        string recovery = await RecoveryRef(expectedHead, "cherry-pick");
        var result = await RunResultAsync(["-c", "core.editor=true", "cherry-pick", "--no-edit", .. targets], timeout: 120);
        if (result.ExitCode == 0) return new(true, $"Applied {targets.Count} commit{(targets.Count == 1 ? "" : "s")}.", recovery);
        if (await OperationAsync() != "" || (await ReadStateAsync()).Changes.Any(c => c.IsConflict))
            return new(false, "Cherry-pick needs conflict resolution. Resolve the files, then Continue.", recovery);
        throw new CommandFailedException(result.Error.Trim(), result.ExitCode);
    }

    /// <summary>Reverts a commit. A merge commit needs the parent to keep, which -m selects.</summary>
    public async Task<GitOperationResult> RevertCommitAsync(string revision, string expectedHead, int mainline = 0) {
        await RequireClean(); await CheckHead(expectedHead);
        string target = await ResolveRef(revision);
        int parents = (await Git("rev-list", "--parents", "-n", "1", target)).Trim().Split(' ').Length - 1;
        if (parents > 1 && mainline < 1) throw new InvalidOperationException("This is a merge commit. Choose which parent's history to keep.");
        if (mainline > parents) throw new InvalidOperationException("That parent does not exist on this commit.");
        string recovery = await RecoveryRef(expectedHead, "revert");
        string[] pick = parents > 1 ? ["-m", mainline.ToString()] : [];
        var result = await RunResultAsync(["-c", "core.editor=true", "revert", "--no-edit", .. pick, target], timeout: 120);
        if (result.ExitCode == 0) return new(true, "Revert completed.", recovery);
        if (await OperationAsync() != "" || (await ReadStateAsync()).Changes.Any(c => c.IsConflict))
            return new(false, "Revert needs conflict resolution. Resolve the files, then Continue.", recovery);
        throw new CommandFailedException(result.Error.Trim(), result.ExitCode);
    }

    /// <summary>Resolves a conflict by taking one side of it wholesale, without opening the editor.</summary>
    public async Task TakeSideAsync(string path, bool ours) {
        ValidatePath(path);
        var state = await ReadStateAsync();
        var change = state.Changes.FirstOrDefault(c => c.Path == path) ?? throw new InvalidOperationException("This file has no changes.");
        if (!change.IsConflict) throw new InvalidOperationException("This file is not conflicted.");
        // A side missing from the index means that side deleted the file, so honour the deletion.
        string stages = await Git("ls-files", "-u", "--", path);
        bool present = stages.Contains(ours ? " 2\t" : " 3\t");
        if (present) { await RunAsync(["checkout", ours ? "--ours" : "--theirs", "--", path]); await Git("add", "--", path); }
        else await RunAsync(["rm", "-f", "--", path]);
    }

    /// <summary>Writes the changes of a commit, or of the working tree, out as a patch file.</summary>
    public async Task<string> ExportPatchAsync(string revision, string destination) {
        string full = Path.GetFullPath(destination);
        string patch = revision.Length == 0
            ? await Git("diff", "--no-ext-diff", "--no-textconv", "--no-color", "HEAD", "--")
            : await Git("format-patch", "-1", "--stdout", await ResolveRef(revision));
        if (patch.Length == 0) throw new InvalidOperationException("There is nothing to export.");
        await File.WriteAllTextAsync(full, patch, new UTF8Encoding(false));
        return full;
    }

    /// <summary>Applies a patch file to the working tree, checking it first so a bad patch changes nothing.</summary>
    public async Task ApplyPatchAsync(string source) {
        string full = Path.GetFullPath(source);
        if (!File.Exists(full)) throw new InvalidOperationException("Choose a patch file that exists.");
        string patch = await File.ReadAllTextAsync(full);
        if (patch.Length == 0) throw new InvalidOperationException("That patch file is empty.");
        await RunAsync(["apply", "--check", "--whitespace=nowarn", "-"], patch);
        await RunAsync(["apply", "--whitespace=nowarn", "-"], patch);
    }
}
