using Gitland.Core;
using Xunit;

namespace Gitland.Tests;

/// <summary>Permanent removal must leave nothing behind — no recovery ref and no backup copy.</summary>
public sealed class PermanentDiscardTests : IDisposable {
    readonly string _root = Path.Combine(Path.GetTempPath(), "gitland-permanent-" + Guid.NewGuid().ToString("N"));
    async Task<GitRepository> Create() {
        var repo = await GitRepository.InitializeAsync(new(Path.Combine(_root, "repo"), "main", false));
        await repo.Git("config", "user.name", "Test"); await repo.Git("config", "user.email", "test@example.invalid"); await repo.Git("config", "core.autocrlf", "false");
        await Write(repo, "file.txt", "base\n"); await Commit(repo, "Base"); return repo;
    }
    static Task Write(GitRepository repo, string path, string text) => File.WriteAllTextAsync(Path.Combine(repo.Root, path), text);
    static string Read(GitRepository repo, string path) => File.ReadAllText(Path.Combine(repo.Root, path));
    static async Task<string> Commit(GitRepository repo, string message) { await repo.StageAllAsync(); var state = await repo.ReadManagementAsync(); return await repo.CommitAsync(message, state.IndexTree, state.Head); }
    static async Task<int> RecoveryCount(GitRepository repo) => (await repo.ReadToolsAsync()).Recovery.Count;
    static bool AnyBackup(GitRepository repo) {
        string folder = Path.Combine(repo.Root, ".git", "gitland-backups");
        return Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any();
    }

    [Fact] public async Task DiscardingUnstagedPermanentlyLeavesNoRecoveryRef() {
        var repo = await Create();
        await Write(repo, "file.txt", "gone\n");
        var result = await repo.DiscardUnstagedAsync(["file.txt"], keepRecovery: false);
        Assert.Equal("base\n", Read(repo, "file.txt"));
        Assert.Equal("", result.RecoveryRef);
        Assert.Equal(0, await RecoveryCount(repo));
    }

    [Fact] public async Task TheSameDiscardKeepingRecoveryDoesLeaveOne() {
        var repo = await Create();
        await Write(repo, "file.txt", "kept\n");
        var result = await repo.DiscardUnstagedAsync(["file.txt"]);
        Assert.NotEqual("", result.RecoveryRef);
        Assert.Equal(1, await RecoveryCount(repo));
        Assert.Equal("kept\n", await repo.Git("show", result.RecoveryRef + ":file.txt"));
    }

    [Fact] public async Task DeletingAnUntrackedFilePermanentlyCopiesItNowhere() {
        var repo = await Create();
        await Write(repo, "scratch.txt", "gone\n");
        var result = await repo.CleanUntrackedAsync(["scratch.txt"], keepRecovery: false);
        Assert.False(File.Exists(Path.Combine(repo.Root, "scratch.txt")));
        Assert.Equal("", result.BackupDirectory);
        Assert.False(AnyBackup(repo));
    }

    [Fact] public async Task DiscardingAFilePermanentlyLeavesNothingBehind() {
        var repo = await Create();
        await Write(repo, "file.txt", "staged\n"); await repo.StageFileAsync("file.txt");
        await Write(repo, "file.txt", "unstaged\n");
        var result = await repo.DiscardFileAsync(["file.txt"], keepRecovery: false);
        Assert.Equal("base\n", Read(repo, "file.txt"));
        Assert.Equal("", result.RecoveryRef);
        Assert.Equal(0, await RecoveryCount(repo));
    }

    [Fact] public async Task DiscardingAHunkPermanentlyLeavesNoRecoveryRef() {
        var repo = await Create();
        await Write(repo, "file.txt", string.Join('\n', Enumerable.Range(0, 40).Select(i => "line " + i)) + "\n");
        await Commit(repo, "Long");
        var lines = Enumerable.Range(0, 40).Select(i => "line " + i).ToArray();
        lines[2] = "FIRST"; lines[36] = "SECOND";
        await Write(repo, "file.txt", string.Join('\n', lines) + "\n");
        string patch = await repo.ReadPatchAsync("file.txt", false);
        var result = await repo.DiscardHunkAsync("file.txt", patch, 0, keepRecovery: false);
        Assert.DoesNotContain("FIRST", Read(repo, "file.txt"));
        Assert.Contains("SECOND", Read(repo, "file.txt"));
        Assert.Equal("", result.RecoveryRef);
        Assert.Equal(0, await RecoveryCount(repo));
    }

    [Fact] public async Task DiscardingEverythingPermanentlyKeepsNoSnapshotAndNoCopies() {
        var repo = await Create();
        await Write(repo, "file.txt", "changed\n"); await Write(repo, "scratch.txt", "gone\n");
        var result = await repo.DiscardEverythingAsync(true, keepRecovery: false);
        Assert.Equal("base\n", Read(repo, "file.txt"));
        Assert.False(File.Exists(Path.Combine(repo.Root, "scratch.txt")));
        Assert.Equal("", result.RecoveryRef);
        Assert.Equal("", result.BackupDirectory);
        Assert.Equal(0, await RecoveryCount(repo));
        Assert.False(AnyBackup(repo));
    }

    public void Dispose() {
        if (!Directory.Exists(_root)) return;
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }
}

public sealed class RecentRepositoryTests {
    [Fact] public void AnOpenedRepositoryGoesToTheFrontWithoutDuplicating() {
        var settings = new UserSettings()
            .WithRecent(@"C:\a").WithRecent(@"C:\b").WithRecent(@"C:\a");
        Assert.Equal([@"C:\a", @"C:\b"], settings.RecentRepositories);
    }

    [Fact] public void TheSameFolderInAnotherCaseOrWithATrailingSlashIsNotASecondEntry() {
        var settings = new UserSettings().WithRecent(@"C:\Projects\Repo").WithRecent(@"c:\projects\repo\");
        Assert.Single(settings.RecentRepositories!);
    }

    [Fact] public void TheListIsCappedAtTheMostRecentEight() {
        var settings = new UserSettings();
        for (int i = 0; i < 12; i++) settings = settings.WithRecent(@"C:\repo" + i);
        Assert.Equal(UserSettings.MaxRecent, settings.RecentRepositories!.Count);
        Assert.Equal(@"C:\repo11", settings.RecentRepositories[0]);
        Assert.DoesNotContain(@"C:\repo0", settings.RecentRepositories);
    }

    [Fact] public void ARepositoryCanBeRemovedFromTheList() {
        var settings = new UserSettings().WithRecent(@"C:\a").WithRecent(@"C:\b").WithoutRecent(@"c:\A");
        Assert.Equal([@"C:\b"], settings.RecentRepositories);
    }

    [Fact] public void TheWorkspaceRootKeepsItsPathButLosesATrailingSlash() =>
        Assert.Equal(@"D:\Workhammer", new UserSettings(WorkspaceRoot: @"D:\Workhammer\").Normalize().WorkspaceRoot);

    /// <summary>A blank root is the same as no root, so the Workspace view asks for one rather than
    /// scanning the process working directory.</summary>
    [Fact] public void ABlankWorkspaceRootNormalizesToNoRootAtAll() {
        Assert.Null(new UserSettings(WorkspaceRoot: "   ").Normalize().WorkspaceRoot);
        Assert.Null(new UserSettings().Normalize().WorkspaceRoot);
    }

    [Fact] public void ARepairedSettingsFileDropsBlanksAndDuplicates() {
        var settings = new UserSettings(RecentRepositories: ["", "  ", @"C:\a", @"C:\a\", @"C:\b"]).Normalize();
        Assert.Equal([@"C:\a", @"C:\b"], settings.RecentRepositories);
    }

    [Fact] public void AbsentRecentsNormalizeToAnEmptyListRatherThanNull() =>
        Assert.Empty(new UserSettings().Normalize().RecentRepositories!);

    [Fact] public void RecentRepositoriesEqualityIsByContentsNotByListIdentity() {
        // Two separately built lists with the same paths: reference equality would call these different,
        // and a saved settings file would never compare equal to the one loaded back.
        var left = new UserSettings(RecentRepositories: new List<string> { @"C:\a", @"C:\b" });
        var right = new UserSettings(RecentRepositories: new List<string> { @"C:\a", @"C:\b" });
        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
        Assert.NotEqual(left, new UserSettings(RecentRepositories: new List<string> { @"C:\b", @"C:\a" }));
        Assert.NotEqual(left, new UserSettings(RecentRepositories: new List<string> { @"C:\a" }));
        Assert.Equal(new UserSettings(), new UserSettings(RecentRepositories: new List<string>()));
    }

    /// <summary>UserSettings writes its own Equals, so a property added to the record without being
    /// added there would silently stop counting. Every property must change equality on its own.</summary>
    [Fact] public void EveryUserSettingsPropertyTakesPartInEquality() {
        var baseline = new UserSettings();
        var changed = new UserSettings[] {
            baseline with { Theme = "paper" },
            baseline with { CodeSize = 17 },
            baseline with { HoswlEnabled = true },
            baseline with { DefaultUnified = true },
            baseline with { SyncMergeScroll = false },
            baseline with { InterfaceFont = "inter" },
            baseline with { CodeFont = "consolas" },
            baseline with { SidebarWidth = 300 },
            baseline with { RecentRepositories = new List<string> { @"C:\a" } },
            baseline with { WorkspaceRoot = @"D:\Workhammer" },
        };
        // One entry per settable property; if the record grows, this count fails first.
        Assert.Equal(typeof(UserSettings).GetProperties().Count(p => p.CanWrite), changed.Length);
        Assert.All(changed, variant => Assert.NotEqual(baseline, variant));
    }
}
