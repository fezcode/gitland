using System.Text;
using Gitland.Core;
using Xunit;

namespace Gitland.Tests;

public sealed class ManagementTests : IDisposable {
    readonly string _directory = Path.Combine(Path.GetTempPath(), "gitland-management-" + Guid.NewGuid().ToString("N"));
    public ManagementTests() => Directory.CreateDirectory(_directory);
    async Task<GitRepository> Create(string name = "repo") {
        var repo = await GitRepository.InitializeAsync(new(Path.Combine(_directory, name), "main", true, "dotnet"));
        await repo.Git("config", "user.name", "Gitland Test"); await repo.Git("config", "user.email", "gitland@example.invalid"); await repo.Git("config", "core.autocrlf", "false"); return repo;
    }
    async Task<string> Commit(GitRepository repo, string message = "Initial commit") {
        await repo.StageAllAsync(); var state = await repo.ReadManagementAsync(); return await repo.CommitAsync(message, state.IndexTree, state.Head);
    }
    [Fact] public async Task FileInventoryIncludesCleanChangedDeletedAndUntrackedButExcludesIgnored() {
        var repo = await Create();
        await File.WriteAllTextAsync(Path.Combine(repo.Root, "clean file.txt"), "unchanged\n");
        await File.WriteAllTextAsync(Path.Combine(repo.Root, "deleted.txt"), "remove\n");
        await Commit(repo);
        await File.WriteAllTextAsync(Path.Combine(repo.Root, "README.md"), "changed\n");
        await File.WriteAllTextAsync(Path.Combine(repo.Root, "new file.txt"), "new\n");
        Directory.CreateDirectory(Path.Combine(repo.Root, "bin")); await File.WriteAllTextAsync(Path.Combine(repo.Root, "bin", "ignored.txt"), "ignored\n");
        File.Delete(Path.Combine(repo.Root, "deleted.txt"));
        var state = await repo.ReadStateAsync();
        var clean = Assert.Single(state.AllFiles.Where(f => f.Path == "clean file.txt")); Assert.False(clean.IsChanged);
        Assert.DoesNotContain(state.Changes, f => f.Path == clean.Path);
        Assert.Contains(state.AllFiles, f => f.Path == "README.md" && f.IsChanged);
        Assert.Contains(state.AllFiles, f => f.Path == "new file.txt" && f.Index == '?');
        Assert.Contains(state.AllFiles, f => f.Path == "deleted.txt" && f.Label == "Deleted");
        Assert.DoesNotContain(state.AllFiles, f => f.Path.StartsWith("bin/"));
        var diff = await repo.WorkingDiffAsync(clean, false); Assert.Equal(diff.Left, diff.Right);
        Assert.Equal(state.AllFiles.Count, state.AllFiles.Select(f => f.Path).Distinct().Count());
    }
    [Fact] public async Task CreatesRepositoryWithoutOverwritingOrAutomaticallyStaging() {
        var path = Path.Combine(_directory, "existing"); Directory.CreateDirectory(path); await File.WriteAllTextAsync(Path.Combine(path, "README.md"), "keep me");
        var repo = await GitRepository.InitializeAsync(new(path, "develop", true, "node"));
        Assert.Equal("keep me", await File.ReadAllTextAsync(Path.Combine(path, "README.md"))); Assert.Contains("node_modules/", await File.ReadAllTextAsync(Path.Combine(path, ".gitignore")));
        var state = await repo.ReadStateAsync(); Assert.Equal("develop", state.Branch); Assert.All(state.Changes, c => Assert.Equal('?', c.Index));
        await Assert.ThrowsAsync<InvalidOperationException>(() => GitRepository.InitializeAsync(new(Path.Combine(path, "nested"))));
    }
    [Fact] public async Task CommitUsesOnlyReviewedIndexAndRejectsStaleHeadAndIndex() {
        var repo = await Create(); await repo.StageAllAsync(); var review = await repo.ReadManagementAsync();
        await File.WriteAllTextAsync(Path.Combine(repo.Root, "untracked.txt"), "not staged");
        string commit = await repo.CommitAsync("Initial\n\nA literal `backtick` and $(not a command).", review.IndexTree, review.Head);
        Assert.Equal(commit, (await repo.ReadManagementAsync()).Head); Assert.DoesNotContain("untracked.txt", await repo.Git("ls-tree", "--name-only", "HEAD"));
        await repo.StageAllAsync(); var state = await repo.ReadManagementAsync(); await File.WriteAllTextAsync(Path.Combine(repo.Root, "second.txt"), "new"); await repo.StageAllAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.CommitAsync("stale", state.IndexTree, state.Head));
        Assert.Contains("Initial", (await repo.ReadManagementAsync()).Commits[0].Subject);
    }
    [Fact] public async Task InspectingStatusDoesNotRewriteTheIndex() {
        var repo = await Create(); await Commit(repo);
        string path = Path.Combine(repo.Root, "README.md");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-2));
        var before = await File.ReadAllBytesAsync(Path.Combine(repo.Root, ".git", "index"));
        var state = await repo.ReadStateAsync();
        Assert.Empty(state.Changes);
        Assert.Equal(before, await File.ReadAllBytesAsync(Path.Combine(repo.Root, ".git", "index")));
    }
    [Fact] public async Task CreatesSwitchesBranchesAndTagsSpecificCommits() {
        var repo = await Create(); string first = await Commit(repo); await repo.CreateBranchAsync("feature/review", "HEAD", true);
        Assert.Equal("feature/review", (await repo.ReadStateAsync()).Branch);
        await File.WriteAllTextAsync(Path.Combine(repo.Root, "change.txt"), "new"); await Commit(repo, "Feature");
        await repo.CreateTagAsync("v1.0.0", first, "Release one\n\nNotes with quotes: \"hello\".");
        var state = await repo.ReadManagementAsync(); var tag = Assert.Single(state.Tags); Assert.Equal(first, tag.Commit); Assert.Equal("tag", (await repo.Git("cat-file", "-t", "refs/tags/v1.0.0")).Trim());
        await Assert.ThrowsAsync<CommandFailedException>(() => repo.CreateTagAsync("v1.0.0", "HEAD", "duplicate"));
        await repo.SwitchBranchAsync("main"); Assert.Equal(first, await repo.ResolveRef("HEAD"));
        await File.WriteAllTextAsync(Path.Combine(repo.Root, "dirty.txt"), "local"); await Assert.ThrowsAsync<InvalidOperationException>(() => repo.SwitchBranchAsync("feature/review"));
    }
    [Theory]
    [InlineData("--force")][InlineData("bad name")][InlineData("a..b")][InlineData("x.lock")][InlineData("@{1}")][InlineData("feature//x")]
    public void RejectsUnsafeRefNames(string name) => Assert.Throws<InvalidOperationException>(() => GitRepository.ValidateRefName(name));
    [Fact] public async Task PushesOnlySelectedBranchAndTagToLocalRemote() {
        var repo = await Create(); string head = await Commit(repo); string remote = Path.Combine(_directory, "remote.git"); Directory.CreateDirectory(remote);
        await new GitRepository(remote).Git("init", "--bare"); await repo.AddRemoteAsync("origin", remote);
        Assert.Equal(remote, Assert.Single(await repo.ReadRemotesAsync()).Url);
        await repo.CreateTagAsync("v1", head, "One"); await repo.Git("config", "push.followTags", "true");
        await repo.PushBranchAsync("origin", "main", head); Assert.Null(await repo.RemoteTagCommitAsync("origin", "v1"));
        await repo.PushTagAsync("origin", "v1", head); Assert.Equal(head, await repo.RemoteTagCommitAsync("origin", "v1"));
        await repo.FetchAsync("origin"); await repo.PullAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.PushBranchAsync("origin", "main", new string('0', 40)));
    }
    [Fact] public async Task AddsBaseToTwoWayConflictWithoutDiscardingWorkingEdits() {
        var repo = await Create(); string path = Path.Combine(repo.Root, "config.txt");
        await File.WriteAllTextAsync(path, "header\nconst config = { mode: 'manual', backup: false };\nfooter\n"); await Commit(repo);
        await repo.CreateBranchAsync("incoming", "HEAD", true); await File.WriteAllTextAsync(path, "header\nconst config = { mode: 'manual', backup: true };\nfooter\n"); await Commit(repo, "Incoming");
        await repo.SwitchBranchAsync("main"); await File.WriteAllTextAsync(path, "header\nconst config = { mode: 'interactive', backup: false };\nfooter\n"); await Commit(repo, "Ours");
        await Assert.ThrowsAsync<CommandFailedException>(() => repo.Git("merge", "incoming"));
        string working = (await File.ReadAllTextAsync(path)).Replace("header", "user-edited header"); await File.WriteAllTextAsync(path, working, new UTF8Encoding(false));
        string indexBeforeReview = await repo.Git("ls-files", "-u", "--", "config.txt");
        var review = await repo.ConflictDiffAsync("config.txt");
        Assert.Equal(await repo.Git("show", ":2:config.txt"), review.Left); Assert.Equal(working, review.Right);
        Assert.Contains("unresolved", review.RightLabel); Assert.Equal(indexBeforeReview, await repo.Git("ls-files", "-u", "--", "config.txt"));
        var merge = await repo.ReadMergeAsync("config.txt"); var conflict = Assert.Single(merge.Document.Conflicts); Assert.NotNull(conflict.Base); Assert.NotNull(conflict.Suggestion);
        Assert.Equal(working, merge.Document.Render()); conflict.Choice = Resolution.Smart;
        Assert.Contains("user-edited header", merge.Document.Render()); Assert.Contains("mode: 'interactive', backup: true", merge.Document.Render());
        Assert.Equal(working, await File.ReadAllTextAsync(path));
    }
    public void Dispose() {
        var full = Path.GetFullPath(_directory);
        if (full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) && Path.GetFileName(full).StartsWith("gitland-management-")) {
            foreach (var file in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal); Directory.Delete(full, true);
        }
    }
}

public class SmartMergeTests {
    [Fact] public void CombinesIndependentEditsOnTheSameLine() {
        var result = SmartMerge.Suggest("const x = { mode: 'manual', backup: false };\n", "const x = { mode: 'interactive', backup: false };\n", "const x = { mode: 'manual', backup: true };\n");
        Assert.Equal("const x = { mode: 'interactive', backup: true };\n", result?.Text);
    }
    [Theory]
    [InlineData("let x = 1;", "let x = 2;", "let x = 3;")]
    [InlineData("return 'hello';", "return 'Hello';", "return 'hello!';")]
    [InlineData("a b", "a x b", "a y b")]
    public void LeavesOverlappingEditsUnresolved(string basis, string left, string right) => Assert.Null(SmartMerge.Suggest(basis, left, right));
    [Fact] public void PreservesLineEndingsAndSharedChangesWithoutDuplicates() {
        var result = SmartMerge.Suggest("a = 1;\r\nb = 2;\r\nc = 3;\r\n", "a = 4;\r\nb = 5;\r\nc = 3;\r\n", "a = 4;\r\nb = 2;\r\nc = 6;\r\n");
        Assert.Equal("a = 4;\r\nb = 5;\r\nc = 6;\r\n", result?.Text);
    }
    [Fact] public void DoesNotGuessWithoutBase() { Assert.Null(SmartMerge.Suggest(null, "a", "b")); Assert.Equal("same", SmartMerge.Suggest(null, "same", "same")?.Text); }
    [Fact] public void RepeatedConflictsDoNotReceiveAnAmbiguousBase() {
        const string two = "<<<<<<< ours\na\n=======\nb\n>>>>>>> theirs\n";
        const string three = "<<<<<<< ours\na\n||||||| base\nc\n=======\nb\n>>>>>>> theirs\n";
        var original = MergeDocument.Parse(two); original.AddBaseHints(MergeDocument.Parse(three + three)); Assert.Null(original.Conflicts[0].Base);
    }
}
