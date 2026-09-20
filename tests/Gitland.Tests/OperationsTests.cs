using Gitland.Core;
using Xunit;

namespace Gitland.Tests;

public sealed class OperationsTests : IDisposable {
    readonly string _root = Path.Combine(Path.GetTempPath(), "gitland-operations-" + Guid.NewGuid().ToString("N"));
    async Task<GitRepository> Create() {
        var repo = await GitRepository.InitializeAsync(new(Path.Combine(_root, "repo"), "main", false));
        await repo.Git("config", "user.name", "Test"); await repo.Git("config", "user.email", "test@example.invalid"); await repo.Git("config", "core.autocrlf", "false");
        await Write(repo, "file.txt", "base\n"); await Commit(repo, "Base"); return repo;
    }
    static Task Write(GitRepository repo, string path, string text) => File.WriteAllTextAsync(Path.Combine(repo.Root, path), text);
    static async Task<string> Commit(GitRepository repo, string message) { await repo.StageAllAsync(); var state = await repo.ReadManagementAsync(); return await repo.CommitAsync(message, state.IndexTree, state.Head); }
    [Fact] public async Task BranchAndTagCrudRetainsRecoveryAndRejectsStaleObjects() {
        var repo = await Create(); string head = await repo.ResolveRef("HEAD");
        await repo.CreateBranchAsync("topic", head, false); await repo.RenameBranchAsync("topic", "renamed", head);
        Assert.Contains((await repo.ReadManagementAsync()).Branches, b => b.Name == "renamed");
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.DeleteBranchAsync("renamed", new string('0', 40)));
        await repo.DeleteBranchAsync("renamed", head); Assert.DoesNotContain((await repo.ReadManagementAsync()).Branches, b => b.Name == "renamed");
        await repo.CreateTagAsync("v1", head, "Original"); string old = await repo.TagObjectAsync("v1");
        await repo.UpdateTagAsync("v1", head, "Updated\n\nMore notes", old); string updated = await repo.TagObjectAsync("v1"); Assert.NotEqual(old, updated);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.DeleteTagAsync("v1", old));
        await repo.DeleteTagAsync("v1", updated); Assert.Empty((await repo.ReadManagementAsync()).Tags);
        var tools = await repo.ReadToolsAsync(); Assert.Contains(tools.Recovery, r => r.Hash == old); Assert.Contains(tools.Recovery, r => r.Hash == updated); Assert.Contains(tools.Recovery, r => r.Hash == head);
    }
    [Fact] public async Task RemotesCanBeRenamedUpdatedAndRemovedWithoutTouchingDestination() {
        var repo = await Create(); await repo.AddRemoteAsync("origin", "https://example.invalid/old.git");
        await repo.UpdateRemoteAsync("origin", "upstream", "https://example.invalid/new.git", "https://example.invalid/old.git");
        var remote = Assert.Single(await repo.ReadRemotesAsync()); Assert.Equal("upstream", remote.Name);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.DeleteRemoteAsync("upstream", "wrong"));
        await repo.DeleteRemoteAsync(remote.Name, remote.Url); Assert.Empty(await repo.ReadRemotesAsync());
    }
    [Fact] public async Task StashRestoresIndexUnstagedChangesAndUntrackedFiles() {
        var repo = await Create(); await Write(repo, "file.txt", "staged\n"); await repo.StageFileAsync("file.txt"); await Write(repo, "file.txt", "unstaged\n"); await Write(repo, "new.txt", "new\n");
        await repo.SaveStashAsync("Test work", true); Assert.Empty((await repo.ReadStateAsync()).Changes);
        var stash = Assert.Single((await repo.ReadToolsAsync()).Stashes); Assert.Contains("new.txt", await repo.StashPatchAsync(stash.Hash));
        Assert.True((await repo.ApplyStashAsync(stash.Hash, true)).Completed);
        Assert.Equal("staged\n", await repo.Git("show", ":file.txt")); Assert.Equal("unstaged\n", await File.ReadAllTextAsync(Path.Combine(repo.Root, "file.txt"))); Assert.True(File.Exists(Path.Combine(repo.Root, "new.txt")));
        Assert.Empty((await repo.ReadToolsAsync()).Stashes); Assert.Contains((await repo.ReadToolsAsync()).Recovery, r => r.Hash == stash.Hash);
    }
    [Fact] public async Task StashCanExcludeUntrackedAndDirtyApplyIsRejected() {
        var repo = await Create(); await Write(repo, "file.txt", "changed\n"); await Write(repo, "new.txt", "new\n");
        await repo.SaveStashAsync("Tracked only", false); Assert.True(File.Exists(Path.Combine(repo.Root, "new.txt")));
        var stash = Assert.Single((await repo.ReadToolsAsync()).Stashes);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.ApplyStashAsync(stash.Hash, false));
    }
    [Fact] public async Task WorktreesProtectDirtyFoldersAndCanBeRemovedWhenClean() {
        var repo = await Create(); string path = Path.Combine(_root, "worktree"); await repo.AddWorktreeAsync(path, "HEAD", "parallel");
        Assert.Equal(2, (await repo.ReadToolsAsync()).Worktrees.Count); await File.WriteAllTextAsync(Path.Combine(path, "untracked.txt"), "keep");
        await Assert.ThrowsAsync<CommandFailedException>(() => repo.RemoveWorktreeAsync(path)); Assert.True(Directory.Exists(path));
        File.Delete(Path.Combine(path, "untracked.txt")); await repo.RemoveWorktreeAsync(path); Assert.False(Directory.Exists(path));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.RemoveWorktreeAsync(repo.Root));
    }
    [Fact] public async Task MergeConflictCanBeAbortedAndContinued() {
        var repo = await Create(); await repo.CreateBranchAsync("topic", "HEAD", true); await Write(repo, "file.txt", "theirs\n"); await Commit(repo, "Theirs");
        await repo.SwitchBranchAsync("main"); await Write(repo, "file.txt", "ours\n"); string head = await Commit(repo, "Ours");
        Assert.False((await repo.HistoryOperationAsync("merge", "topic", head)).Completed); Assert.Equal("merge", await repo.OperationAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.FinishOperationAsync("merge", false));
        Assert.True((await repo.FinishOperationAsync("merge", true)).Completed); Assert.Equal(head, await repo.ResolveRef("HEAD")); Assert.Empty((await repo.ReadStateAsync()).Changes);
        Assert.False((await repo.HistoryOperationAsync("merge", "topic", head)).Completed);
        await Write(repo, "file.txt", "combined\n"); await repo.StageFileAsync("file.txt"); Assert.True((await repo.FinishOperationAsync("merge", false)).Completed);
        Assert.Equal("", await repo.OperationAsync()); Assert.NotEqual(head, await repo.ResolveRef("HEAD"));
    }
    [Theory] [InlineData("rebase")] [InlineData("cherry-pick")] [InlineData("revert")]
    public async Task HistoryOperationsRetainRecovery(string kind) {
        var repo = await Create(); string initial = await repo.ResolveRef("HEAD");
        await repo.CreateBranchAsync("topic", initial, true); await Write(repo, "feature.txt", "feature\n"); string feature = await Commit(repo, "Feature"); await repo.SwitchBranchAsync("main");
        await Write(repo, "main.txt", "main\n"); string head = await Commit(repo, "Main change");
        string target = kind == "revert" ? head : feature;
        var result = await repo.HistoryOperationAsync(kind, target, head); Assert.True(result.Completed); Assert.Equal(head, await repo.ResolveRef(result.RecoveryRef)); Assert.Equal("", await repo.OperationAsync());
    }
    [Theory] [InlineData("rebase")] [InlineData("cherry-pick")] [InlineData("revert")]
    public async Task HistoryConflictAbortRestoresOriginalHead(string kind) {
        var repo = await Create(); await Write(repo, "file.txt", "first\n"); string first = await Commit(repo, "First");
        await repo.CreateBranchAsync("topic", "HEAD", true); await Write(repo, "file.txt", "topic\n"); string topic = await Commit(repo, "Topic"); await repo.SwitchBranchAsync("main");
        await Write(repo, "file.txt", "main\n"); string head = await Commit(repo, "Main");
        Assert.False((await repo.HistoryOperationAsync(kind, kind == "revert" ? first : topic, head)).Completed);
        Assert.Equal(kind, await repo.OperationAsync()); Assert.True((await repo.FinishOperationAsync(kind, true)).Completed); Assert.Equal(head, await repo.ResolveRef("HEAD"));
        Assert.False((await repo.HistoryOperationAsync(kind, kind == "revert" ? first : topic, head)).Completed);
        await Write(repo, "file.txt", "resolved\n"); await repo.StageFileAsync("file.txt");
        Assert.True((await repo.FinishOperationAsync(kind, false)).Completed); Assert.Equal("", await repo.OperationAsync());
        Assert.Equal("resolved\n", await File.ReadAllTextAsync(Path.Combine(repo.Root, "file.txt")));
    }
    [Fact] public async Task AmendPreservesStagedFilesAndResetKeepsWorkingFiles() {
        var repo = await Create(); string initial = await repo.ResolveRef("HEAD"); await Write(repo, "new.txt", "staged"); await repo.StageFileAsync("new.txt");
        await repo.AmendMessageAsync("New subject\n\nBody", initial); string amended = await repo.ResolveRef("HEAD"); Assert.NotEqual(initial, amended);
        Assert.Equal("New subject", (await repo.ReadManagementAsync()).Commits[0].Subject); Assert.Contains((await repo.ReadStateAsync()).Changes, c => c.Path == "new.txt" && c.IsStaged);
        Assert.DoesNotContain("new.txt", await repo.Git("ls-tree", "--name-only", "HEAD"));
        await repo.ResetKeepingFilesAsync(initial, amended, true); Assert.True(File.Exists(Path.Combine(repo.Root, "new.txt"))); Assert.Contains((await repo.ReadStateAsync()).Changes, c => c.IsStaged);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.ResetKeepingFilesAsync(amended, amended, false));
        await repo.ResetKeepingFilesAsync(initial, initial, false); Assert.Contains((await repo.ReadStateAsync()).Changes, c => c.Path == "new.txt" && !c.IsStaged);
    }
    [Fact] public async Task ThreeWayReadsPinnedSourcesAndHandlesAddedDeletedFiles() {
        var repo = await Create(); string basis = await repo.ResolveRef("HEAD"); await repo.CreateBranchAsync("left", basis, true);
        await Write(repo, "left.txt", "left\n"); File.Delete(Path.Combine(repo.Root, "file.txt")); await Commit(repo, "Left");
        await repo.SwitchBranchAsync("main"); await Write(repo, "file.txt", "right\n"); await Commit(repo, "Right");
        var revisions = await repo.ThreeWayRevisionsAsync("left", "main"); Assert.Equal(basis, revisions.Base); Assert.Equal(2, revisions.Files.Count);
        var file = await repo.ThreeWayFileAsync(revisions, "file.txt"); Assert.False(file.LeftExists); Assert.Equal("base\n", file.Base); Assert.Equal("right\n", file.Right);
        var added = await repo.ThreeWayFileAsync(revisions, "left.txt"); Assert.False(added.BaseExists); Assert.False(added.RightExists); Assert.Equal("left\n", added.Left);
        await Write(repo, "file.txt", "uncommitted\n"); Assert.Equal("right\n", (await repo.ThreeWayFileAsync(revisions, "file.txt")).Right);
    }
    [Fact] public async Task CloneCreatesASeparateWorkingRepository() {
        var repo = await Create(); string path = Path.Combine(_root, "clone"); var clone = await GitRepository.CloneAsync(repo.Root, path);
        Assert.Equal(await repo.ResolveRef("HEAD"), await clone.ResolveRef("HEAD")); Assert.True(File.Exists(Path.Combine(path, "file.txt")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => GitRepository.CloneAsync(repo.Root, path));
    }
    [Theory] [InlineData("--upload-pack=bad")] [InlineData("ext::bad")][InlineData("https://a\nb")]
    public void RejectsRemoteHelperCommands(string url) => Assert.Throws<InvalidOperationException>(() => GitRepository.ValidateRemoteUrl(url));
    public void Dispose() {
        string full = Path.GetFullPath(_root);
        if (!full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("gitland-operations-")) throw new InvalidOperationException("Unexpected test path.");
        if (!Directory.Exists(full)) return;
        foreach (string path in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories)) File.SetAttributes(path, FileAttributes.Normal);
        Directory.Delete(full, true);
    }
}
