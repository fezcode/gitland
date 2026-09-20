using Gitland.Core;
using Xunit;

namespace Gitland.Tests;

/// <summary>Staging and unstaging several files at once, for the multi-file selection.</summary>
public sealed class BatchStagingTests : IDisposable {
    readonly string _root = Path.Combine(Path.GetTempPath(), "gitland-batch-" + Guid.NewGuid().ToString("N"));
    async Task<GitRepository> Create() {
        var repo = await GitRepository.InitializeAsync(new(Path.Combine(_root, "repo"), "main", false));
        await repo.Git("config", "user.name", "Test"); await repo.Git("config", "user.email", "test@example.invalid"); await repo.Git("config", "core.autocrlf", "false");
        await Write(repo, "one.txt", "1\n"); await Write(repo, "two.txt", "2\n"); await Write(repo, "three.txt", "3\n");
        await Commit(repo, "Base"); return repo;
    }
    static Task Write(GitRepository repo, string path, string text) => File.WriteAllTextAsync(Path.Combine(repo.Root, path), text);
    static async Task Commit(GitRepository repo, string message) { await repo.StageAllAsync(); var state = await repo.ReadManagementAsync(); await repo.CommitAsync(message, state.IndexTree, state.Head); }
    static async Task<string[]> Staged(GitRepository repo) =>
        (await repo.ReadStateAsync()).Changes.Where(c => c.IsStaged).Select(c => c.Path).Order(StringComparer.Ordinal).ToArray();

    [Fact] public async Task SeveralFilesStageInOneCall() {
        var repo = await Create();
        await Write(repo, "one.txt", "changed\n"); await Write(repo, "two.txt", "changed\n"); await Write(repo, "three.txt", "changed\n");
        await repo.StageFilesAsync(["one.txt", "three.txt"]);
        Assert.Equal(new[] { "one.txt", "three.txt" }, (await Staged(repo)).AsEnumerable());
    }

    [Fact] public async Task SeveralFilesUnstageInOneCall() {
        var repo = await Create();
        await Write(repo, "one.txt", "changed\n"); await Write(repo, "two.txt", "changed\n");
        await repo.StageFilesAsync(["one.txt", "two.txt"]);
        await repo.UnstageFilesAsync(["one.txt"]);
        Assert.Equal(new[] { "two.txt" }, (await Staged(repo)).AsEnumerable());
    }

    [Fact] public async Task UntrackedFilesStageAlongsideModifiedOnes() {
        var repo = await Create();
        await Write(repo, "one.txt", "changed\n"); await Write(repo, "new.txt", "new\n");
        await repo.StageFilesAsync(["one.txt", "new.txt"]);
        Assert.Equal(new[] { "new.txt", "one.txt" }, (await Staged(repo)).AsEnumerable());
    }

    [Fact] public async Task OneBadPathStagesNothingAtAll() {
        var repo = await Create();
        await Write(repo, "one.txt", "changed\n"); await Write(repo, "two.txt", "changed\n");
        // Validation runs over the whole batch first, so the good paths are not staged either.
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.StageFilesAsync(["one.txt", "../escape.txt", "two.txt"]));
        Assert.Empty(await Staged(repo));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.UnstageFilesAsync(["one.txt", ".git/config"]));
        Assert.Empty(await Staged(repo));
    }

    [Fact] public async Task AnEmptyBatchIsRejectedRatherThanStagingEverything() {
        var repo = await Create();
        await Write(repo, "one.txt", "changed\n");
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.StageFilesAsync([]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.UnstageFilesAsync([]));
        Assert.Empty(await Staged(repo));
    }

    [Fact] public async Task UnstagingWorksBeforeTheFirstCommitExists() {
        var repo = await GitRepository.InitializeAsync(new(Path.Combine(_root, "fresh"), "main", false));
        await repo.Git("config", "user.name", "Test"); await repo.Git("config", "user.email", "test@example.invalid");
        await Write(repo, "a.txt", "a\n"); await Write(repo, "b.txt", "b\n");
        await repo.StageFilesAsync(["a.txt", "b.txt"]);
        Assert.Equal(new[] { "a.txt", "b.txt" }, (await Staged(repo)).AsEnumerable());
        await repo.UnstageFilesAsync(["a.txt", "b.txt"]);
        Assert.Empty(await Staged(repo));
    }

    public void Dispose() {
        if (!Directory.Exists(_root)) return;
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }
}
