using System.Text;
using System.Text.RegularExpressions;

namespace Gitland.Core;

public sealed record SmartSuggestion(string Text, string Reason);
public sealed record MagicMergePlan(string Source, MergeDocument Document) {
    public IReadOnlyList<ConflictBlock> Suggestions => Document.Conflicts.Where(c => c.Suggestion != null).ToArray();
    public string Apply(IEnumerable<int> selected) {
        var ids = selected.ToHashSet();
        if (ids.Any(id => !Suggestions.Any(c => c.Id == id))) throw new InvalidOperationException("A selected conflict has no independent resolution.");
        foreach (var block in Document.Conflicts) block.Choice = ids.Contains(block.Id) ? Resolution.Smart : Resolution.Unresolved;
        return Document.Render();
    }
}

public static class SmartMerge {
    public static MagicMergePlan Prepare(string currentResult, MergeDocument original) {
        var document = MergeDocument.Parse(currentResult);
        document.AddBaseHints(original);
        Analyze(document);
        return new(currentResult, document);
    }
    sealed record Edit(int Start, int End, string Replacement);
    static readonly Regex Tokens = new("\"(?:\\\\.|[^\"\\\\])*\"|'(?:\\\\.|[^'\\\\])*'|[\\p{L}_][\\p{L}\\p{N}_]*|\\d+(?:\\.\\d+)?|\\r\\n|\\n|[^\\S\\r\\n]+|.", RegexOptions.Compiled);
    public static void Analyze(MergeDocument document) {
        foreach (var block in document.Conflicts) block.Suggestion = Suggest(block.Base, block.Ours, block.Theirs);
    }
    public static SmartSuggestion? Suggest(string? ancestor, string ours, string theirs) {
        if (ours == theirs) return new(ours, "Both sides made the same change.");
        if (ancestor == null) return null;
        if (ours == ancestor) return new(theirs, "Only the incoming side changed this block.");
        if (theirs == ancestor) return new(ours, "Only the current side changed this block.");
        // Bound work; ambiguous or very large conflicts remain a human decision.
        if (ancestor.Length + ours.Length + theirs.Length > 60000) return null;
        var basis = Tokens.Matches(ancestor).Select(m => m.Value).ToArray();
        var left = Tokens.Matches(ours).Select(m => m.Value).ToArray(); var right = Tokens.Matches(theirs).Select(m => m.Value).ToArray();
        if (basis.Length + left.Length + right.Length > 12000) return null;
        var edits = Changes(basis, left); var additions = Changes(basis, right);
        foreach (var incoming in additions) {
            if (edits.Any(e => e == incoming)) continue;
            if (edits.Any(e => Overlaps(e, incoming))) return null;
            edits.Add(incoming);
        }
        var result = new StringBuilder(); int at = 0;
        foreach (var edit in edits.OrderBy(e => e.Start).ThenBy(e => e.End)) {
            for (; at < edit.Start; at++) result.Append(basis[at]);
            result.Append(edit.Replacement); at = edit.End;
        }
        for (; at < basis.Length; at++) result.Append(basis[at]);
        string text = result.ToString();
        if (MergeDocument.HasMarkers(text)) return null;
        return new(text, "Combined independent edits against the common ancestor. Review the result before saving.");
    }
    static bool Overlaps(Edit a, Edit b) {
        if (a.Start == a.End) return a.Start >= b.Start && a.Start <= b.End;
        if (b.Start == b.End) return b.Start >= a.Start && b.Start <= a.End;
        return a.Start < b.End && b.Start < a.End;
    }
    static List<Edit> Changes(string[] basis, string[] changed) {
        // Encode tokens as lines so the tested Myers engine can compare a token sequence losslessly.
        static string Encode(string[] tokens) => string.Join('\n', tokens.Select(s => Convert.ToBase64String(Encoding.UTF8.GetBytes(s))));
        var rows = DiffEngine.Compare(Encode(basis), Encode(changed)).Rows;
        var edits = new List<Edit>(); int cursor = 0, start = -1; var replacement = new StringBuilder();
        void Flush() { if (start < 0) return; edits.Add(new(start, cursor, replacement.ToString())); start = -1; replacement.Clear(); }
        foreach (var row in rows) {
            if (row.Kind == ChangeKind.Equal) { Flush(); cursor++; continue; }
            if (start < 0) start = cursor;
            if (row.LeftNumber != null) cursor++;
            if (row.RightNumber is int number) replacement.Append(changed[number - 1]);
        }
        Flush(); return edits;
    }
}
