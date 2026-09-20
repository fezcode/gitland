using Gitland.Core;
using Xunit;

namespace Gitland.Tests;

/// <summary>Covers the operations added to close the gaps against other Git clients.</summary>
public sealed class CompletenessTests : IDisposable {
    readonly string _root = Path.Combine(Path.GetTempPath(), "gitland-complete-" + Guid.NewGuid().ToString("N"));
    async Task<GitRepository> Create() {
        var repo = await GitRepository.InitializeAsync(new(Path.Combine(_root, "repo"), "main", false));
        await repo.Git("config", "user.name", "Test"); await repo.Git("config", "user.email", "test@example.invalid"); await repo.Git("config", "core.autocrlf", "false");
        await Write(repo, "file.txt", "base\n"); await Commit(repo, "Base"); return repo;
    }
    static Task Write(GitRepository repo, string path, string text) => File.WriteAllTextAsync(Path.Combine(repo.Root, path), text);
    static string Read(GitRepository repo, string path) => File.ReadAllText(Path.Combine(repo.Root, path));
    static async Task<string> Commit(GitRepository repo, string message) { await repo.StageAllAsync(); var state = await repo.ReadManagementAsync(); return await repo.CommitAsync(message, state.IndexTree, state.Head); }

    // ---- Tier 1 -------------------------------------------------------------

    [Fact] public async Task AmendCanFoldStagedChangesIntoTheLastCommitOrLeaveThemAlone() {
        var repo = await Create();
        await Write(repo, "file.txt", "amended\n"); await repo.StageFileAsync("file.txt");
        string head = await repo.ResolveRef("HEAD");
        await repo.AmendMessageAsync("Base, amended", head, includeStaged: true);
        Assert.Equal("amended\n", await repo.Git("show", "HEAD:file.txt"));
        Assert.Empty((await repo.ReadStateAsync()).Changes);

        await Write(repo, "file.txt", "later\n"); await repo.StageFileAsync("file.txt");
        await repo.AmendMessageAsync("Message only", await repo.ResolveRef("HEAD"));
        Assert.Equal("amended\n", await repo.Git("show", "HEAD:file.txt"));
        Assert.Single((await repo.ReadStateAsync()).Changes);
    }

    [Fact] public async Task AHunkCanBeUnstagedWithoutTouchingTheWorkingTree() {
        var repo = await Create();
        await Write(repo, "file.txt", string.Join('\n', Enumerable.Range(0, 40).Select(i => "line " + i)) + "\n");
        await Commit(repo, "Long");
        var lines = Enumerable.Range(0, 40).Select(i => "line " + i).ToArray();
        lines[2] = "FIRST"; lines[36] = "SECOND";
        await Write(repo, "file.txt", string.Join('\n', lines) + "\n");
        await repo.StageFileAsync("file.txt");
        string staged = await repo.ReadPatchAsync("file.txt", true);
        Assert.Equal(2, GitRepository.ParseHunks(staged).Count);
        await repo.UnstageHunkAsync("file.txt", staged, 0);
        Assert.DoesNotContain("FIRST", await repo.Git("show", ":file.txt"));
        Assert.Contains("SECOND", await repo.Git("show", ":file.txt"));
        Assert.Contains("FIRST", Read(repo, "file.txt"));       // the working tree keeps both edits
    }

    [Fact] public async Task UnstageHunkRefusesAStalePatch() {
        var repo = await Create();
        await Write(repo, "file.txt", "changed\n"); await repo.StageFileAsync("file.txt");
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.UnstageHunkAsync("file.txt", "not the patch", 0));
    }

    [Fact] public async Task PullRejectsAnUnknownModeAndADirtyTreeWithoutAutostash() {
        var repo = await Create();
        await Assert.ThrowsAsync<ArgumentException>(() => repo.PullAsync("squash"));
        await Write(repo, "file.txt", "dirty\n");
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.PullAsync("rebase"));
    }

    [Fact] public async Task HistorySearchMatchesAuthorAndMessageBeyondTheDisplayedPage() {
        var repo = await Create();
        for (int i = 0; i < 12; i++) { await Write(repo, "file.txt", "v" + i + "\n"); await Commit(repo, i == 0 ? "needle in here" : "filler " + i); }
        var byText = await repo.ReadHistoryAsync(new(Text: "needle"));
        Assert.Equal("needle in here", Assert.Single(byText).Subject);
        // The match is older than this limit, so a client-side filter over the page would miss it.
        var limited = await repo.ReadHistoryAsync(new(Text: "needle", Limit: 2));
        Assert.Single(limited);
        Assert.Equal(13, (await repo.ReadHistoryAsync(new(Author: "Test"))).Count);
        Assert.Empty(await repo.ReadHistoryAsync(new(Author: "nobody")));
    }

    [Fact] public async Task FileHistoryReturnsOnlyCommitsTouchingThatFile() {
        var repo = await Create();
        await Write(repo, "other.txt", "one\n"); await Commit(repo, "Add other");
        await Write(repo, "file.txt", "two\n"); await Commit(repo, "Change file");
        var history = await repo.ReadHistoryAsync(new(Path: "other.txt"));
        Assert.Equal("Add other", Assert.Single(history).Subject);
    }

    // ---- Tier 2 -------------------------------------------------------------

    [Fact] public async Task BlameAttributesEachLineToTheCommitThatChangedIt() {
        var repo = await Create();
        await Write(repo, "file.txt", "base\nsecond\n"); string second = await Commit(repo, "Second");
        var blame = await repo.BlameAsync("file.txt");
        Assert.Equal(2, blame.Count);
        Assert.Equal(1, blame[0].Number); Assert.Equal("base", blame[0].Text);
        Assert.Equal("second", blame[1].Text); Assert.Equal(second, blame[1].Hash);
        Assert.Equal("Test", blame[1].Author);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", blame[1].Date);
    }

    [Fact] public async Task ReflogStillShowsACommitThatAHardResetAbandoned() {
        var repo = await Create();
        string first = await repo.ResolveRef("HEAD");
        await Write(repo, "file.txt", "second\n"); string second = await Commit(repo, "Second");
        await repo.ResetHardAsync(first, second);
        Assert.Contains(await repo.ReadReflogAsync(), e => e.Hash == second);
    }

    [Fact] public async Task MovingAFileKeepsItTrackedAndRefusesAnOccupiedDestination() {
        var repo = await Create();
        await repo.MoveFileAsync("file.txt", "renamed.txt");
        Assert.True(File.Exists(Path.Combine(repo.Root, "renamed.txt")));
        Assert.Contains((await repo.ReadStateAsync()).Changes, c => c.Path == "renamed.txt" && c.Index == 'R');
        await Write(repo, "taken.txt", "x\n");
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.MoveFileAsync("renamed.txt", "taken.txt"));
    }

    [Fact] public async Task IgnoreAppendsOncePreservingExistingContent() {
        var repo = await Create();
        await Write(repo, ".gitignore", "*.log\n");
        await repo.IgnoreAsync("build/");
        await repo.IgnoreAsync("build/");                      // already present, must not duplicate
        string text = Read(repo, ".gitignore");
        Assert.Equal("*.log\nbuild/\n", text);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.IgnoreAsync("  "));
    }

    [Fact] public async Task IgnoreCreatesTheFileAndSeparatesFromAnUnterminatedLastLine() {
        var repo = await Create();
        await repo.IgnoreAsync("first");
        Assert.Equal("first\n", Read(repo, ".gitignore"));
        await File.WriteAllTextAsync(Path.Combine(repo.Root, ".gitignore"), "no-newline");
        await repo.IgnoreAsync("second");
        Assert.Equal("no-newline\nsecond\n", Read(repo, ".gitignore"));
    }

    [Fact] public async Task CherryPickAppliesARunOfCommitsInOrder() {
        var repo = await Create();
        string start = await repo.ResolveRef("HEAD");
        await repo.CreateBranchAsync("topic", start, true);
        await Write(repo, "a.txt", "a\n"); string one = await Commit(repo, "Add a");
        await Write(repo, "b.txt", "b\n"); string two = await Commit(repo, "Add b");
        await repo.SwitchBranchAsync("main");
        var result = await repo.CherryPickRangeAsync([one, two], await repo.ResolveRef("HEAD"));
        Assert.True(result.Completed);
        Assert.True(File.Exists(Path.Combine(repo.Root, "a.txt")) && File.Exists(Path.Combine(repo.Root, "b.txt")));
    }

    [Fact] public async Task RevertingAMergeCommitRequiresChoosingTheParentToKeep() {
        var repo = await Create();
        string start = await repo.ResolveRef("HEAD");
        await repo.CreateBranchAsync("topic", start, true);
        await Write(repo, "topic.txt", "topic\n"); await Commit(repo, "Topic work");
        await repo.SwitchBranchAsync("main");
        await Write(repo, "main.txt", "main\n"); await Commit(repo, "Main work");
        var merge = await repo.HistoryOperationAsync("merge", "topic", await repo.ResolveRef("HEAD"));
        Assert.True(merge.Completed);
        string head = await repo.ResolveRef("HEAD");
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.RevertCommitAsync(head, head));
        Assert.True((await repo.RevertCommitAsync(head, head, mainline: 1)).Completed);
        Assert.False(File.Exists(Path.Combine(repo.Root, "topic.txt")));
    }

    [Fact] public async Task TakingOneSideResolvesAConflictWithoutTheEditor() {
        var repo = await Create();
        string start = await repo.ResolveRef("HEAD");
        await repo.CreateBranchAsync("topic", start, true);
        await Write(repo, "file.txt", "theirs\n"); await Commit(repo, "Theirs");
        await repo.SwitchBranchAsync("main");
        await Write(repo, "file.txt", "ours\n"); await Commit(repo, "Ours");
        var merge = await repo.HistoryOperationAsync("merge", "topic", await repo.ResolveRef("HEAD"));
        Assert.False(merge.Completed);
        await repo.TakeSideAsync("file.txt", ours: true);
        Assert.Equal("ours\n", Read(repo, "file.txt"));
        Assert.DoesNotContain((await repo.ReadStateAsync()).Changes, c => c.IsConflict);
    }

    [Fact] public async Task TakeSideRefusesAFileThatIsNotConflicted() {
        var repo = await Create();
        await Write(repo, "file.txt", "changed\n");
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.TakeSideAsync("file.txt", true));
    }

    [Fact] public async Task APatchCanBeExportedAndAppliedBack() {
        var repo = await Create();
        await Write(repo, "file.txt", "patched\n");
        string patch = Path.Combine(_root, "change.patch");
        await repo.ExportPatchAsync("", patch);
        await repo.DiscardUnstagedAsync(["file.txt"]);
        Assert.Equal("base\n", Read(repo, "file.txt"));
        await repo.ApplyPatchAsync(patch);
        Assert.Equal("patched\n", Read(repo, "file.txt"));
    }

    [Fact] public async Task ApplyingABrokenPatchChangesNothing() {
        var repo = await Create();
        string patch = Path.Combine(_root, "bad.patch");
        await File.WriteAllTextAsync(patch, "not a patch at all\n");
        await Assert.ThrowsAsync<CommandFailedException>(() => repo.ApplyPatchAsync(patch));
        Assert.Equal("base\n", Read(repo, "file.txt"));
    }

    public void Dispose() {
        if (!Directory.Exists(_root)) return;
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }
}
