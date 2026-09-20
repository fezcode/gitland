using Gitland.Core;
using Xunit;

namespace Gitland.Tests;

public class MergeHighlightingTests {
    [Theory]
    [InlineData("\n", 7)]
    [InlineData("\r\n", 10)]
    public void Diff3SectionsHaveDifferentColorsAndExactOffsets(string eol, int width) {
        var lines = new[] { "untouched", new string('<', width) + " ours", "ours", new string('|', width) + " base", "base", new string('=', width), "theirs", new string('>', width) + " incoming", "after" };
        var text = string.Join(eol, lines);
        var highlights = MergeHighlighting.Result(text);
        Assert.Equal(new[] { MergeLineKind.Marker, MergeLineKind.Ours, MergeLineKind.Marker, MergeLineKind.Base, MergeLineKind.Marker, MergeLineKind.Theirs, MergeLineKind.Marker }, highlights.Select(h => h.Kind));
        Assert.Equal(lines.Skip(1).Take(7), highlights.Select(h => text.Substring(h.Start, h.Length).TrimEnd('\r', '\n')));
    }
    [Fact] public void IncompleteManualConflictsStayColoredWithoutThrowing() {
        var text = "<<<<<<< ours\nours\n=======\nincoming";
        Assert.Equal(new[] { MergeLineKind.Marker, MergeLineKind.Ours, MergeLineKind.Marker, MergeLineKind.Theirs }, MergeHighlighting.Result(text).Select(h => h.Kind));
    }
    [Fact] public void OrdinaryOperatorsAndSeparatorsAreNotConflicts() {
        Assert.Empty(MergeHighlighting.Result("a === b;\n=======\n|||||||\n>>>>>>>> not an open conflict\n"));
    }
    [Fact] public void SourceHighlightsOnlyChangedLinesAgainstBase() {
        const string source = "same\r\nchanged\r\ninserted\r\nlast\r\n";
        var highlights = MergeHighlighting.Source("same\r\nold\r\nlast\r\n", source, MergeLineKind.Theirs);
        Assert.Equal(new[] { "changed\r\n", "inserted\r\n" }, highlights.Select(h => source.Substring(h.Start, h.Length)));
        Assert.All(highlights, h => Assert.Equal(MergeLineKind.Theirs, h.Kind));
    }
    [Fact] public void AcceptedResultsAreGreenAndManualOffsetsAreNotReused() {
        var doc = MergeDocument.Parse("prefix\n<<<<<<< ours\nchosen\n=======\nother\n>>>>>>> theirs\nsuffix\n");
        doc.Conflicts[0].Choice = Resolution.Ours;
        string rendered = doc.Render();
        var highlight = Assert.Single(MergeHighlighting.Result(rendered, doc));
        Assert.Equal(MergeLineKind.Resolved, highlight.Kind);
        Assert.Equal("chosen\n", rendered.Substring(highlight.Start, highlight.Length));
        Assert.Empty(MergeHighlighting.Result("manual prefix\n" + rendered, doc));
    }
    [Fact] public void SeparatorWidthMustMatchItsConflict() {
        var highlights = MergeHighlighting.Result("<<<<<<<<<< ours\n=======\n==========\ntheirs\n>>>>>>>>>> theirs\n");
        Assert.Equal(MergeLineKind.Ours, highlights[1].Kind);
        Assert.Equal(MergeLineKind.Marker, highlights[2].Kind);
        Assert.Equal(MergeLineKind.Theirs, highlights[3].Kind);
    }
    [Fact] public void EmptySourcesAndResultsHaveNoHighlights() {
        Assert.Empty(MergeHighlighting.Source("removed\n", "", MergeLineKind.Ours));
        Assert.Empty(MergeHighlighting.Result(""));
    }
}
