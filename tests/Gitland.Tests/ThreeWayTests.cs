using Gitland.Core;
using Xunit;

namespace Gitland.Tests;

public class ThreeWayTests {
    [Theory]
    [InlineData("a\nb\nc\n", "new\na\nb\nc\n", "a\nc\n")]
    [InlineData("a\nb\nc\n", "a\nx\nc\n", "a\ny\nc\n")]
    [InlineData("", "one\ntwo\n", "two\nthree\n")]
    [InlineData("\n", "\n\n", "")]
    [InlineData("a\nb\n", "", "a\nb\nc\n")]
    [InlineData("a\na\nb\na\n", "a\nb\na\na\n", "a\na\nc\na\n")]
    public void AlignmentPreservesAllThreeSourcesAndTheirLineNumbers(string basis, string left, string right) {
        var rows = ThreeWayDiff.Compare(basis, left, right).Rows;
        Assert.Equal(DiffEngine.Lines(basis), rows.Where(r => r.BaseNumber != null).Select(r => r.Base));
        Assert.Equal(DiffEngine.Lines(left), rows.Where(r => r.LeftNumber != null).Select(r => r.Left));
        Assert.Equal(DiffEngine.Lines(right), rows.Where(r => r.RightNumber != null).Select(r => r.Right));
        Assert.Equal(Enumerable.Range(1, DiffEngine.Lines(left).Length), rows.Where(r => r.LeftNumber != null).Select(r => r.LeftNumber!.Value));
        Assert.Equal(Enumerable.Range(1, DiffEngine.Lines(right).Length), rows.Where(r => r.RightNumber != null).Select(r => r.RightNumber!.Value));
    }
    [Fact] public void DistinguishesSharedIndependentAndDivergentEdits() {
        var rows = ThreeWayDiff.Compare("a\nb\nc\nd\n", "same\nb\nleft\nx\n", "same\nright\nc\ny\n").Rows;
        Assert.True(rows[0].Changed); Assert.False(rows[0].Divergent);
        Assert.False(rows[1].LeftChanged); Assert.True(rows[1].RightChanged);
        Assert.True(rows[2].LeftChanged); Assert.False(rows[2].RightChanged);
        Assert.True(rows[3].Divergent);
    }
    [Fact] public void CanIgnoreWhitespaceWithoutLosingOriginalText() {
        var rows = ThreeWayDiff.Compare("a b\n", "a   b\n", " a b \n", true).Rows;
        Assert.All(rows, r => Assert.False(r.Changed)); Assert.Equal("a   b", rows[0].Left);
    }
    [Fact] public void RandomEditsNeverLoseOrDuplicateLines() {
        var random = new Random(9182);
        string RandomText() => string.Join('\n', Enumerable.Range(0, random.Next(25)).Select(_ => random.Next(8).ToString())) + "\n";
        for (int i = 0; i < 100; i++) AlignmentPreservesAllThreeSourcesAndTheirLineNumbers(RandomText(), RandomText(), RandomText());
    }
    [Fact] public void DivergentNavigationHonorsWhitespaceForChangedAndInsertedLines() {
        Assert.All(ThreeWayDiff.Compare("old\n", "new value\nadded value\n", "new  value\nadded  value\n", true).Rows, r => Assert.False(r.Divergent));
        Assert.Contains(ThreeWayDiff.Compare("old\n", "new value\n", "new  value\n").Rows, r => r.Divergent);
    }
}
