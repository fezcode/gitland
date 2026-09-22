namespace Gitland.Core;

public sealed record GitStash(string Reference, string Hash, string Subject);
public sealed record GitWorktree(string Path, string Head, string Branch, bool Locked);
public sealed record GitRecovery(string Reference, string Hash, string Subject);
public sealed record RepositoryTools(IReadOnlyList<GitStash> Stashes, IReadOnlyList<GitWorktree> Worktrees, IReadOnlyList<GitRecovery> Recovery, string Operation);
public sealed record GitOperationResult(bool Completed, string Message, string RecoveryRef = "");

public sealed partial class GitRepository {
    public static async Task<GitRepository> CloneAsync(string url, string destination) {
        ValidateRemoteUrl(url);
        string path = Path.GetFullPath(destination);
        if (File.Exists(path) || Directory.Exists(path)) throw new InvalidOperationException("Choose a new destination folder for the clone.");
        string parent = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Choose a destination inside a folder.");
        if (!Directory.Exists(parent)) throw new InvalidOperationException("The destination's parent folder must already exist.");
        await new GitRepository(parent).RunAsync(["clone", "--", url, path], timeout: 300);
        return await OpenAsync(path);
    }
    public static void ValidateRemoteUrl(string url) {
        if (string.IsNullOrWhiteSpace(url) || url.StartsWith('-') || url.Contains("::") || url.Any(char.IsControl)) throw new InvalidOperationException("Enter an HTTPS, SSH, or local repository address.");
    }
    async Task CheckHead(string expected) {
        if (await ResolveRef("HEAD") != expected) throw new InvalidOperationException("HEAD changed. Refresh and review the operation again.");
    }
    async Task RequireClean() {
        if ((await ReadStateAsync()).Changes.Count != 0) throw new InvalidOperationException("Commit or stash all local changes before this operation.");
        if (await OperationAsync() != "") throw new InvalidOperationException("Finish or abort the current Git operation first.");
    }
    public async Task<string> OperationAsync() {
        foreach (var (marker, kind) in new[] { ("rebase-merge", "rebase"), ("rebase-apply", "rebase"), ("MERGE_HEAD", "merge"), ("CHERRY_PICK_HEAD", "cherry-pick"), ("REVERT_HEAD", "revert") }) {
            string path = (await Git("rev-parse", "--git-path", marker)).Trim(); if (!Path.IsPathRooted(path)) path = Path.Combine(Root, path);
            if (File.Exists(path) || Directory.Exists(path)) return kind;
        }
        return "";
    }
    async Task<string> RecoveryRef(string hash, string reason) {
        string reference = $"refs/gitland/recovery/{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}-{reason}";
        await Git("update-ref", reference, hash, new string('0', hash.Length)); return reference;
    }
    public async Task<RepositoryTools> ReadToolsAsync() {
        var stashes = new List<GitStash>();
        var fields = (await Git("stash", "list", "--format=%gd%x00%H%x00%gs%x00")).Split('\0');
        for (int i = 0; i + 2 < fields.Length; i += 3) stashes.Add(new(fields[i].Trim(), fields[i + 1], fields[i + 2]));
        var trees = new List<GitWorktree>(); string? path = null; string head = "", branch = ""; bool locked = false;
        var listing = await RunResultAsync(["worktree", "list", "--porcelain", "-z"]);
        bool legacy = listing.ExitCode == 129;
        if (listing.ExitCode != 0 && !legacy) throw new CommandFailedException(listing.Error, listing.ExitCode);
        string worktrees = legacy ? await Git("worktree", "list", "--porcelain") : listing.Output;
        foreach (string raw in worktrees.Split(legacy ? '\n' : '\0')) {
            string item = legacy ? raw.TrimEnd('\r') : raw;
            if (item.StartsWith("worktree ")) { if (path != null) trees.Add(new(path, head, branch, locked)); path = item[9..]; if (legacy && path.StartsWith('"')) path = System.Text.Json.JsonSerializer.Deserialize<string>(path)!; head = branch = ""; locked = false; }
            else if (item.StartsWith("HEAD ")) head = item[5..]; else if (item.StartsWith("branch ")) branch = item[7..].Replace("refs/heads/", ""); else if (item.StartsWith("locked")) locked = true;
        }
        if (path != null) trees.Add(new(path, head, branch, locked));
        var recovery = new List<GitRecovery>();
        var refs = (await Git("for-each-ref", "--sort=-refname", "--format=%(refname)%00%(objectname)%00%(subject)%00", "refs/gitland/recovery")).Split('\0');
        for (int i = 0; i + 2 < refs.Length; i += 3) recovery.Add(new(refs[i].Trim(), refs[i + 1], refs[i + 2]));
        return new(stashes, trees, recovery, await OperationAsync());
    }
    public async Task RenameBranchAsync(string oldName, string newName, string expectedHash) {
        ValidateRefName(oldName); ValidateRefName(newName);
        if (await ResolveRef("refs/heads/" + oldName) != expectedHash) throw new InvalidOperationException("This branch changed. Refresh before renaming it.");
        await Git("branch", "-m", oldName, newName);
    }
    public async Task DeleteBranchAsync(string name, string expectedHash, bool force = false) {
        ValidateRefName(name);
        if (await ResolveRef("refs/heads/" + name) != expectedHash) throw new InvalidOperationException("This branch changed. Refresh before deleting it.");
        await RecoveryRef(expectedHash, "branch"); await Git("branch", force ? "-D" : "-d", "--", name);
    }
    public async Task UpdateRemoteAsync(string oldName, string newName, string url, string expectedUrl) {
        ValidateRemoteName(oldName); ValidateRemoteName(newName); ValidateRemoteUrl(url);
        if ((await Git("remote", "get-url", "--", oldName)).Trim() != expectedUrl) throw new InvalidOperationException("This remote changed. Refresh first.");
        // Set the URL before renaming; each operation is independently valid if a later step fails.
        if (newName != oldName && (await ReadRemotesAsync()).Any(r => r.Name == newName)) throw new InvalidOperationException("A remote with that name already exists.");
        await Git("remote", "set-url", "--", oldName, url);
        if (newName != oldName) await Git("remote", "rename", oldName, newName);
    }
    public async Task DeleteRemoteAsync(string name, string expectedUrl) {
        ValidateRemoteName(name);
        if ((await Git("remote", "get-url", "--", name)).Trim() != expectedUrl) throw new InvalidOperationException("This remote changed. Refresh first.");
        await Git("remote", "remove", name);
    }
    public async Task<string> TagObjectAsync(string name) { ValidateRefName(name); return (await Git("rev-parse", "--verify", "--end-of-options", "refs/tags/" + name)).Trim(); }
    public async Task UpdateTagAsync(string name, string revision, string message, string expectedObject) {
        ValidateRefName(name);
        if (string.IsNullOrWhiteSpace(message)) throw new InvalidOperationException("Write a tag annotation.");
        string commit = await ResolveRef(revision), identity = (await Git("var", "GIT_COMMITTER_IDENT")).Trim();
        string tag = (await RunAsync(["mktag"], $"object {commit}\ntype commit\ntag {name}\ntagger {identity}\n\n{message}\n")).Trim();
        if (await TagObjectAsync(name) != expectedObject) throw new InvalidOperationException("The tag changed. Refresh first.");
        await RecoveryRef(expectedObject, "tag-edit");
        await Git("update-ref", "refs/tags/" + name, tag, expectedObject);
    }
    public async Task DeleteTagAsync(string name, string expectedObject) {
        ValidateRefName(name);
        if (await TagObjectAsync(name) != expectedObject) throw new InvalidOperationException("The tag changed. Refresh first.");
        await RecoveryRef(expectedObject, "tag"); await Git("update-ref", "-d", "refs/tags/" + name, expectedObject);
    }
    public async Task SaveStashAsync(string message, bool includeUntracked) {
        if (await OperationAsync() != "" || (await ReadStateAsync()).Changes.Any(c => c.IsConflict)) throw new InvalidOperationException("Finish the current merge or rebase before stashing.");
        if (!await HasHead()) throw new InvalidOperationException("Create the repository's first commit before using a stash.");
        // Git 2.55 stashes an untracked file but leaves it in the working tree when
        // --literal-pathspecs is in force, so a "clean" checkout still holds the file the stash
        // claims to have taken. This call passes no pathspec, so the flag guards nothing here.
        await RunAsync(["stash", "push", includeUntracked ? "--include-untracked" : "--no-include-untracked", "-m", string.IsNullOrWhiteSpace(message) ? "Gitland stash" : message], literalPathspecs: false);
    }
    public async Task<GitOperationResult> ApplyStashAsync(string hash, bool dropAfter) {
        await RequireClean(); string pinned = await ResolveRef(hash);
        var result = await RunResultAsync(["stash", "apply", "--index", pinned]);
        if (result.ExitCode != 0) {
            if ((await ReadStateAsync()).Changes.Any(c => c.IsConflict)) return new(false, "Stash applied with conflicts. The stash has been kept.");
            throw new CommandFailedException(result.Error.Trim(), result.ExitCode);
        }
        if (dropAfter) await DropStashAsync(pinned);
        return new(true, dropAfter ? "Stash applied and removed from the stash list." : "Stash applied; its saved copy is retained.");
    }
    public async Task DropStashAsync(string hash) {
        var stash = (await ReadToolsAsync()).Stashes.FirstOrDefault(s => s.Hash == hash) ?? throw new InvalidOperationException("This stash is no longer in the list.");
        if (await ResolveRef(stash.Reference) != hash) throw new InvalidOperationException("The stash list changed. Refresh first.");
        await RecoveryRef(hash, "stash"); await Git("stash", "drop", stash.Reference);
    }
    public async Task<string> StashPatchAsync(string hash) => await Git("stash", "show", "--patch", "--include-untracked", "--no-ext-diff", "--no-textconv", await ResolveRef(hash));
    public async Task AddWorktreeAsync(string path, string revision, string? branch = null) {
        string destination = Path.GetFullPath(path); if (Directory.Exists(destination) || File.Exists(destination)) throw new InvalidOperationException("Choose a new folder for the worktree.");
        string commit = await ResolveRef(revision);
        if (string.IsNullOrWhiteSpace(branch)) await Git("worktree", "add", "--detach", "--", destination, commit);
        else { ValidateRefName(branch); await Git("worktree", "add", "-b", branch, "--", destination, commit); }
    }
    public async Task RemoveWorktreeAsync(string path) {
        var tree = (await ReadToolsAsync()).Worktrees.SingleOrDefault(w => Path.GetFullPath(w.Path).Equals(Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidOperationException("This worktree is no longer registered.");
        if (Path.GetFullPath(tree.Path).Equals(Root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Open a different worktree before removing this one.");
        if (tree.Locked) throw new InvalidOperationException("This worktree is locked.");
        await Git("worktree", "remove", "--", tree.Path); // No --force: Git rejects dirty/untracked worktrees.
    }
    public async Task<GitOperationResult> HistoryOperationAsync(string kind, string revision, string expectedHead) {
        if (kind is not ("merge" or "rebase" or "cherry-pick" or "revert")) throw new ArgumentException("Unsupported history operation.");
        await RequireClean(); await CheckHead(expectedHead); string target = await ResolveRef(revision);
        string recovery = await RecoveryRef(expectedHead, kind);
        string[] args = kind switch { "rebase" => ["-c", "core.editor=true", "rebase", target], _ => ["-c", "core.editor=true", kind, "--no-edit", target] };
        var result = await RunResultAsync(args, timeout: 120);
        if (result.ExitCode == 0) return new(true, kind + " completed.", recovery);
        if (await OperationAsync() != "" || (await ReadStateAsync()).Changes.Any(c => c.IsConflict)) return new(false, kind + " needs conflict resolution. Resolve the files, then Continue.", recovery);
        throw new CommandFailedException(result.Error.Trim(), result.ExitCode);
    }
    public async Task<GitOperationResult> FinishOperationAsync(string expectedKind, bool abort) {
        string kind = await OperationAsync();
        if (kind.Length == 0 || kind != expectedKind) throw new InvalidOperationException("The operation changed. Refresh first.");
        if (!abort && (await ReadStateAsync()).Changes.Any(c => c.IsConflict)) throw new InvalidOperationException("Resolve and stage every conflict first.");
        var result = await RunResultAsync(kind == "merge" && !abort ? ["-c", "core.editor=true", "commit", "--no-edit"] : ["-c", "core.editor=true", kind, abort ? "--abort" : "--continue"], timeout: 120);
        if (result.ExitCode != 0 && await OperationAsync() == "") throw new CommandFailedException(result.Error.Trim(), result.ExitCode);
        return new(result.ExitCode == 0, result.ExitCode == 0 ? (abort ? "Operation aborted." : "Operation completed.") : "More conflicts need resolution. " + result.Error.Trim());
    }
    public async Task<string> ResetKeepingFilesAsync(string revision, string expectedHead, bool keepStaged) {
        await CheckHead(expectedHead); if (await OperationAsync() != "") throw new InvalidOperationException("Finish the current operation first.");
        string target = await ResolveRef(revision); string recovery = await RecoveryRef(expectedHead, "reset");
        await Git("reset", keepStaged ? "--soft" : "--mixed", target, "--"); return recovery;
    }
    /// <summary>Rewrites the last commit. With <paramref name="includeStaged"/> the current index is
    /// folded into it; without, --only amends the message and leaves staged work for the next commit.</summary>
    public async Task AmendMessageAsync(string message, string expectedHead, bool includeStaged = false) {
        if (string.IsNullOrWhiteSpace(message)) throw new InvalidOperationException("Write a commit message.");
        await CheckHead(expectedHead); if (await OperationAsync() != "") throw new InvalidOperationException("Finish the current operation first.");
        if (includeStaged && (await ReadStateAsync()).Changes.Any(c => c.IsConflict)) throw new InvalidOperationException("Resolve every conflict before amending.");
        await RecoveryRef(expectedHead, "amend");
        string[] scope = includeStaged ? [] : ["--only"];
        await RunAsync(["commit", "--amend", .. scope, "--file=-"], message, timeout: 120);
    }
    public async Task<string> CommitDetailsAsync(string revision) => await Git("show", "--format=fuller", "--stat", "--no-ext-diff", "--no-textconv", await ResolveRef(revision), "--");
    public async Task<string> CommitMessageAsync(string revision) => await Git("log", "-1", "--format=%B", await ResolveRef(revision));
    public async Task<string> CommitPatchAsync(string revision) => await Git("show", "--format=", "--no-ext-diff", "--no-textconv", "--no-color", await ResolveRef(revision), "--");
}
