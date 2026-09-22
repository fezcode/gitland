using System.Text.RegularExpressions;

namespace Gitland.Core;

public sealed record GitSubmodule(string Path, string Url, string Hash, bool Initialized);
public sealed record LfsPattern(string Pattern, string Handler);
public sealed record BisectState(bool Running, string Term, int Remaining, string Commit, string Subject);
public sealed record GitHook(string Name, bool Enabled, string Path);
public sealed record SignatureStatus(string Code, string Signer, string Key) {
    /// <summary>G good, U good with untrusted key, B bad, N none, X/Y/R expired or revoked.</summary>
    public bool Good => Code is "G" or "U";
}

public sealed partial class GitRepository {
    // ---- Submodules ---------------------------------------------------------

    public async Task<IReadOnlyList<GitSubmodule>> ReadSubmodulesAsync() {
        var modules = new List<GitSubmodule>();
        var result = await RunResultAsync(["config", "-f", ".gitmodules", "--get-regexp", @"^submodule\..*\.path$"]);
        if (result.ExitCode != 0) return modules;                    // no .gitmodules at all
        string status = (await RunResultAsync(["submodule", "status"])).Output;
        foreach (string raw in result.Output.Split('\n')) {
            string line = raw.TrimEnd('\r');
            int space = line.IndexOf(' ');
            if (space <= 0) continue;
            string name = line[..space].Replace("submodule.", "").Replace(".path", "");
            string path = line[(space + 1)..];
            string url = (await RunResultAsync(["config", "-f", ".gitmodules", "--get", $"submodule.{name}.url"])).Output.Trim();
            // `submodule status` prefixes '-' when a module has never been initialized.
            var entry = status.Split('\n').FirstOrDefault(s => s.TrimEnd('\r').EndsWith(" " + path) || s.Contains(" " + path + " "));
            modules.Add(new(path, url, entry?.TrimStart('-', '+', 'U', ' ').Split(' ').FirstOrDefault() ?? "", entry?.StartsWith('-') != true));
        }
        return modules;
    }

    public async Task AddSubmoduleAsync(string url, string path) {
        ValidateRemoteUrl(url); ValidatePath(path);
        await RunAsync(["submodule", "add", "--", url, path], timeout: 300);
    }

    public async Task UpdateSubmodulesAsync(bool initialize = true) =>
        await RunAsync(initialize ? ["submodule", "update", "--init", "--recursive"] : ["submodule", "update", "--recursive"], timeout: 600);

    public async Task SyncSubmodulesAsync() => await RunAsync(["submodule", "sync", "--recursive"], timeout: 120);

    // ---- Git LFS ------------------------------------------------------------

    /// <summary>True when git-lfs is installed and this repository has it configured.</summary>
    public async Task<bool> HasLfsAsync() {
        var version = await _runner.RunAsync(new("git", Root, ["lfs", "version"]));
        if (version.ExitCode != 0) return false;
        return File.Exists(Path.Combine(Root, ".gitattributes")) &&
               (await File.ReadAllTextAsync(Path.Combine(Root, ".gitattributes"))).Contains("filter=lfs");
    }

    public async Task<IReadOnlyList<LfsPattern>> ReadLfsPatternsAsync() {
        var patterns = new List<LfsPattern>();
        string attributes = Path.Combine(Root, ".gitattributes");
        if (!File.Exists(attributes)) return patterns;
        foreach (string raw in await File.ReadAllLinesAsync(attributes)) {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || !line.Contains("filter=lfs")) continue;
            patterns.Add(new(line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0], "lfs"));
        }
        return patterns;
    }

    public async Task TrackLfsAsync(string pattern) {
        pattern = pattern.Trim();
        if (pattern.Length == 0 || pattern.Contains('\n')) throw new InvalidOperationException("Write a single file pattern, such as *.psd.");
        await RunAsync(["lfs", "track", "--", pattern], timeout: 60);
    }

    public async Task UntrackLfsAsync(string pattern) {
        pattern = pattern.Trim();
        if (pattern.Length == 0 || pattern.Contains('\n')) throw new InvalidOperationException("Write a single file pattern.");
        await RunAsync(["lfs", "untrack", "--", pattern], timeout: 60);
    }

    // ---- Bisect -------------------------------------------------------------

    public async Task<BisectState> ReadBisectAsync() {
        string gitDir = (await Git("rev-parse", "--absolute-git-dir")).Trim();
        if (!File.Exists(Path.Combine(gitDir, "BISECT_LOG"))) return new(false, "", 0, "", "");
        var view = await RunResultAsync(["bisect", "visualize", "--format=%H%x00%s%x00", "-n", "1"]);
        string head = await ResolveRef("HEAD");
        string subject = (await RunResultAsync(["log", "-1", "--format=%s", head])).Output.Trim();
        // "Bisecting: N revisions left to test" is the only place Git reports the remaining span.
        var remaining = Regex.Match(view.Output + view.Error, @"Bisecting:\s+(\d+)\s+revision");
        return new(true, "", remaining.Success ? int.Parse(remaining.Groups[1].Value) : 0, head, subject);
    }

    public async Task<string> StartBisectAsync(string bad, string good) {
        string badHash = await ResolveRef(bad), goodHash = await ResolveRef(good);
        await RequireClean();
        await RunAsync(["bisect", "start", badHash, goodHash], timeout: 120);
        return (await ReadBisectAsync()).Commit;
    }

    /// <summary>Marks the current commit and moves to the next one. Kind is good, bad or skip.</summary>
    public async Task<string> MarkBisectAsync(string kind) {
        if (kind is not ("good" or "bad" or "skip")) throw new ArgumentException("Mark a bisect step good, bad or skip.");
        var result = await RunResultAsync(["bisect", kind], timeout: 120);
        if (result.ExitCode != 0) throw new CommandFailedException(result.Error.Trim(), result.ExitCode);
        // Git announces the answer on stdout once the range collapses to one commit. Git 2.55
        // quotes the term ("is the first 'bad' commit"); older releases do not.
        var found = Regex.Match(result.Output, @"([0-9a-f]{40}) is the first '?bad'? commit");
        return found.Success ? found.Groups[1].Value : "";
    }

    public async Task ResetBisectAsync() => await RunAsync(["bisect", "reset"], timeout: 120);

    // ---- Signing ------------------------------------------------------------

    /// <summary>Commits with a signature. Git picks GPG or SSH from gpg.format in the user's config.</summary>
    public async Task<string> CommitSignedAsync(string message, string expectedTree, string? expectedHead) {
        if (string.IsNullOrWhiteSpace(message)) throw new InvalidOperationException("Write a commit message.");
        var state = await ReadManagementAsync();
        if (state.IndexTree != expectedTree || state.Head != expectedHead) throw new InvalidOperationException("The repository changed. Refresh before committing.");
        await RunAsync(["commit", "--gpg-sign", "--file=-"], message, timeout: 120);
        return await ResolveRef("HEAD");
    }

    public async Task<SignatureStatus> VerifySignatureAsync(string revision) {
        string target = await ResolveRef(revision);
        var result = await RunResultAsync(["log", "-1", "--format=%G?%x00%GS%x00%GK%x00", target]);
        var fields = result.Output.Split('\0');
        return fields.Length < 3 ? new("N", "", "") : new(fields[0].Trim(), fields[1], fields[2]);
    }

    // ---- Archive and hooks --------------------------------------------------

    /// <summary>Writes a revision out as a zip, without the repository metadata.</summary>
    public async Task<string> ArchiveAsync(string revision, string destination) {
        string full = Path.GetFullPath(destination);
        string target = await ResolveRef(revision);
        await RunAsync(["archive", "--format=zip", "--output=" + full, target], timeout: 300);
        return full;
    }

    /// <summary>Lists this repository's hooks. Git ignores any hook whose name ends in .sample.</summary>
    public async Task<IReadOnlyList<GitHook>> ReadHooksAsync() {
        string gitDir = (await Git("rev-parse", "--absolute-git-dir")).Trim();
        string configured = (await RunResultAsync(["config", "--get", "core.hooksPath"])).Output.Trim();
        string folder = configured.Length > 0 ? Path.GetFullPath(Path.Combine(Root, configured)) : Path.Combine(gitDir, "hooks");
        if (!Directory.Exists(folder)) return [];
        return Directory.EnumerateFiles(folder)
            .Select(file => new GitHook(Path.GetFileName(file).Replace(".sample", ""), !file.EndsWith(".sample", StringComparison.OrdinalIgnoreCase), file))
            .OrderBy(h => h.Name, StringComparer.Ordinal).ToArray();
    }
}
