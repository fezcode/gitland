using Gitland.Core;
using Xunit;

namespace Gitland.Tests;

/// <summary>Interactive rebase and the operations that were previously out of scope.</summary>
public sealed class AdvancedTests : IDisposable {
    readonly string _root = Path.Combine(Path.GetTempPath(), "gitland-advanced-" + Guid.NewGuid().ToString("N"));
    async Task<GitRepository> Create() {
        var repo = await GitRepository.InitializeAsync(new(Path.Combine(_root, "repo"), "main", false));
        await repo.Git("config", "user.name", "Test"); await repo.Git("config", "user.email", "test@example.invalid"); await repo.Git("config", "core.autocrlf", "false");
        await Write(repo, "file.txt", "base\n"); await Commit(repo, "Base"); return repo;
    }
    static Task Write(GitRepository repo, string path, string text) => File.WriteAllTextAsync(Path.Combine(repo.Root, path), text);
    static async Task<string> Commit(GitRepository repo, string message) { await repo.StageAllAsync(); var state = await repo.ReadManagementAsync(); return await repo.CommitAsync(message, state.IndexTree, state.Head); }
    static async Task<string[]> Subjects(GitRepository repo) =>
        (await repo.ReadHistoryAsync(new(Limit: 50))).Select(c => c.Subject).ToArray();

    // ---- Interactive rebase -------------------------------------------------

    [Fact] public async Task TheRebasePlanListsCommitsOldestFirst() {
        var repo = await Create(); string start = await repo.ResolveRef("HEAD");
        await Write(repo, "a.txt", "a\n"); await Commit(repo, "One");
        await Write(repo, "b.txt", "b\n"); await Commit(repo, "Two");
        var plan = await repo.ReadRebasePlanAsync(start);
        Assert.Equal(["One", "Two"], plan.Select(s => s.Subject));
        Assert.All(plan, s => Assert.Equal("pick", s.Action));
    }

    [Fact] public async Task DroppingACommitRemovesItFromHistory() {
        var repo = await Create(); string start = await repo.ResolveRef("HEAD");
        await Write(repo, "a.txt", "a\n"); await Commit(repo, "Keep me");
        await Write(repo, "b.txt", "b\n"); await Commit(repo, "Drop me");
        var plan = await repo.ReadRebasePlanAsync(start);
        var edited = plan.Select(s => s.Subject == "Drop me" ? s with { Action = "drop" } : s).ToArray();
        var result = await repo.RebaseInteractiveAsync(start, edited, await repo.ResolveRef("HEAD"));
        Assert.True(result.Completed, result.Message);
        Assert.Contains("Keep me", await Subjects(repo));
        Assert.DoesNotContain("Drop me", await Subjects(repo));
        Assert.False(File.Exists(Path.Combine(repo.Root, "b.txt")));
    }

    [Fact] public async Task ReorderingCommitsReplaysThemInTheGivenOrder() {
        var repo = await Create(); string start = await repo.ResolveRef("HEAD");
        await Write(repo, "a.txt", "a\n"); await Commit(repo, "First");
        await Write(repo, "b.txt", "b\n"); await Commit(repo, "Second");
        var plan = (await repo.ReadRebasePlanAsync(start)).Reverse().ToArray();
        Assert.True((await repo.RebaseInteractiveAsync(start, plan, await repo.ResolveRef("HEAD"))).Completed);
        var subjects = await Subjects(repo);
        Assert.Equal("First", subjects[0]);          // newest first in log order
        Assert.Equal("Second", subjects[1]);
    }

    [Fact] public async Task SquashingCombinesTwoCommitsIntoOne() {
        var repo = await Create(); string start = await repo.ResolveRef("HEAD");
        await Write(repo, "a.txt", "a\n"); await Commit(repo, "Feature");
        await Write(repo, "a.txt", "a fixed\n"); await Commit(repo, "Fix typo");
        var plan = await repo.ReadRebasePlanAsync(start);
        var edited = plan.Select(s => s.Subject == "Fix typo" ? s with { Action = "fixup" } : s).ToArray();
        Assert.True((await repo.RebaseInteractiveAsync(start, edited, await repo.ResolveRef("HEAD"))).Completed);
        var subjects = await Subjects(repo);
        Assert.Equal(new[] { "Feature", "Base" }, subjects.AsEnumerable());
        Assert.Equal("a fixed\n", await File.ReadAllTextAsync(Path.Combine(repo.Root, "a.txt")));
    }

    [Fact] public async Task RewordingReplacesTheSubjectWithoutOpeningAnEditor() {
        var repo = await Create(); string start = await repo.ResolveRef("HEAD");
        await Write(repo, "a.txt", "a\n"); await Commit(repo, "Bad name");
        var plan = (await repo.ReadRebasePlanAsync(start)).Select(s => s with { Action = "reword", Message = "Good name" }).ToArray();
        Assert.True((await repo.RebaseInteractiveAsync(start, plan, await repo.ResolveRef("HEAD"))).Completed);
        Assert.Contains("Good name", await Subjects(repo));
        Assert.DoesNotContain("Bad name", await Subjects(repo));
    }

    [Fact] public async Task AnUnusablePlanIsRejectedBeforeAnythingRuns() {
        var repo = await Create(); string start = await repo.ResolveRef("HEAD");
        await Write(repo, "a.txt", "a\n"); await Commit(repo, "One");
        string head = await repo.ResolveRef("HEAD");
        var plan = await repo.ReadRebasePlanAsync(start);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.RebaseInteractiveAsync(start, plan.Select(s => s with { Action = "squash" }).ToArray(), head));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.RebaseInteractiveAsync(start, plan.Select(s => s with { Action = "drop" }).ToArray(), head));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.RebaseInteractiveAsync(start, [plan[0] with { Action = "explode" }], head));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.RebaseInteractiveAsync(start, [plan[0] with { Hash = new string('0', 40) }], head));
        Assert.Equal(head, await repo.ResolveRef("HEAD"));      // nothing moved
    }

    // ---- Bisect -------------------------------------------------------------

    [Fact] public async Task BisectFindsTheCommitThatIntroducedABreak() {
        var repo = await Create();
        string good = await repo.ResolveRef("HEAD");
        await Write(repo, "file.txt", "ok 1\n"); await Commit(repo, "Fine");
        await Write(repo, "file.txt", "BROKEN\n"); string broke = await Commit(repo, "Break it");
        await Write(repo, "file.txt", "BROKEN later\n"); string bad = await Commit(repo, "More");

        Assert.False((await repo.ReadBisectAsync()).Running);
        await repo.StartBisectAsync(bad, good);
        Assert.True((await repo.ReadBisectAsync()).Running);
        string found = "";
        for (int step = 0; step < 10 && found.Length == 0; step++) {
            string text = await File.ReadAllTextAsync(Path.Combine(repo.Root, "file.txt"));
            found = await repo.MarkBisectAsync(text.Contains("BROKEN") ? "bad" : "good");
        }
        Assert.Equal(broke, found);
        await repo.ResetBisectAsync();
        Assert.False((await repo.ReadBisectAsync()).Running);
    }

    [Fact] public async Task BisectRejectsAnUnknownMark() {
        var repo = await Create();
        await Assert.ThrowsAsync<ArgumentException>(() => repo.MarkBisectAsync("maybe"));
    }

    // ---- Submodules, LFS, hooks, archive, signing ---------------------------

    [Fact] public async Task SubmodulesAreListedWithTheirUrlAndInitializedState() {
        var outer = await Create();
        var inner = await GitRepository.InitializeAsync(new(Path.Combine(_root, "inner"), "main", false));
        await inner.Git("config", "user.name", "Test"); await inner.Git("config", "user.email", "test@example.invalid");
        await Write(inner, "inner.txt", "inner\n"); await Commit(inner, "Inner base");
        await outer.Git("-c", "protocol.file.allow=always", "submodule", "add", "--", inner.Root.Replace('\\', '/'), "vendor");
        var modules = await outer.ReadSubmodulesAsync();
        var module = Assert.Single(modules);
        Assert.Equal("vendor", module.Path);
        Assert.True(module.Initialized);
    }

    [Fact] public async Task ReadingSubmodulesOfAPlainRepositoryReturnsNothing() {
        var repo = await Create();
        Assert.Empty(await repo.ReadSubmodulesAsync());
    }

    [Fact] public async Task LfsPatternsAreReadFromGitattributesAndAbsentWhenUnconfigured() {
        var repo = await Create();
        Assert.Empty(await repo.ReadLfsPatternsAsync());
        Assert.False(await repo.HasLfsAsync());
        await Write(repo, ".gitattributes", "*.psd filter=lfs diff=lfs merge=lfs -text\n# comment\n*.txt text\n");
        var pattern = Assert.Single(await repo.ReadLfsPatternsAsync());
        Assert.Equal("*.psd", pattern.Pattern);
    }

    [Fact] public async Task TrackingRejectsAnEmptyPattern() {
        var repo = await Create();
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.TrackLfsAsync("  "));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.UntrackLfsAsync("a\nb"));
    }

    [Fact] public async Task HooksAreListedWithSamplesMarkedDisabled() {
        var repo = await Create();
        var hooks = await repo.ReadHooksAsync();
        Assert.NotEmpty(hooks);
        Assert.All(hooks, h => Assert.False(h.Enabled));      // a fresh repository has samples only
        string folder = Path.GetDirectoryName(hooks[0].Path)!;
        await File.WriteAllTextAsync(Path.Combine(folder, "pre-commit"), "#!/bin/sh\nexit 0\n");
        Assert.Contains(await repo.ReadHooksAsync(), h => h.Name == "pre-commit" && h.Enabled);
    }

    [Fact] public async Task ArchiveWritesAZipOfTheRevision() {
        var repo = await Create();
        string zip = Path.Combine(_root, "export.zip");
        await repo.ArchiveAsync("HEAD", zip);
        Assert.True(new FileInfo(zip).Length > 0);
        using var archive = System.IO.Compression.ZipFile.OpenRead(zip);
        Assert.Contains(archive.Entries, e => e.FullName == "file.txt");
    }

    [Fact] public async Task AnUnsignedCommitReportsNoSignatureRatherThanFailing() {
        var repo = await Create();
        var status = await repo.VerifySignatureAsync("HEAD");
        Assert.Equal("N", status.Code);
        Assert.False(status.Good);
    }

    [Fact] public void AnEnterpriseHostIsAcceptedAndAnInvalidOneIsRejected() {
        Assert.Equal("github.example.com", new GitHubService(host: "GitHub.Example.com").Host);
        Assert.Equal("github.com", new GitHubService().Host);
        Assert.Throws<InvalidOperationException>(() => new GitHubService(host: "not a host"));
        // Remote parsing must follow the configured host, not the public one.
        var service = new GitHubService(host: "github.example.com");
        Assert.Equal("owner/repo", service.RepositoryOnHost("https://github.example.com/owner/repo.git"));
        Assert.Null(service.RepositoryOnHost("https://github.com/owner/repo.git"));
    }

    public void Dispose() {
        if (!Directory.Exists(_root)) return;
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }
}
