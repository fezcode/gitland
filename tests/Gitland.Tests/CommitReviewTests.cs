using Gitland.Core;
using Xunit;

namespace Gitland.Tests;

public sealed class CommitReviewTests : IDisposable {
    readonly string _root = Path.Combine(Path.GetTempPath(), "gitland-commit-review-" + Guid.NewGuid().ToString("N"));
    async Task<GitRepository> Create() {
        var repo = await GitRepository.InitializeAsync(new(_root, "main", false));
        await repo.Git("config", "user.name", "Test"); await repo.Git("config", "user.email", "test@example.invalid"); await repo.Git("config", "core.autocrlf", "false"); return repo;
    }
    Task Write(string path, string text) => File.WriteAllTextAsync(Path.Combine(_root, path), text);
    async Task CommitAll(GitRepository repo) { await repo.StageAllAsync(); var review = await repo.ReadManagementAsync(); await repo.CommitAsync("Initial", review.IndexTree, review.Head); }
    [Fact] public async Task ReviewOfFirstCommitIncludesOnlyStagedLines() {
        var repo = await Create(); await Write("first.txt", "one\ntwo\n"); await repo.StageFileAsync("first.txt"); await Write("first.txt", "one\ntwo\nnot staged\n"); await Write("untracked.txt", "untracked\n");
        var review = await repo.ReadCommitReviewAsync(); var file = Assert.Single(review.Files);
        Assert.Null(review.Head); Assert.Equal("Added", file.Label); Assert.Equal(2, file.Added); Assert.Equal(0, file.Removed); Assert.Equal(2, review.Unstaged);
        Assert.Contains("first.txt (+2 / -0)", review.MessageSummary()); Assert.DoesNotContain("untracked.txt", review.MessageSummary());
        await repo.CommitAsync("Subject\n\nLong body\n\n" + review.MessageSummary(), review.IndexTree, review.Head, review.Branch);
        Assert.Equal("one\ntwo\n", await repo.Git("show", "HEAD:first.txt")); Assert.Contains("Long body", await repo.CommitMessageAsync("HEAD"));
    }
    [Fact] public async Task SummaryHandlesRenamesDeletionsBinaryAndUnicodeNames() {
        var repo = await Create(); await Write("old.txt", "keep\n"); await Write("delete.txt", "remove\n"); await Write("edit.txt", "before\n"); await CommitAll(repo);
        await repo.Git("mv", "old.txt", "renamed ü.txt"); File.Delete(Path.Combine(_root, "delete.txt")); await Write("edit.txt", "after\nextra\n");
        await File.WriteAllBytesAsync(Path.Combine(_root, "binary.dat"), [0, 1, 2, 3]); await repo.StageAllAsync();
        var review = await repo.ReadCommitReviewAsync(); Assert.Equal(4, review.Files.Count);
        var rename = Assert.Single(review.Files.Where(f => f.Status == 'R')); Assert.Equal("old.txt", rename.OldPath); Assert.Equal("renamed ü.txt", rename.Path); Assert.Equal(0, rename.Added);
        var edit = Assert.Single(review.Files.Where(f => f.Path == "edit.txt")); Assert.Equal(2, edit.Added); Assert.Equal(1, edit.Removed);
        Assert.True(review.Files.Single(f => f.Path == "binary.dat").Binary); Assert.Equal(1, review.Files.Single(f => f.Status == 'D').Removed);
        Assert.Contains("Renamed: old.txt → renamed ü.txt", review.MessageSummary()); Assert.Contains("binary.dat (binary)", review.MessageSummary());
    }
    [Fact] public async Task ReviewedCommitRejectsChangedIndexAndBranchEvenAtSameHead() {
        var repo = await Create(); await Write("file.txt", "base\n"); await CommitAll(repo); await repo.CreateBranchAsync("other", "HEAD", false);
        await Write("file.txt", "staged\n"); await repo.StageFileAsync("file.txt"); var review = await repo.ReadCommitReviewAsync();
        await Write("new.txt", "new\n"); await repo.StageFileAsync("new.txt");
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.CommitAsync("Rejected", review.IndexTree, review.Head, review.Branch));
        review = await repo.ReadCommitReviewAsync(); await repo.Git("switch", "other");
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.CommitAsync("Wrong branch", review.IndexTree, review.Head, review.Branch));
        Assert.Equal("Initial", (await repo.ReadManagementAsync()).Commits[0].Subject);
    }
    public void Dispose() {
        var full = Path.GetFullPath(_root);
        if (!full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("gitland-commit-review-")) throw new InvalidOperationException("Unexpected fixture path.");
        if (!Directory.Exists(full)) return;
        foreach (var file in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal); Directory.Delete(full, true);
    }
}
