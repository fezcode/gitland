using Gitland.Core;
using Xunit;

namespace Gitland.Tests;

public sealed class GitHubTests {
    const string Sha = "1234567890123456789012345678901234567890";
    sealed class FakeCommands : ICommandRunner {
        public List<CommandRequest> Requests { get; } = [];
        public bool Origin { get; set; } = true;
        public string? RemoteTag { get; set; } = Sha;
        public bool PushFails { get; set; }
        public bool CreateFails { get; set; }
        public Task<CommandResult> RunAsync(CommandRequest command) {
            Requests.Add(command); var a = command.Arguments.ToArray();
            CommandResult result;
            if (command.Executable == "gh") {
                if (a[0] == "repo" && CreateFails) result = new(1, "", "name already exists");
                else if (a[0] == "release" && a[1] == "create") result = new(0, "https://github.com/octocat/project/releases/tag/v1\n", "");
                else if (a[0] == "release" && a[1] == "list") result = new(0, "[{\"tagName\":\"v1\",\"name\":\"Release one\",\"isDraft\":true,\"isPrerelease\":false,\"publishedAt\":null}]", "");
                else result = new(0, "octocat\n", "");
            } else {
                a = a.Skip(3).ToArray();
                result = a[0] switch {
                    "remote" when a.Length == 1 => new(0, Origin ? "origin\n" : "", ""),
                    "remote" when a[1] == "get-url" => new(0, "https://github.com/octocat/project.git\n", ""),
                    "remote" when a[1] == "add" => AddOrigin(),
                    "symbolic-ref" => new(0, "main\n", ""),
                    "rev-parse" when a.Contains("MERGE_HEAD") => new(1, "", ""),
                    "rev-parse" => new(0, Sha + "\n", ""),
                    "write-tree" => new(0, "tree\n", ""),
                    "ls-remote" => new(0, RemoteTag == null ? "" : RemoteTag + "\trefs/tags/v1\n", ""),
                    _ when a.Contains("push") && PushFails => new(1, "", "network unavailable"),
                    _ => new(0, "", "")
                };
            }
            return Task.FromResult(result);
        }
        CommandResult AddOrigin() { Origin = true; return new(0, "", ""); }
    }
    [Theory]
    [InlineData("https://github.com/octocat/project.git", "octocat/project")]
    [InlineData("git@github.com:octocat/project.git", "octocat/project")]
    [InlineData("ssh://git@github.com/octocat/project.git", "octocat/project")]
    [InlineData("https://github.com.evil.invalid/octocat/project.git", null)]
    [InlineData("https://user:secret@github.com/octocat/project.git", null)]
    [InlineData("https://github.com/octocat/project?x=y", null)]
    public void AcceptsOnlyUnambiguousGitHubRemotes(string remote, string? expected) => Assert.Equal(expected, GitHubService.RepositoryFromRemote(remote));
    [Fact] public async Task PublishUsesExplicitPrivacyAndOnlyPushesWhenRequested() {
        var commands = new FakeCommands { Origin = false }; var repo = new GitRepository(Path.GetTempPath(), commands); var service = new GitHubService(commands);
        var result = await service.PublishAsync(repo, new("octocat", "project", "A literal $(command) and `tick`", true, false, "main", Sha));
        Assert.Equal("https://github.com/octocat/project", result.Url); Assert.False(result.Pushed);
        var request = Assert.Single(commands.Requests.Where(r => r.Executable == "gh")); Assert.Contains("--private", request.Arguments); Assert.DoesNotContain("--public", request.Arguments);
        Assert.Contains("A literal $(command) and `tick`", request.Arguments); Assert.DoesNotContain(commands.Requests, r => r.Arguments.Contains("push"));
        Assert.True(commands.Origin);
    }
    [Fact] public async Task CreationFailureDoesNotConfigureOrPushRemote() {
        var commands = new FakeCommands { Origin = false, CreateFails = true }; var repo = new GitRepository(Path.GetTempPath(), commands);
        await Assert.ThrowsAsync<CommandFailedException>(() => new GitHubService(commands).PublishAsync(repo, new("octocat", "project", "", true, true, "main", Sha)));
        Assert.False(commands.Origin); Assert.DoesNotContain(commands.Requests, r => r.Arguments.Contains("push"));
    }
    [Fact] public async Task FailedInitialPushReportsRecoverablePartialSuccess() {
        var commands = new FakeCommands { Origin = false, PushFails = true }; var repo = new GitRepository(Path.GetTempPath(), commands);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new GitHubService(commands).PublishAsync(repo, new("octocat", "project", "", false, true, "main", Sha)));
        Assert.Contains("created", error.Message, StringComparison.OrdinalIgnoreCase); Assert.Contains("retry", error.Message); Assert.True(commands.Origin);
        Assert.DoesNotContain(commands.Requests, r => r.Arguments.Contains("delete"));
    }
    [Fact] public async Task ReleaseVerifiesTagAndSendsExactNotesThroughStandardInput() {
        var commands = new FakeCommands(); var repo = new GitRepository(Path.GetTempPath(), commands);
        string notes = "# Changes\n\n* Preserve `code` and $(literal text).\n";
        var url = await new GitHubService(commands).CreateReleaseAsync(repo, new("octocat/project", "v1", Sha, "Version 1", notes, true, true, true));
        var gh = Assert.Single(commands.Requests.Where(r => r.Executable == "gh"));
        Assert.Contains("--verify-tag", gh.Arguments); Assert.Contains("--draft", gh.Arguments); Assert.Contains("--prerelease", gh.Arguments); Assert.Contains("--generate-notes", gh.Arguments); Assert.Equal(notes, gh.Input);
        Assert.Equal("https://github.com/octocat/project/releases/tag/v1", url);
    }
    [Theory]
    [InlineData(null)]
    [InlineData("9999999999999999999999999999999999999999")]
    public async Task ReleaseNeverCallsGitHubWhenTagIsMissingOrMismatched(string? remoteCommit) {
        var commands = new FakeCommands { RemoteTag = remoteCommit }; var repo = new GitRepository(Path.GetTempPath(), commands);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new GitHubService(commands).CreateReleaseAsync(repo, new("octocat/project", "v1", Sha, "Release", "notes")));
        Assert.DoesNotContain(commands.Requests, r => r.Executable == "gh");
    }
    [Fact] public async Task ParsesDraftReleasesWithoutPublishedDate() {
        var service = new GitHubService(new FakeCommands()); var release = Assert.Single(await service.ListReleasesAsync(Path.GetTempPath(), "octocat/project")); Assert.True(release.Draft); Assert.Null(release.PublishedAt);
    }
}
