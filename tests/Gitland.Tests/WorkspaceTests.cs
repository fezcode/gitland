using Gitland.Core;
using Xunit;

namespace Gitland.Tests;

/// <summary>Covers the multi-repository workspace scan behind the Workspace view: which folders
/// count as repositories, and what a single row reports about one.</summary>
public sealed class WorkspaceTests : IDisposable {
    readonly string _directory = Path.Combine(Path.GetTempPath(), "gitland-workspace-" + Guid.NewGuid().ToString("N"));
    public WorkspaceTests() => Directory.CreateDirectory(_directory);

    async Task<GitRepository> Create(string name) {
        var repo = await GitRepository.InitializeAsync(new(Path.Combine(_directory, name), "main", false, "none"));
        await repo.Git("config", "user.name", "Gitland Test"); await repo.Git("config", "user.email", "gitland@example.invalid"); await repo.Git("config", "core.autocrlf", "false");
        return repo;
    }
    static async Task<string> Commit(GitRepository repo, string message = "Initial commit") {
        await repo.StageAllAsync(); var state = await repo.ReadManagementAsync(); return await repo.CommitAsync(message, state.IndexTree, state.Head);
    }
    static Task Write(GitRepository repo, string path, string text) => File.WriteAllTextAsync(Path.Combine(repo.Root, path), text);
    void Folder(string name) => Directory.CreateDirectory(Path.Combine(_directory, name));

    // ---- Finding repositories ----------------------------------------------

    [Fact] public async Task FindRepositoriesListsDirectChildrenThatHoldAGitEntry() {
        await Create("repo");
        Folder("notes");
        await File.WriteAllTextAsync(Path.Combine(_directory, "loose.txt"), "not a folder\n");
        Assert.Equal([Path.Combine(_directory, "repo")], WorkspaceScan.FindRepositories(_directory));
    }

    /// <summary>A linked worktree and a submodule checkout carry a <c>.git</c> file, not a folder.
    /// Testing for a directory alone would drop them from the table with no explanation.</summary>
    [Fact] public void FindRepositoriesAcceptsAGitFileSoLinkedWorktreesAppear() {
        Folder("worktree");
        File.WriteAllText(Path.Combine(_directory, "worktree", ".git"), "gitdir: ../repo/.git/worktrees/worktree\n");
        Assert.Equal([Path.Combine(_directory, "worktree")], WorkspaceScan.FindRepositories(_directory));
    }

    [Fact] public async Task FindRepositoriesDoesNotDescendBelowTheDirectChildren() {
        Folder("archive");
        await Create(Path.Combine("archive", "old"));
        Assert.Empty(WorkspaceScan.FindRepositories(_directory));
    }

    [Fact] public void FindRepositoriesReturnsNothingForAFolderThatIsNotThere() =>
        Assert.Empty(WorkspaceScan.FindRepositories(Path.Combine(_directory, "absent")));

    // ---- Reading one repository --------------------------------------------

    [Fact] public async Task ReadReportsTheBranchAndNoChangesForACleanRepository() {
        var repo = await Create("repo");
        await Write(repo, "file.txt", "content\n"); await Commit(repo);
        var summary = await WorkspaceScan.ReadAsync(repo.Root);
        Assert.Equal("repo", summary.Name);
        Assert.Equal("main", summary.Branch);
        Assert.False(summary.IsDirty);
        Assert.Null(summary.Error);
    }

    [Fact] public async Task ReadCountsUntrackedModifiedAndDeletedFilesSeparately() {
        var repo = await Create("repo");
        await Write(repo, "kept.txt", "one\n"); await Write(repo, "gone.txt", "two\n"); await Commit(repo);
        await Write(repo, "kept.txt", "changed\n");
        await Write(repo, "fresh.txt", "new\n");
        File.Delete(Path.Combine(repo.Root, "gone.txt"));
        var summary = await WorkspaceScan.ReadAsync(repo.Root);
        Assert.Equal(1, summary.Added);
        Assert.Equal(1, summary.Modified);
        Assert.Equal(1, summary.Deleted);
        Assert.True(summary.IsDirty);
    }

    /// <summary>A repository stopped mid-merge reads as modified files to a plain status count.
    /// Gitland is a merge client, so the row says "conflicted" rather than quietly saying "modified".</summary>
    [Fact] public async Task ReadCountsConflictedFilesRatherThanCallingThemModified() {
        var repo = await Create("repo");
        await Write(repo, "file.txt", "base\n"); await Commit(repo);
        await repo.Git("checkout", "-b", "other");
        await Write(repo, "file.txt", "theirs\n"); await Commit(repo, "Theirs");
        await repo.Git("checkout", "main");
        await Write(repo, "file.txt", "ours\n"); await Commit(repo, "Ours");
        await Assert.ThrowsAsync<CommandFailedException>(() => repo.Git("merge", "other", "--no-edit"));
        var summary = await WorkspaceScan.ReadAsync(repo.Root);
        Assert.Equal(1, summary.Conflicts);
        Assert.Equal(0, summary.Modified);
        Assert.True(summary.IsDirty);
    }

    [Fact] public async Task ReadCountsConfiguredRemotes() {
        var repo = await Create("repo");
        await Write(repo, "file.txt", "content\n"); await Commit(repo);
        Assert.Equal(0, (await WorkspaceScan.ReadAsync(repo.Root)).Remotes);
        await repo.Git("remote", "add", "origin", "https://example.invalid/one.git");
        await repo.Git("remote", "add", "mirror", "https://example.invalid/two.git");
        Assert.Equal(2, (await WorkspaceScan.ReadAsync(repo.Root)).Remotes);
    }

    [Fact] public async Task ReadReportsCommitsAheadOfTheUpstream() {
        var repo = await Published("repo");
        await Write(repo, "later.txt", "ahead\n"); await Commit(repo, "Ahead one");
        var summary = await WorkspaceScan.ReadAsync(repo.Root);
        Assert.Equal(1, summary.Ahead);
        Assert.Equal(0, summary.Behind);
    }

    [Fact] public async Task ReadReportsCommitsBehindTheUpstream() {
        var repo = await Published("repo");
        var other = await Clone("other");
        await Write(other, "theirs.txt", "behind\n"); await Commit(other, "Behind one");
        await other.Git("push", "origin", "main");
        await repo.Git("fetch", "origin");
        var summary = await WorkspaceScan.ReadAsync(repo.Root);
        Assert.Equal(0, summary.Ahead);
        Assert.Equal(1, summary.Behind);
    }

    [Fact] public async Task ReadReportsTheBranchOfARepositoryWithNoCommitsYet() {
        var repo = await Create("repo");
        Assert.Equal("main", (await WorkspaceScan.ReadAsync(repo.Root)).Branch);
    }

    [Fact] public async Task ReadReportsADetachedHeadInsteadOfFailing() {
        var repo = await Create("repo");
        await Write(repo, "file.txt", "content\n"); string head = await Commit(repo);
        await repo.Git("checkout", head);
        var summary = await WorkspaceScan.ReadAsync(repo.Root);
        Assert.Equal("HEAD (detached)", summary.Branch);
        Assert.Null(summary.Error);
    }

    // ---- Scanning a whole folder -------------------------------------------

    [Fact] public async Task ScanReturnsARowPerRepositoryOrderedByName() {
        foreach (string name in new[] { "zulu", "alpha", "mike" }) {
            var repo = await Create(name);
            await Write(repo, "file.txt", "content\n"); await Commit(repo);
        }
        Folder("not-a-repository");
        var rows = await WorkspaceScan.ScanAsync(_directory);
        Assert.Equal(["alpha", "mike", "zulu"], rows.Select(r => r.Name));
    }

    /// <summary>One unreadable repository must not blank the whole table; the row stays and says why.</summary>
    [Fact] public async Task ScanRecordsAnErrorRowForARepositoryItCannotRead() {
        var healthy = await Create("healthy");
        await Write(healthy, "file.txt", "content\n"); await Commit(healthy);
        Folder("broken");
        File.WriteAllText(Path.Combine(_directory, "broken", ".git"), "gitdir: nowhere-at-all\n");
        var rows = await WorkspaceScan.ScanAsync(_directory);
        Assert.Equal(["broken", "healthy"], rows.Select(r => r.Name));
        Assert.NotNull(rows[0].Error);
        Assert.Null(rows[1].Error);
    }

    // ---- Helpers ------------------------------------------------------------

    /// <summary>A repository with a bare origin and an upstream-tracking branch.</summary>
    async Task<GitRepository> Published(string name) {
        await new GitRepository(_directory).Git("init", "--bare", "origin.git");
        var repo = await Create(name);
        await Write(repo, "file.txt", "content\n"); await Commit(repo);
        await repo.Git("remote", "add", "origin", Path.Combine(_directory, "origin.git"));
        await repo.Git("push", "-u", "origin", "main");
        return repo;
    }
    async Task<GitRepository> Clone(string name) {
        await new GitRepository(_directory).Git("clone", Path.Combine(_directory, "origin.git"), name);
        var repo = new GitRepository(Path.Combine(_directory, name));
        await repo.Git("config", "user.name", "Gitland Test"); await repo.Git("config", "user.email", "gitland@example.invalid"); await repo.Git("config", "core.autocrlf", "false");
        return repo;
    }

    public void Dispose() {
        try { foreach (string path in Directory.EnumerateFiles(_directory, "*", SearchOption.AllDirectories)) File.SetAttributes(path, FileAttributes.Normal); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        try { Directory.Delete(_directory, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
