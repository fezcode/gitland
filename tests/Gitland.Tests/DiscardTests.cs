using Gitland.Core;
using Xunit;

namespace Gitland.Tests;

public sealed class DiscardTests : IDisposable {
    readonly string _root = Path.Combine(Path.GetTempPath(), "gitland-discard-" + Guid.NewGuid().ToString("N"));
    async Task<GitRepository> Create() {
        var repo = await GitRepository.InitializeAsync(new(Path.Combine(_root, "repo"), "main", false));
        await repo.Git("config", "user.name", "Test"); await repo.Git("config", "user.email", "test@example.invalid"); await repo.Git("config", "core.autocrlf", "false");
        await Write(repo, "file.txt", "base\n"); await Commit(repo, "Base"); return repo;
    }
    static Task Write(GitRepository repo, string path, string text) => File.WriteAllTextAsync(Path.Combine(repo.Root, path), text);
    static string Read(GitRepository repo, string path) => File.ReadAllText(Path.Combine(repo.Root, path));
    static bool Exists(GitRepository repo, string path) => File.Exists(Path.Combine(repo.Root, path));
    static async Task<string> Commit(GitRepository repo, string message) { await repo.StageAllAsync(); var state = await repo.ReadManagementAsync(); return await repo.CommitAsync(message, state.IndexTree, state.Head); }

    [Fact] public async Task DiscardingUnstagedEditsKeepsWhatIsAlreadyStaged() {
        var repo = await Create();
        await Write(repo, "file.txt", "staged\n"); await repo.StageFileAsync("file.txt");
        await Write(repo, "file.txt", "unstaged\n");
        var result = await repo.DiscardUnstagedAsync(["file.txt"]);
        Assert.Equal("staged\n", Read(repo, "file.txt"));
        Assert.Equal("staged\n", await repo.Git("show", ":file.txt"));
        Assert.NotEqual("", result.RecoveryRef);
    }

    [Fact] public async Task DiscardingAFileReturnsItToTheCommittedState() {
        var repo = await Create();
        await Write(repo, "file.txt", "staged\n"); await repo.StageFileAsync("file.txt");
        await Write(repo, "file.txt", "unstaged\n");
        await repo.DiscardFileAsync(["file.txt"]);
        Assert.Equal("base\n", Read(repo, "file.txt"));
        Assert.Empty((await repo.ReadStateAsync()).Changes);
    }

    [Fact] public async Task DiscardedWorkIsRecoverableFromTheSnapshotRef() {
        var repo = await Create();
        await Write(repo, "file.txt", "valuable\n");
        var result = await repo.DiscardUnstagedAsync(["file.txt"]);
        Assert.Equal("base\n", Read(repo, "file.txt"));
        // The recovery ref must carry the discarded text, not merely exist.
        Assert.Equal("valuable\n", await repo.Git("show", result.RecoveryRef + ":file.txt"));
        Assert.Contains((await repo.ReadToolsAsync()).Recovery, r => r.Reference == result.RecoveryRef);
    }

    [Fact] public async Task UntrackedFilesAreCopiedOutBeforeTheyAreDeleted() {
        var repo = await Create();
        await Write(repo, "scratch.txt", "unsaved\n");
        var result = await repo.CleanUntrackedAsync(["scratch.txt"]);
        Assert.False(Exists(repo, "scratch.txt"));
        Assert.Equal("unsaved\n", await File.ReadAllTextAsync(Path.Combine(result.BackupDirectory, "scratch.txt")));
    }

    [Fact] public async Task CleanRefusesTrackedFilesSoCommittedWorkIsNeverSweptAway() {
        var repo = await Create();
        await Write(repo, "file.txt", "changed\n");
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.CleanUntrackedAsync(["file.txt"]));
        Assert.Equal("changed\n", Read(repo, "file.txt"));
    }

    [Fact] public async Task DiscardingAHunkLeavesTheOtherHunksInPlace() {
        var repo = await Create();
        await Write(repo, "file.txt", string.Join('\n', Enumerable.Range(0, 40).Select(i => "line " + i)) + "\n");
        await Commit(repo, "Long");
        var lines = Enumerable.Range(0, 40).Select(i => "line " + i).ToArray();
        lines[2] = "FIRST"; lines[36] = "SECOND";
        await Write(repo, "file.txt", string.Join('\n', lines) + "\n");
        string patch = await repo.ReadPatchAsync("file.txt", false);
        Assert.Equal(2, GitRepository.ParseHunks(patch).Count);
        await repo.DiscardHunkAsync("file.txt", patch, 0);
        string text = Read(repo, "file.txt");
        Assert.DoesNotContain("FIRST", text);
        Assert.Contains("SECOND", text);
    }

    [Fact] public async Task DiscardingEverythingClearsTrackedChangesAndCanKeepUntrackedFiles() {
        var repo = await Create();
        await Write(repo, "file.txt", "changed\n"); await Write(repo, "scratch.txt", "keep\n");
        await repo.DiscardEverythingAsync(false);
        Assert.Equal("base\n", Read(repo, "file.txt"));
        Assert.True(Exists(repo, "scratch.txt"));
    }

    [Fact] public async Task DiscardingEverythingWithUntrackedSweepsAndBacksThemUp() {
        var repo = await Create();
        await Write(repo, "file.txt", "changed\n"); await Write(repo, "scratch.txt", "sweep\n");
        var result = await repo.DiscardEverythingAsync(true);
        Assert.Equal("base\n", Read(repo, "file.txt"));
        Assert.False(Exists(repo, "scratch.txt"));
        Assert.Equal("sweep\n", await File.ReadAllTextAsync(Path.Combine(result.BackupDirectory, "scratch.txt")));
    }

    [Fact] public async Task DiscardingNothingIsRejectedRatherThanSilentlySucceeding() {
        var repo = await Create();
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.DiscardUnstagedAsync([]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.DiscardEverythingAsync(false));
    }

    [Fact] public async Task DiscardRejectsPathsOutsideTheRepository() {
        var repo = await Create();
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.DiscardFileAsync(["../escape.txt"]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.DiscardUnstagedAsync([".git/config"]));
    }

    [Fact] public async Task HardResetMovesTheBranchAndRecordsBothTheCommitAndTheWorkingTree() {
        var repo = await Create();
        string first = await repo.ResolveRef("HEAD");
        await Write(repo, "file.txt", "second\n"); string second = await Commit(repo, "Second");
        await Write(repo, "file.txt", "in progress\n");
        string recovery = await repo.ResetHardAsync(first, second);
        Assert.Equal("base\n", Read(repo, "file.txt"));
        Assert.Equal(first, await repo.ResolveRef("HEAD"));
        var tools = await repo.ReadToolsAsync();
        Assert.Contains(tools.Recovery, r => r.Reference == recovery && r.Hash == second);
        // The uncommitted edit must also be recoverable, not just the abandoned commit.
        Assert.Contains(tools.Recovery, r => r.Reference.EndsWith("-reset-hard") && r.Reference != recovery);
    }

    [Fact] public async Task HardResetRefusesAStaleHead() {
        var repo = await Create();
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.ResetHardAsync("HEAD", new string('0', 40)));
    }

    public void Dispose() {
        if (!Directory.Exists(_root)) return;
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }
}
