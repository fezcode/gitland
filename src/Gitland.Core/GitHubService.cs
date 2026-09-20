using System.Text.Json;
using System.Text.RegularExpressions;

namespace Gitland.Core;

public sealed record PublishRepositoryPlan(string Owner, string Name, string Description, bool Private, bool PushBranch, string Branch, string? Head);
public sealed record GitHubReleasePlan(string Repository, string Tag, string Commit, string Title, string Notes, bool Draft = true, bool Prerelease = false, bool GenerateNotes = false);
public sealed record GitHubRelease(string Tag, string Name, bool Draft, bool Prerelease, string? PublishedAt);
public sealed record PublishedRepository(string Url, string RemoteUrl, bool Pushed);

public sealed class GitHubService(ICommandRunner? runner = null) {
    readonly ICommandRunner _runner = runner ?? new CommandRunner();
    async Task<string> Gh(string directory, string[] args, string? input = null) {
        var result = await _runner.RunAsync(new("gh", directory, args, input, 120));
        if (result.ExitCode != 0) throw new CommandFailedException(string.IsNullOrWhiteSpace(result.Error) ? result.Output.Trim() : result.Error.Trim(), result.ExitCode);
        return result.Output.Trim();
    }
    public async Task<string> ConnectedUserAsync(string directory) {
        string user = await Gh(directory, ["api", "--hostname", "github.com", "user", "--jq", ".login"]);
        ValidateOwner(user); return user;
    }
    static void ValidateOwner(string owner) {
        if (!Regex.IsMatch(owner, @"\A[A-Za-z0-9][A-Za-z0-9-]{0,38}\z")) throw new InvalidOperationException("Enter a valid GitHub owner or organization.");
    }
    public static string FullName(string owner, string name) {
        ValidateOwner(owner);
        if (!Regex.IsMatch(name, @"\A[A-Za-z0-9_.-]{1,100}\z") || name is "." or "..") throw new InvalidOperationException("Enter a GitHub repository name using letters, digits, dots, hyphens, or underscores.");
        return owner + "/" + name;
    }
    public static string? RepositoryFromRemote(string url) {
        if (url.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase)) url = "https://github.com/" + url[15..];
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) || uri.Query.Length != 0 || uri.Fragment.Length != 0 || !uri.IsDefaultPort || uri.Scheme is not ("https" or "ssh")) return null;
        if (uri.Scheme == "https" && uri.UserInfo.Length > 0) return null;
        var path = uri.AbsolutePath.Trim('/'); if (path.EndsWith(".git")) path = path[..^4];
        var parts = path.Split('/'); if (parts.Length != 2) return null;
        try { return FullName(parts[0], parts[1]); } catch (InvalidOperationException) { return null; }
    }
    public async Task<PublishedRepository> PublishAsync(GitRepository repo, PublishRepositoryPlan plan) {
        var fullName = FullName(plan.Owner, plan.Name);
        if ((await repo.ReadRemotesAsync()).Any(r => r.Name == "origin")) throw new InvalidOperationException("This repository already has origin. Use Push current branch to publish new commits.");
        var current = await repo.ReadManagementAsync(); var state = await repo.ReadStateAsync();
        if (current.Head != plan.Head || state.Branch != plan.Branch) throw new InvalidOperationException("HEAD changed. Refresh and review the repository before publishing.");
        if (plan.PushBranch && plan.Head == null) throw new InvalidOperationException("Create an initial commit before publishing a branch, or turn off Push current branch.");
        if (plan.PushBranch) GitRepository.ValidateRefName(plan.Branch);
        await Gh(repo.Root, ["repo", "create", fullName, plan.Private ? "--private" : "--public", "--description", plan.Description]);
        string url = "https://github.com/" + fullName, remote = url + ".git";
        try { await repo.AddRemoteAsync("origin", remote); }
        catch (Exception e) { throw new InvalidOperationException($"Created {url}, but origin could not be added: {e.Message} Add this remote to continue; the GitHub repository has been kept.", e); }
        if (plan.PushBranch) {
            try { await repo.PushBranchAsync("origin", plan.Branch, plan.Head!); }
            catch (Exception e) { throw new InvalidOperationException($"Created {url} and configured origin, but the first push failed: {e.Message} Use Push current branch to retry.", e); }
        }
        return new(url, remote, plan.PushBranch);
    }
    public async Task<IReadOnlyList<GitHubRelease>> ListReleasesAsync(string directory, string repository) {
        ValidateRepository(repository);
        var json = await Gh(directory, ["release", "list", "--repo", "github.com/" + repository, "--limit", "20", "--json", "tagName,name,isDraft,isPrerelease,publishedAt"]);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateArray().Select(r => new GitHubRelease(r.GetProperty("tagName").GetString()!, r.GetProperty("name").GetString() ?? "", r.GetProperty("isDraft").GetBoolean(), r.GetProperty("isPrerelease").GetBoolean(), r.GetProperty("publishedAt").GetString())).ToArray();
    }
    static void ValidateRepository(string repository) { var parts = repository.Split('/'); if (parts.Length != 2 || FullName(parts[0], parts[1]) != repository) throw new InvalidOperationException("Choose a repository in owner/name format."); }
    public async Task<string> CreateReleaseAsync(GitRepository repo, GitHubReleasePlan plan) {
        ValidateRepository(plan.Repository); GitRepository.ValidateRefName(plan.Tag);
        if (string.IsNullOrWhiteSpace(plan.Title)) throw new InvalidOperationException("Give the release a title.");
        var origin = (await repo.ReadRemotesAsync()).SingleOrDefault(r => r.Name == "origin");
        if (origin == null || RepositoryFromRemote(origin.Url) != plan.Repository) throw new InvalidOperationException("Origin changed or does not match this GitHub repository. Refresh before creating the release.");
        if (await repo.ResolveRef("refs/tags/" + plan.Tag) != plan.Commit) throw new InvalidOperationException("The local tag changed. Refresh before creating this release.");
        string? remoteCommit = await repo.RemoteTagCommitAsync("origin", plan.Tag);
        if (remoteCommit == null) throw new InvalidOperationException("Push this tag first, then create the release. No release was created.");
        if (remoteCommit != plan.Commit) throw new InvalidOperationException("The GitHub tag points to a different commit. Resolve that mismatch before releasing.");
        var args = new List<string> { "release", "create", plan.Tag, "--repo", "github.com/" + plan.Repository, "--verify-tag", "--title", plan.Title, "--notes-file", "-" };
        if (plan.Draft) args.Add("--draft"); if (plan.Prerelease) args.Add("--prerelease"); if (plan.GenerateNotes) args.Add("--generate-notes");
        string output = await Gh(repo.Root, args.ToArray(), plan.Notes);
        return Uri.TryCreate(output, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == "github.com" ? output : "https://github.com/" + plan.Repository + "/releases";
    }
}
