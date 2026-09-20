using Gitland.Core;
using Xunit;
using System.Text;

namespace Gitland.Tests;

public class DiffTests {
    [Theory]
    [InlineData("", "")]
    [InlineData("", "new\n")]
    [InlineData("old\n", "")]
    [InlineData("one\ntwo\nthree\n", "one\nchanged\nthree\nfour\n")]
    [InlineData("a\nb\na\nb\n", "b\na\nb\na\n")]
    [InlineData("a\r\nb\r\n", "a\nb\n")]
    public void DiffReconstructsBothSources(string left, string right) {
        var diff = DiffEngine.Compare(left, right);
        Assert.Equal(DiffEngine.Lines(left), diff.Rows.Where(r => r.Left != null).Select(r => r.Left));
        Assert.Equal(DiffEngine.Lines(right), diff.Rows.Where(r => r.Right != null).Select(r => r.Right));
        Assert.All(diff.Rows.Where(r => r.Kind == ChangeKind.Equal), r => Assert.Equal(r.Left, r.Right));
    }
    [Fact] public void RandomDiffsReconstructSourcesAndUseMinimalEdits() {
        var random = new Random(301);
        for (int trial = 0; trial < 250; trial++) {
            var left = Enumerable.Range(0, random.Next(1, 30)).Select(_ => "line " + random.Next(8)).ToArray();
            var right = Enumerable.Range(0, random.Next(1, 30)).Select(_ => "line " + random.Next(8)).ToArray();
            var diff = DiffEngine.Compare(string.Join('\n', left), string.Join('\n', right));
            Assert.Equal(left, diff.Rows.Where(r => r.Left != null).Select(r => r.Left)); Assert.Equal(right, diff.Rows.Where(r => r.Right != null).Select(r => r.Right));
            var lcs = new int[left.Length + 1, right.Length + 1];
            for (int i = 1; i <= left.Length; i++) for (int j = 1; j <= right.Length; j++) lcs[i, j] = left[i - 1] == right[j - 1] ? lcs[i - 1, j - 1] + 1 : Math.Max(lcs[i - 1, j], lcs[i, j - 1]);
            Assert.Equal(left.Length + right.Length - 2 * lcs[left.Length, right.Length], diff.Added + diff.Removed);
        }
    }
    [Fact] public void WhitespaceFilterPreservesOriginalText() {
        var diff = DiffEngine.Compare("  return value;\n", "return  value;\n", true);
        Assert.Equal(ChangeKind.Equal, diff.Rows[0].Kind); Assert.Equal("  return value;", diff.Rows[0].Left);
    }
    [Fact] public void FoldKeepsContextAndEveryChange() {
        var left = string.Join('\n', Enumerable.Range(0, 100).Select(i => $"line {i}"));
        var rows = DiffEngine.Fold(DiffEngine.Compare(left, left.Replace("line 50", "changed")).Rows);
        Assert.Equal(1, rows.Count(r => r.Kind == ChangeKind.Modified)); Assert.Equal(100, rows.Sum(r => r.Kind == ChangeKind.Fold ? r.Hidden : 1));
    }
}

public class MergeTests {
    [Fact] public void Diff3ConflictPreservesEolAndContext() {
        const string text = "before\r\n<<<<<<< HEAD\r\nours\r\n||||||| base\r\nbase\r\n=======\r\ntheirs\r\n>>>>>>> incoming\r\nafter";
        var merge = MergeDocument.Parse(text); Assert.Equal(text, merge.Render()); Assert.Single(merge.Conflicts);
        merge.Conflicts[0].Choice = Resolution.Both; Assert.Equal("before\r\nours\r\ntheirs\r\nafter", merge.Render());
        merge.Conflicts[0].Choice = Resolution.Base; Assert.Equal("before\r\nbase\r\nafter", merge.Render());
    }
    [Fact] public void SupportsCustomConflictMarkerWidthAndMissingFinalNewline() {
        const string text = "<<<<<<<<<< ours\nA\n==========\nB\n>>>>>>>>>> theirs";
        var doc = MergeDocument.Parse(text); Assert.Equal(text, doc.Render()); doc.Conflicts[0].Choice = Resolution.Theirs; Assert.Equal("B\n", doc.Render());
    }
    [Fact] public void RejectsTruncatedMarkers() => Assert.Throws<InvalidOperationException>(() => MergeDocument.Parse("<<<<<<< HEAD\na\n=======\nb\n"));
    [Theory]
    [InlineData("before\n=======\nafter")]
    [InlineData(">>>>>>> incoming\n")]
    [InlineData("||||||| base\n")]
    public void DetectsOrphanedConflictMarkers(string text) => Assert.True(MergeDocument.HasMarkers(text));
}

public sealed class RepositoryTests : IDisposable {
    readonly string _root = Path.Combine(Path.GetTempPath(), "gitland-tests-" + Guid.NewGuid().ToString("N"));
    readonly GitRepository _repo;
    public RepositoryTests() { Directory.CreateDirectory(_root); _repo = new(_root); }
    async Task Init() { await _repo.Git("init", "-b", "main"); await _repo.Git("config", "user.name", "Gitland Test"); await _repo.Git("config", "user.email", "gitland-test@example.invalid"); await _repo.Git("config", "core.autocrlf", "false"); }
    async Task Write(string path, string content) => await File.WriteAllTextAsync(Path.Combine(_root, path), content, new UTF8Encoding(false));
    async Task Commit() { await _repo.Git("add", "."); await _repo.Git("commit", "-m", "Fixture"); }
    [Fact] public async Task StagesOnlyChosenHunkAndLeavesWorkingTreeUntouched() {
        await Init(); string baseline = string.Join('\n', Enumerable.Range(1, 35).Select(i => "line " + i)) + "\n";
        await Write("space [file].txt", baseline); await Commit();
        string changed = baseline.Replace("line 2\n", "changed 2\n").Replace("line 30\n", "changed 30\n"); await Write("space [file].txt", changed);
        var state = await _repo.ReadStateAsync(); var diff = await _repo.WorkingDiffAsync(Assert.Single(state.Changes), false);
        Assert.Equal(2, GitRepository.ParseHunks(diff.Patch).Count);
        await _repo.StageHunkAsync("space [file].txt", diff.Patch, 1);
        string index = await _repo.Git("show", ":space [file].txt"); Assert.Contains("changed 30", index); Assert.DoesNotContain("changed 2", index);
        Assert.Equal(changed, await File.ReadAllTextAsync(Path.Combine(_root, "space [file].txt")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _repo.StageHunkAsync("space [file].txt", diff.Patch, 0));
        await _repo.UnstageFileAsync("space [file].txt"); Assert.Equal(baseline, await _repo.Git("show", ":space [file].txt"));
    }
    [Fact] public async Task HandlesUnbornHeadAndRenames() {
        await Init(); await Write("new.txt", "new\n"); await _repo.StageFileAsync("new.txt");
        var diff = await _repo.WorkingDiffAsync(Assert.Single((await _repo.ReadStateAsync()).Changes), true); Assert.Equal("", diff.Left); Assert.Equal("new\n", diff.Right);
        await _repo.UnstageFileAsync("new.txt"); Assert.Equal('?', Assert.Single((await _repo.ReadStateAsync()).Changes).Index);
        await Commit(); await _repo.Git("mv", "new.txt", "renamed file.txt");
        var renamed = Assert.Single((await _repo.ReadStateAsync()).Changes); Assert.Equal("new.txt", renamed.OldPath);
        var renameDiff = await _repo.WorkingDiffAsync(renamed, true); Assert.Equal(renameDiff.Left, renameDiff.Right);
        await _repo.UnstageFileAsync(renamed.Path, renamed.OldPath);
        Assert.DoesNotContain((await _repo.ReadStateAsync()).Changes, c => c.IsStaged);
    }
    [Fact] public async Task ComparesRevisionsWithoutCheckingOut() {
        await Init(); await Write("file.txt", "before\n"); await Commit(); await _repo.Git("branch", "baseline");
        await Write("file.txt", "after\n"); await Commit(); var changes = await _repo.CompareChangesAsync("baseline", "HEAD");
        var diff = await _repo.CompareFileAsync(Assert.Single(changes), "baseline", "HEAD"); Assert.Equal("before\n", diff.Left); Assert.Equal("after\n", diff.Right); Assert.Equal("main", (await _repo.ReadStateAsync()).Branch);
        await Assert.ThrowsAsync<CommandFailedException>(() => _repo.ResolveRef("--output=bad"));
    }
    [Fact] public async Task MergeSaveMakesBackupPreservesBomAndRejectsStaleEdits() {
        await Init(); await Write("merge.txt", "top\nbase\nbottom\n"); await Commit(); await _repo.Git("checkout", "-b", "incoming");
        await Write("merge.txt", "top\ntheirs\nbottom\n"); await Commit(); await _repo.Git("checkout", "main");
        await Write("merge.txt", "top\nours\nbottom\n"); await Commit();
        await Assert.ThrowsAsync<CommandFailedException>(() => _repo.Git("merge", "incoming"));
        var raw = await File.ReadAllTextAsync(Path.Combine(_root, "merge.txt"));
        await File.WriteAllTextAsync(Path.Combine(_root, "merge.txt"), raw.Replace("\n", "\r\n"), new UTF8Encoding(true));
        var merge = await _repo.ReadMergeAsync("merge.txt"); Assert.Single(merge.Document.Conflicts);
        merge.Document.Conflicts[0].Choice = Resolution.Theirs;
        var backup = await _repo.SaveMergeAsync(merge, merge.Document.Render()); Assert.True(File.Exists(backup));
        var bytes = await File.ReadAllBytesAsync(Path.Combine(_root, "merge.txt")); Assert.True(bytes.AsSpan().StartsWith(new byte[] { 239, 187, 191 }));
        Assert.Equal("top\r\ntheirs\r\nbottom\r\n", await File.ReadAllTextAsync(Path.Combine(_root, "merge.txt")));
        Assert.Contains("<<<<<<<", await File.ReadAllTextAsync(backup));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _repo.SaveMergeAsync(merge, "overwrite"));
        await _repo.StageFileAsync("merge.txt"); Assert.DoesNotContain((await _repo.ReadStateAsync()).Changes, c => c.IsConflict);
    }
    [Fact] public void RejectsPathsOutsideRepository() { Assert.Throws<InvalidOperationException>(() => _repo.ValidatePath("../outside.txt")); Assert.Throws<InvalidOperationException>(() => _repo.ValidatePath(".git/config")); }
    [Fact] public async Task TracksStagedAndUnstagedChangesIndependently() {
        await Init(); await Write("file.txt", "base\n"); await Commit(); await Write("file.txt", "staged\n"); await _repo.StageFileAsync("file.txt"); await Write("file.txt", "working\n");
        var change = Assert.Single((await _repo.ReadStateAsync()).Changes); Assert.True(change.IsStaged && change.IsUnstaged);
        var staged = await _repo.WorkingDiffAsync(change, true); var working = await _repo.WorkingDiffAsync(change, false);
        Assert.Equal("base\n", staged.Left); Assert.Equal("staged\n", staged.Right); Assert.Equal("staged\n", working.Left); Assert.Equal("working\n", working.Right);
    }
    [Fact] public async Task ReadsDeletedFilesWithoutTouchingDisk() {
        await Init(); await Write("gone.txt", "content\n"); await Commit(); File.Delete(Path.Combine(_root, "gone.txt"));
        var change = Assert.Single((await _repo.ReadStateAsync()).Changes); var diff = await _repo.WorkingDiffAsync(change, false);
        Assert.Equal("content\n", diff.Left); Assert.Equal("", diff.Right); await _repo.StageFileAsync("gone.txt");
        var staged = await _repo.WorkingDiffAsync(Assert.Single((await _repo.ReadStateAsync()).Changes), true); Assert.Equal("content\n", staged.Left); Assert.Equal("", staged.Right);
    }
    public void Dispose() { if (Path.GetFullPath(_root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) && Path.GetFileName(_root).StartsWith("gitland-tests-")) { foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal); Directory.Delete(_root, true); } }
}
