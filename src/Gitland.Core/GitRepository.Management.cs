using System.Text;
using System.Text.RegularExpressions;

namespace Gitland.Core;

public sealed record GitCommit(string Hash, string ShortHash, string Author, string Date, string Subject, string Decorations, string Parents = "");
public sealed record GitBranch(string Name, string Hash, string Upstream, bool Current);
public sealed record GitTag(string Name, string Commit, string Subject);
public sealed record GitRemote(string Name, string Url);
public sealed record ManagementState(IReadOnlyList<GitCommit> Commits, IReadOnlyList<GitBranch> Branches, IReadOnlyList<GitTag> Tags, IReadOnlyList<GitRemote> Remotes, string IndexTree, string? Head, bool MergeInProgress);
public sealed record RepositoryCreation(string Path, string Branch = "main", bool Readme = true, string IgnoreTemplate = "none");

public sealed partial class GitRepository {
    public static async Task<GitRepository> InitializeAsync(RepositoryCreation options) {
        if (string.IsNullOrWhiteSpace(options.Path)) throw new InvalidOperationException("Choose a repository folder.");
        ValidateRefName(options.Branch);
        string root = Path.GetFullPath(options.Path);
        if (File.Exists(Path.Combine(root, ".git")) || Directory.Exists(Path.Combine(root, ".git"))) throw new InvalidOperationException("This folder is already a Git repository.");
        Directory.CreateDirectory(root);
        var repo = new GitRepository(root);
        var parent = await repo.RunResultAsync(["rev-parse", "--show-toplevel"]);
        if (parent.ExitCode == 0) throw new InvalidOperationException("This folder is already inside a Git repository. Open that repository instead.");
        if (parent.ExitCode != 128 || !parent.Error.Contains("not a git repository", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException(parent.Error.Trim());
        await repo.Git("check-ref-format", "refs/heads/" + options.Branch);
        await repo.Git("init", "--initial-branch=" + options.Branch);
        if (options.Readme) await WriteNew(Path.Combine(root, "README.md"), "# " + Path.GetFileName(root) + "\n");
        string ignore = options.IgnoreTemplate switch { "dotnet" => "**/bin/\n**/obj/\n.vs/\n*.user\n", "node" => "node_modules/\ndist/\n.env\n.env.*\n!.env.example\n", _ => "" };
        if (ignore.Length > 0) await WriteNew(Path.Combine(root, ".gitignore"), ignore);
        return repo;
    }
    static async Task WriteNew(string path, string text) {
        if (File.Exists(path)) return;
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false)); await writer.WriteAsync(text);
    }
    public static void ValidateRefName(string name) {
        if (string.IsNullOrWhiteSpace(name) || name.StartsWith('-') || name.StartsWith('/') || name.EndsWith('/') || name.EndsWith('.') || name.Contains("..") || name.Contains("@{") || name.Contains("//") || name == "@" || name.Split('/').Any(p => p.StartsWith('.') || p.EndsWith(".lock", StringComparison.OrdinalIgnoreCase)) || name.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || "~^:?*[\\".Contains(c)))
            throw new InvalidOperationException("Use a valid Git branch or tag name, such as feature/search or v1.2.0.");
    }
    static void ValidateRemoteName(string name) { if (!Regex.IsMatch(name, @"\A[A-Za-z0-9][A-Za-z0-9._-]*\z")) throw new InvalidOperationException("Choose a valid remote name."); }
    public async Task<ManagementState> ReadManagementAsync(bool allBranches = false, int historyLimit = 200) {
        var state = await ReadStateAsync();
        var commits = new List<GitCommit>(); string? head = null;
        if (await HasHead()) {
            head = await ResolveRef("HEAD");
            var arguments = new List<string> { "log", "-" + Math.Clamp(historyLimit, 1, 2000), "--topo-order", "--date=iso-strict", "--format=%H%x00%h%x00%an%x00%aI%x00%s%x00%D%x00%P%x00" };
            if (allBranches) arguments.AddRange(["--branches", "--remotes", "--tags"]);
            arguments.Add("HEAD"); arguments.Add("--");
            var fields = (await Git(arguments.ToArray())).Split('\0');
            for (int i = 0; i + 6 < fields.Length; i += 7) commits.Add(new(fields[i].Trim(), fields[i + 1], fields[i + 2], fields[i + 3], fields[i + 4], fields[i + 5], fields[i + 6]));
        }
        var branches = new List<GitBranch>();
        var refs = (await Git("for-each-ref", "--format=%(refname:short)%00%(objectname)%00%(upstream:short)%00%(HEAD)%00", "refs/heads")).Split('\0');
        for (int i = 0; i + 3 < refs.Length; i += 4) branches.Add(new(refs[i].Trim(), refs[i + 1], refs[i + 2], refs[i + 3] == "*"));
        var tags = new List<GitTag>();
        var tagFields = (await Git("for-each-ref", "--sort=-creatordate", "--format=%(refname:strip=2)%00%(*objectname)%00%(objectname)%00%(subject)%00", "refs/tags")).Split('\0');
        for (int i = 0; i + 3 < tagFields.Length; i += 4) tags.Add(new(tagFields[i].Trim(), tagFields[i + 1].Length == 0 ? tagFields[i + 2] : tagFields[i + 1], tagFields[i + 3]));
        string indexTree = state.Changes.Any(c => c.IsConflict) ? "" : (await Git("write-tree")).Trim();
        bool merging = (await RunResultAsync(["rev-parse", "-q", "--verify", "MERGE_HEAD"])).ExitCode == 0;
        return new(commits, branches, tags, await ReadRemotesAsync(), indexTree, head, merging);
    }
    public async Task<IReadOnlyList<GitRemote>> ReadRemotesAsync() {
        var remotes = new List<GitRemote>();
        foreach (var name in (await Git("remote")).Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()))
            remotes.Add(new(name, (await Git("remote", "get-url", "--", name)).Trim()));
        return remotes;
    }
    public async Task StageAllAsync() {
        if ((await ReadStateAsync()).Changes.Any(c => c.IsConflict)) throw new InvalidOperationException("Resolve conflicts before staging all files.");
        await Git("add", "--all", "--", ".");
    }
    public async Task<string> CommitAsync(string message, string expectedTree, string? expectedHead, string? expectedBranch = null) {
        if (string.IsNullOrWhiteSpace(message)) throw new InvalidOperationException("Write a commit message.");
        var management = await ReadManagementAsync();
        if (management.IndexTree.Length == 0) throw new InvalidOperationException("Resolve all conflicts before committing.");
        if (management.IndexTree != expectedTree || management.Head != expectedHead) throw new InvalidOperationException("The index or HEAD changed. Refresh and review the staged files before committing.");
        var status = await ReadStateAsync();
        if (expectedBranch != null && status.Branch != expectedBranch) throw new InvalidOperationException("The current branch changed. Refresh the file summary before committing.");
        if (!status.Changes.Any(c => c.IsStaged) && !management.MergeInProgress) throw new InvalidOperationException("Stage at least one change before committing.");
        await RunAsync(["commit", "--file=-"], message, timeout: 120);
        return await ResolveRef("HEAD");
    }
    public async Task CreateBranchAsync(string name, string revision, bool switchTo) {
        ValidateRefName(name); await Git("check-ref-format", "refs/heads/" + name);
        string sha = await ResolveRef(revision);
        if (switchTo && (await ReadStateAsync()).Changes.Count > 0) throw new InvalidOperationException("Commit or stash your working changes before switching branches.");
        if (switchTo) await Git("switch", "-c", name, sha); else await Git("branch", "--", name, sha);
    }
    public async Task SwitchBranchAsync(string name) {
        ValidateRefName(name);
        if ((await ReadStateAsync()).Changes.Count > 0) throw new InvalidOperationException("Commit or stash your working changes before switching branches.");
        await Git("switch", "--", name);
    }
    public async Task CreateTagAsync(string name, string target, string message) {
        ValidateRefName(name); await Git("check-ref-format", "refs/tags/" + name);
        if (string.IsNullOrWhiteSpace(message)) throw new InvalidOperationException("Add an annotation for this tag.");
        string commit = await ResolveRef(target);
        await RunAsync(["tag", "--annotate", "--file=-", "--", name, commit], message);
    }
    public async Task FetchAsync(string remote) { ValidateRemoteName(remote); await RunAsync(["fetch", "--", remote], timeout: 120); }
    public async Task PullAsync() {
        if ((await ReadStateAsync()).Changes.Count > 0) throw new InvalidOperationException("Commit or stash your changes before pulling.");
        await RunAsync(["pull", "--ff-only", "--no-rebase"], timeout: 120);
    }
    public async Task AddRemoteAsync(string name, string url) { ValidateRemoteName(name); ValidateRemoteUrl(url); await Git("remote", "add", "--", name, url); }
    public async Task PushBranchAsync(string remote, string branch, string expectedHead) {
        ValidateRemoteName(remote); ValidateRefName(branch);
        var state = await ReadStateAsync();
        if (state.Branch != branch || await ResolveRef("HEAD") != expectedHead) throw new InvalidOperationException("The current branch changed. Refresh before pushing.");
        await RunAsync(["-c", "push.followTags=false", "push", "--porcelain", "--set-upstream", "--", remote, $"refs/heads/{branch}:refs/heads/{branch}"], timeout: 120);
    }
    public async Task PushTagAsync(string remote, string tag, string expectedCommit) {
        ValidateRemoteName(remote); ValidateRefName(tag);
        if (await ResolveRef("refs/tags/" + tag) != expectedCommit) throw new InvalidOperationException("This tag changed. Refresh before pushing.");
        await RunAsync(["-c", "push.followTags=false", "push", "--porcelain", "--", remote, $"refs/tags/{tag}:refs/tags/{tag}"], timeout: 120);
    }
    public async Task<string?> RemoteTagCommitAsync(string remote, string tag) {
        ValidateRemoteName(remote); ValidateRefName(tag);
        var refs = (await RunAsync(["ls-remote", "--tags", "--", remote, "refs/tags/" + tag, "refs/tags/" + tag + "^{}"], timeout: 120)).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return refs.FirstOrDefault(s => s.EndsWith("refs/tags/" + tag + "^{}"))?.Split('\t')[0] ?? refs.FirstOrDefault(s => s.EndsWith("refs/tags/" + tag))?.Split('\t')[0];
    }
    async Task<string> ReconstructMergeAsync(string ancestor, string ours, string theirs) {
        string directory = Path.Combine(Path.GetTempPath(), "gitland-merge-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        string a = Path.Combine(directory, "ours"), b = Path.Combine(directory, "base"), c = Path.Combine(directory, "theirs");
        try {
            await File.WriteAllTextAsync(a, ours, new UTF8Encoding(false)); await File.WriteAllTextAsync(b, ancestor, new UTF8Encoding(false)); await File.WriteAllTextAsync(c, theirs, new UTF8Encoding(false));
            var result = await RunResultAsync(["merge-file", "--stdout", "--diff3", "--", a, b, c]);
            if (result.ExitCode < 0 || result.ExitCode > 127) throw new InvalidOperationException(result.Error.Trim());
            return result.Output;
        } finally {
            // Only these three files, created by this call, are removed.
            foreach (var file in new[] { a, b, c }) if (File.Exists(file)) File.Delete(file);
            Directory.Delete(directory);
        }
    }
}
