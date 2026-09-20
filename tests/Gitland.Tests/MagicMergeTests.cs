using Gitland.Core;
using Xunit;

namespace Gitland.Tests;

public class MagicMergeTests {
    const string Independent = "<<<<<<< ours\nconst x = { a: 2, b: 1 };\n||||||| base\nconst x = { a: 1, b: 1 };\n=======\nconst x = { a: 1, b: 2 };\n>>>>>>> theirs\n";
    const string Overlap = "<<<<<<< ours\nvalue = 2;\n||||||| base\nvalue = 1;\n=======\nvalue = 3;\n>>>>>>> theirs\n";
    [Fact] public void PreviewDoesNotChangeOriginalAndApplyPreservesManualSurroundings() {
        var original = MergeDocument.Parse(Independent + Overlap);
        string edited = "// hand edited header\n" + Independent + "// keep this too\n" + Overlap;
        var plan = SmartMerge.Prepare(edited, original);
        var suggestion = Assert.Single(plan.Suggestions); Assert.Contains("independent", suggestion.Suggestion!.Reason);
        Assert.Equal(Independent + Overlap, original.Render()); Assert.Equal(edited, plan.Document.Render());
        string result = plan.Apply([suggestion.Id]);
        Assert.StartsWith("// hand edited header\nconst x = { a: 2, b: 2 };\n// keep this too\n", result);
        Assert.EndsWith(Overlap, result); Assert.Equal(1, plan.Document.Unresolved);
    }
    [Fact] public void AppliesOnlySelectedSuggestions() {
        var source = Independent + Independent;
        var plan = SmartMerge.Prepare(source, MergeDocument.Parse(source));
        Assert.Equal(2, plan.Suggestions.Count);
        Assert.Equal(Independent + "const x = { a: 2, b: 2 };\n", plan.Apply([2]));
    }
    [Fact] public void NoSelectionKeepsExactTextIncludingCrLf() {
        string source = ("header\n" + Independent + "footer\n").Replace("\n", "\r\n");
        var plan = SmartMerge.Prepare(source, MergeDocument.Parse(source));
        Assert.Equal(source, plan.Apply([]));
        Assert.Equal("header\r\nconst x = { a: 2, b: 2 };\r\nfooter\r\n", plan.Apply([1]));
    }
    [Fact] public void ReusesBaseHintsOnlyForUnchangedConflictAlternatives() {
        var hints = MergeDocument.Parse(Independent);
        string twoWay = Independent.Replace("||||||| base\nconst x = { a: 1, b: 1 };\n", "");
        Assert.Single(SmartMerge.Prepare("// manual\n" + twoWay, hints).Suggestions);
        Assert.Empty(SmartMerge.Prepare(twoWay.Replace("a: 2", "a: 4"), hints).Suggestions);
    }
    [Fact] public void RejectsIncompleteMarkersAndUnsupportedSelectionsWithoutChangingSource() {
        var original = MergeDocument.Parse(Overlap);
        var plan = SmartMerge.Prepare(Overlap, original);
        Assert.Empty(plan.Suggestions);
        Assert.Throws<InvalidOperationException>(() => plan.Apply([1]));
        Assert.Equal(Overlap, plan.Document.Render());
        Assert.Throws<InvalidOperationException>(() => SmartMerge.Prepare("<<<<<<< ours\na\n=======\nb\n", original));
    }
}
