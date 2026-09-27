using Gitland.Core;
using Xunit;

namespace Gitland.Tests;

public sealed class CommitComparisonTests : IDisposable {
    readonly string _root = Path.Combine(Path.GetTempPath(), "gitland-history-diff-" + Guid.NewGuid().ToString("N"));
    async Task<GitRepository> Create() {
        var repo = await GitRepository.InitializeAsync(new(_root, "main", false));
        await repo.Git("config", "user.name", "Test"); await repo.Git("config", "user.email", "test@example.invalid"); await repo.Git("config", "core.autocrlf", "false"); return repo;
    }
    Task Write(string path, string text) => File.WriteAllTextAsync(Path.Combine(_root, path), text);
    static async Task Commit(GitRepository repo) { await repo.Git("add", "."); await repo.Git("commit", "-m", "History test"); }
    [Fact] public async Task RootCommitUsesEmptyTreeAndPinsSourceEvenWhenHeadMoves() {
        var repo = await Create(); await Write("source.cs", "initial\n"); await Commit(repo);
        var review = await repo.ReadCommitComparisonAsync("HEAD"); Assert.Null(review.Parent); Assert.Empty(review.Parents);
        await Write("source.cs", "next commit\n"); await Commit(repo); await Write("source.cs", "uncommitted\n");
        var before = await repo.Git("status", "--porcelain=v1"); var index = await repo.Git("write-tree");
        var diff = await repo.ReadCommitFileAsync(review, Assert.Single(review.Files));
        Assert.Equal("", diff.Left); Assert.Equal("initial\n", diff.Right);
        Assert.Equal(before, await repo.Git("status", "--porcelain=v1")); Assert.Equal(index, await repo.Git("write-tree"));
    }
    [Fact] public async Task HandlesAddedDeletedRenamedBinaryAndModifiedFiles() {
        var repo = await Create(); await Write("old name.txt", "same\n"); await Write("delete.txt", "removed\n"); await Write("edit.txt", "before\n"); await Commit(repo);
        await repo.Git("mv", "old name.txt", "new ü.txt"); File.Delete(Path.Combine(_root, "delete.txt")); await Write("edit.txt", "after\n"); await Write("added.txt", "new\n"); await File.WriteAllBytesAsync(Path.Combine(_root, "binary.dat"), [0, 1, 2]); await Commit(repo);
        var review = await repo.ReadCommitComparisonAsync("HEAD"); Assert.Equal(5, review.Files.Count);
        async Task<FileComparison> Diff(string path) => await repo.ReadCommitFileAsync(review, review.Files.Single(f => f.Path == path));
        var rename = await Diff("new ü.txt"); Assert.Equal("same\n", rename.Left); Assert.Equal(rename.Left, rename.Right);
        Assert.Equal("old name.txt", review.Files.Single(f => f.Path == "new ü.txt").OldPath);
        Assert.Equal("", (await Diff("delete.txt")).Right); Assert.Equal("removed\n", (await Diff("delete.txt")).Left);
        Assert.Equal("", (await Diff("added.txt")).Left); Assert.Equal("new\n", (await Diff("added.txt")).Right);
        Assert.Equal("before\n", (await Diff("edit.txt")).Left); Assert.Equal("after\n", (await Diff("edit.txt")).Right);
        Assert.True((await Diff("binary.dat")).Binary);
    }
    [Fact] public async Task MergeCommitComparesAgainstTheChosenParentAndEmptyCommitIsEmpty() {
        var repo = await Create(); await Write("base.txt", "base\n"); await Commit(repo); await repo.Git("checkout", "-b", "side");
        await Write("side.txt", "side\n"); await Commit(repo); await repo.Git("checkout", "main"); await Write("main.txt", "main\n"); await Commit(repo);
        await repo.Git("merge", "--no-ff", "side", "-m", "Merge side");
        var first = await repo.ReadCommitComparisonAsync("HEAD"); var second = await repo.ReadCommitComparisonAsync("HEAD", 1);
        Assert.Equal(2, first.Parents.Count); Assert.Equal("side.txt", Assert.Single(first.Files).Path); Assert.Equal("main.txt", Assert.Single(second.Files).Path);
        Assert.NotEqual(first.Parent, second.Parent); Assert.Equal(first.Commit, second.Commit);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repo.ReadCommitComparisonAsync("HEAD", 2));
        await repo.Git("commit", "--allow-empty", "-m", "Empty"); Assert.Empty((await repo.ReadCommitComparisonAsync("HEAD")).Files);
    }
    public void Dispose() {
        if (!Directory.Exists(_root)) return;
        if (!Path.GetFullPath(_root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(_root).StartsWith("gitland-history-diff-")) throw new InvalidOperationException("Unexpected test path.");
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_root, true);
    }
}
