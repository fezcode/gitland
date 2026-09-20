using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Gitland.Core;

public sealed record GitChange(string Path, string? OldPath, char Index, char Worktree) {
    public bool IsChanged => Index != ' ' || Worktree != ' ';
    public bool IsConflict => Index == 'U' || Worktree == 'U' || new[] { "AA", "DD" }.Contains($"{Index}{Worktree}");
    public bool IsStaged => Index is not (' ' or '?') && !IsConflict;
    public bool IsUnstaged => Worktree != ' ' || Index == '?';
    public string Label => !IsChanged ? "Unchanged" : IsConflict ? "Conflict" : Index == '?' ? "New" : Worktree == 'D' || Index == 'D' ? "Deleted" : Index == 'R' ? "Renamed" : "Modified";
}
public sealed record RepositoryState(string Root, string Branch, IReadOnlyList<GitChange> Changes, IReadOnlyList<string> Refs, IReadOnlyList<GitChange>? Files = null) {
    public IReadOnlyList<GitChange> AllFiles => Files ?? Changes;
}
public sealed record FileComparison(string Path, string Left, string Right, string LeftLabel, string RightLabel, string Patch = "", bool Binary = false);
public sealed record PatchHunk(int Index, string Header, int RightLine, string Patch);
public sealed record FileSnapshot(string Text, string Hash, bool Bom, string Newline);
public sealed record MergeFile(string Path, string Base, string Ours, string Theirs, FileSnapshot Snapshot, MergeDocument Document, string StageSignature = "");

public sealed partial class GitRepository {
    public string Root { get; }
    public const int MaxBytes = 2 * 1024 * 1024;
    static readonly UTF8Encoding StrictUtf8 = new(false, true);
    readonly ICommandRunner _runner;
    public GitRepository(string root, ICommandRunner? runner = null) { Root = System.IO.Path.GetFullPath(root); _runner = runner ?? new CommandRunner(); }

    public static async Task<GitRepository> OpenAsync(string path) {
        var candidate = new GitRepository(path);
        var root = (await candidate.Git("rev-parse", "--show-toplevel")).Trim();
        return new(root);
    }

    public async Task<string> Git(params string[] args) => await RunAsync(args);
    async Task<CommandResult> RunResultAsync(string[] args, string? input = null, int timeout = 30, IReadOnlyDictionary<string, string>? environment = null) =>
        await _runner.RunAsync(new("git", Root, ["--literal-pathspecs", "-c", "core.quotepath=false", ..args], input, timeout, environment));
    async Task<string> RunAsync(string[] args, string? input = null, bool allowOne = false, int timeout = 30, IReadOnlyDictionary<string, string>? environment = null) {
        var result = await RunResultAsync(args, input, timeout, environment);
        if (result.ExitCode != 0 && !(allowOne && result.ExitCode == 1)) throw new CommandFailedException(string.IsNullOrWhiteSpace(result.Error) ? $"Git exited with code {result.ExitCode}. {result.Output.Trim()}" : result.Error.Trim(), result.ExitCode);
        return result.Output;
    }

    public async Task<RepositoryState> ReadStateAsync() {
        // Status must not rewrite the index while the parallel file inventory reads it.
        var statusTask = Git("--no-optional-locks", "status", "--porcelain=v1", "-z", "--untracked-files=all");
        var refsTask = Git("for-each-ref", "--format=%(refname:short)", "refs/heads", "refs/remotes", "refs/tags");
        var filesTask = Git("ls-files", "--cached", "--others", "--exclude-standard", "-z");
        string branch;
        try { branch = (await Git("symbolic-ref", "--short", "HEAD")).Trim(); }
        catch (InvalidOperationException) { branch = (await Git("rev-parse", "--short", "HEAD")).Trim() + " (detached)"; }
        var changes = ParseStatus(await statusTask);
        var changed = changes.ToDictionary(c => c.Path, StringComparer.Ordinal);
        var files = (await filesTask).Split('\0', StringSplitOptions.RemoveEmptyEntries).Concat(changes.Select(c => c.Path)).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).Select(path => changed.GetValueOrDefault(path) ?? new GitChange(path, null, ' ', ' ')).ToArray();
        return new(Root, branch, changes, (await refsTask).Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(s => s.TrimEnd('\r')).ToArray(), files);
    }

    public static IReadOnlyList<GitChange> ParseStatus(string output) {
        var entries = output.Split('\0'); var changes = new List<GitChange>();
        for (int i = 0; i < entries.Length; i++) {
            var entry = entries[i]; if (entry.Length < 4) continue;
            var path = entry[3..]; string? original = null;
            if (entry[0] is 'R' or 'C' || entry[1] is 'R' or 'C') original = entries[++i];
            changes.Add(new(path, original, entry[0], entry[1]));
        }
        return changes;
    }

    public async Task<FileComparison> WorkingDiffAsync(GitChange change, bool staged) {
        ValidatePath(change.Path);
        string left = "", right = "";
        if (staged) {
            if (change.Index != 'A' && await HasHead()) left = await ReadObject($"HEAD:{change.OldPath ?? change.Path}");
            if (change.Index != 'D') right = await ReadObject($":{change.Path}");
        } else {
            if (change.Index != '?' && change.Worktree != 'A') left = await ReadObject($":{change.Path}");
            if (change.Worktree != 'D') right = (await ReadWorkingFile(change.Path)).Text;
        }
        var patch = change.Index == '?' ? "" : await ReadPatchAsync(change.Path, staged);
        bool binary = left.Contains('\0') || right.Contains('\0');
        return new(change.Path, binary ? "" : left, binary ? "" : right, staged ? "HEAD" : "Index", staged ? "Index · staged" : "Working tree", patch, binary);
    }
    async Task<bool> HasHead() { try { await Git("rev-parse", "--verify", "HEAD"); return true; } catch (InvalidOperationException) { return false; } }
    public async Task<FileComparison> ConflictDiffAsync(string path) {
        ValidatePath(path);
        var stages = await Git("ls-files", "-u", "--", path);
        // A deleted side has no stage 2. The working file can also be absent for structural conflicts.
        string ours = Regex.IsMatch(stages, @" 2\t") ? await ReadObject($":2:{path}") : "";
        string working = File.Exists(ValidatePath(path)) ? (await ReadWorkingFile(path)).Text : "";
        return new(path, ours, working, "Ours · current branch", "Working tree · unresolved", Binary: ours.Contains('\0') || working.Contains('\0'));
    }
    async Task<string> ReadObject(string spec) {
        var size = await Git("cat-file", "-s", spec);
        if (!long.TryParse(size.Trim(), out long bytes) || bytes > MaxBytes) throw new InvalidOperationException("This file is larger than the 2 MB text comparison limit.");
        return await Git("show", "--no-ext-diff", "--no-textconv", spec);
    }
    public async Task<string> ReadPatchAsync(string path, bool staged) => await RunAsync(staged
        ? ["diff", "--cached", "--no-ext-diff", "--no-textconv", "--no-color", "--unified=3", "--", path]
        : ["diff", "--no-ext-diff", "--no-textconv", "--no-color", "--unified=3", "--", path]);

    public async Task<string> ResolveRef(string name) {
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Choose both revisions to compare.");
        return (await Git("rev-parse", "--verify", "--end-of-options", name + "^{commit}")).Trim();
    }
    public async Task<IReadOnlyList<GitChange>> CompareChangesAsync(string left, string right) {
        string a = await ResolveRef(left), b = await ResolveRef(right);
        var entries = (await Git("diff", "--name-status", "-z", "--find-renames", a, b, "--")).Split('\0');
        var changes = new List<GitChange>();
        for (int i = 0; i + 1 < entries.Length && entries[i].Length > 0;) {
            var status = entries[i++][0]; var path = entries[i++]; string? old = null;
            if (status is 'R' or 'C') { old = path; path = entries[i++]; }
            changes.Add(new(path, old, status, ' '));
        }
        return changes;
    }
    public async Task<FileComparison> CompareFileAsync(GitChange file, string left, string right) {
        var a = await ResolveRef(left); var b = await ResolveRef(right);
        string l = file.Index == 'A' ? "" : await ReadObject($"{a}:{file.OldPath ?? file.Path}");
        string r = file.Index == 'D' ? "" : await ReadObject($"{b}:{file.Path}");
        return new(file.Path, l, r, left, right, Binary: l.Contains('\0') || r.Contains('\0'));
    }

    public string ValidatePath(string path) {
        if (System.IO.Path.IsPathRooted(path) || path.Split('/', '\\').Any(p => p is ".." or ".git") || path.Contains('\0') || path.Contains(':')) throw new InvalidOperationException("The path must be a file inside the repository.");
        string full = System.IO.Path.GetFullPath(System.IO.Path.Combine(Root, path));
        var relative = System.IO.Path.GetRelativePath(Root, full);
        if (relative.StartsWith("..")) throw new InvalidOperationException("The path is outside the repository.");
        string? at = full;
        while (at != null && !string.Equals(at, Root, StringComparison.OrdinalIgnoreCase)) {
            if ((File.Exists(at) || Directory.Exists(at)) && File.GetAttributes(at).HasFlag(FileAttributes.ReparsePoint)) throw new InvalidOperationException("Symbolic links are not editable in Gitland.");
            at = System.IO.Path.GetDirectoryName(at);
        }
        return full;
    }
    public async Task<FileSnapshot> ReadWorkingFile(string path) {
        var full = ValidatePath(path);
        if (new FileInfo(full).Length > MaxBytes) throw new InvalidOperationException("This file is larger than the 2 MB text comparison limit.");
        var bytes = await File.ReadAllBytesAsync(full);
        bool bom = bytes.AsSpan().StartsWith(new byte[] { 239, 187, 191 });
        string text;
        try { text = StrictUtf8.GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0)); }
        catch (DecoderFallbackException) { throw new InvalidOperationException("This file is binary or uses an unsupported encoding. Gitland currently edits UTF-8 files only."); }
        return new(text, Convert.ToHexString(SHA256.HashData(bytes)), bom, text.Contains("\r\n") ? "\r\n" : "\n");
    }
    public async Task StageFileAsync(string path) { ValidatePath(path); await Git("add", "--", path); }
    public async Task UnstageFileAsync(string path, string? oldPath = null) {
        ValidatePath(path);
        if (oldPath != null) ValidatePath(oldPath);
        if (await HasHead()) await RunAsync(oldPath == null ? ["reset", "-q", "HEAD", "--", path] : ["reset", "-q", "HEAD", "--", path, oldPath]);
        else await Git("rm", "--cached", "-f", "--", path);
    }
    /// <summary>Stages several files in one Git call. Every path is validated before anything runs,
    /// so a batch holding one bad path stages nothing rather than stopping half way.</summary>
    public async Task StageFilesAsync(IReadOnlyList<string> paths) {
        if (paths.Count == 0) throw new InvalidOperationException("Select at least one file to stage.");
        foreach (string path in paths) ValidatePath(path);
        await RunAsync(["add", "--", .. paths]);
    }
    /// <summary>Unstages several files in one Git call, with the same all-or-nothing validation.</summary>
    public async Task UnstageFilesAsync(IReadOnlyList<string> paths) {
        if (paths.Count == 0) throw new InvalidOperationException("Select at least one file to unstage.");
        foreach (string path in paths) ValidatePath(path);
        if (await HasHead()) await RunAsync(["reset", "-q", "HEAD", "--", .. paths]);
        else await RunAsync(["rm", "--cached", "-f", "--", .. paths]);
    }
    public static IReadOnlyList<PatchHunk> ParseHunks(string patch) {
        var matches = Regex.Matches(patch, @"^@@ -\d+(?:,\d+)? \+(\d+)(?:,\d+)? @@[^\n]*", RegexOptions.Multiline);
        if (matches.Count == 0) return [];
        var header = patch[..matches[0].Index];
        if (header.Contains("new file mode") || header.Contains("deleted file mode") || header.Contains("rename from")) return [];
        return matches.Select((m, i) => new PatchHunk(i, m.Value, int.Parse(m.Groups[1].Value), header + patch[m.Index..(i + 1 < matches.Count ? matches[i + 1].Index : patch.Length)])).ToArray();
    }
    public async Task StageHunkAsync(string path, string expectedPatch, int index) {
        ValidatePath(path);
        if (await ReadPatchAsync(path, false) != expectedPatch) throw new InvalidOperationException("The file or index changed. Refresh before staging this hunk.");
        var hunks = ParseHunks(expectedPatch);
        if (index < 0 || index >= hunks.Count) throw new InvalidOperationException("This hunk is no longer available.");
        await RunAsync(["apply", "--cached", "--check", "--whitespace=nowarn", "-"], hunks[index].Patch);
        await RunAsync(["apply", "--cached", "--whitespace=nowarn", "-"], hunks[index].Patch);
    }
    /// <summary>Takes a single hunk back out of the index, leaving the working tree untouched.</summary>
    public async Task UnstageHunkAsync(string path, string expectedPatch, int index) {
        ValidatePath(path);
        // The staged patch is the one being reversed, so it is what must not have moved.
        if (await ReadPatchAsync(path, true) != expectedPatch) throw new InvalidOperationException("The file or index changed. Refresh before unstaging this hunk.");
        var hunks = ParseHunks(expectedPatch);
        if (index < 0 || index >= hunks.Count) throw new InvalidOperationException("This hunk is no longer available.");
        await RunAsync(["apply", "--cached", "--reverse", "--check", "--whitespace=nowarn", "-"], hunks[index].Patch);
        await RunAsync(["apply", "--cached", "--reverse", "--whitespace=nowarn", "-"], hunks[index].Patch);
    }
    public async Task<MergeFile> ReadMergeAsync(string path) {
        var snapshot = await ReadWorkingFile(path);
        if (snapshot.Text.Contains('\0')) throw new InvalidOperationException("Binary conflicts need an external merge tool.");
        var stages = await Git("ls-files", "-u", "--", path);
        if (!Regex.IsMatch(stages, @" 2\t") || !Regex.IsMatch(stages, @" 3\t")) throw new InvalidOperationException("This is a delete/modify or structural conflict. Resolve it with Git, then refresh.");
        string ancestor = Regex.IsMatch(stages, @" 1\t") ? await ReadObject($":1:{path}") : "";
        string ours = await ReadObject($":2:{path}"), theirs = await ReadObject($":3:{path}");
        var document = MergeDocument.Parse(snapshot.Text);
        if (document.Conflicts.Any(c => c.Base == null)) {
            var reconstructed = await ReconstructMergeAsync(ancestor, ours, theirs);
            document.AddBaseHints(MergeDocument.Parse(reconstructed));
        }
        await Task.Run(() => SmartMerge.Analyze(document));
        return new(path, ancestor, ours, theirs, snapshot, document, stages);
    }
    public async Task<string> SaveMergeAsync(MergeFile merge, string result) {
        if (MergeDocument.HasMarkers(result)) throw new InvalidOperationException("Resolve all conflict markers before saving.");
        var full = ValidatePath(merge.Path);
        if ((await ReadWorkingFile(merge.Path)).Hash != merge.Snapshot.Hash) throw new InvalidOperationException("The file changed outside Gitland. Refresh to protect those edits.");
        if (merge.StageSignature.Length > 0 && await Git("ls-files", "-u", "--", merge.Path) != merge.StageSignature) throw new InvalidOperationException("The merge index changed outside Gitland. Refresh before saving.");
        var gitDir = (await Git("rev-parse", "--absolute-git-dir")).Trim();
        var backupDir = System.IO.Path.Combine(gitDir, "gitland-backups"); Directory.CreateDirectory(backupDir);
        if (File.GetAttributes(backupDir).HasFlag(FileAttributes.ReparsePoint)) throw new InvalidOperationException("The recovery directory must not be a symbolic link.");
        string backup = System.IO.Path.Combine(backupDir, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8] + ".bak");
        // Result editors normalize newlines; restore the original file's convention and UTF-8 BOM.
        result = result.Replace("\r\n", "\n").Replace("\n", merge.Snapshot.Newline);
        string temp = full + ".gitland-" + Guid.NewGuid().ToString("N");
        try {
            await File.WriteAllTextAsync(temp, result, new UTF8Encoding(merge.Snapshot.Bom));
            if ((await ReadWorkingFile(merge.Path)).Hash != merge.Snapshot.Hash) throw new InvalidOperationException("The file changed while saving. Refresh and try again.");
            File.Replace(temp, full, backup);
        } finally { if (File.Exists(temp)) File.Delete(temp); }
        return backup;
    }
}
