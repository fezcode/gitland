namespace Gitland.Core;

public enum MergeLineKind { Ours, Base, Theirs, Marker, Resolved }
public readonly record struct MergeLineHighlight(int Start, int Length, MergeLineKind Kind);

/// <summary>Character ranges for display only; never rewrites merge source.</summary>
public static class MergeHighlighting {
    public static IReadOnlyList<MergeLineHighlight> Source(string ancestor, string source, MergeLineKind kind) {
        var lines = Lines(source).ToArray();
        var changed = DiffEngine.Compare(ancestor, source).Rows
            .Where(row => row.Kind is ChangeKind.Added or ChangeKind.Modified && row.RightNumber.HasValue)
            .Select(row => row.RightNumber!.Value - 1).ToHashSet();
        return lines.Where((_, index) => changed.Contains(index)).Select(line => new MergeLineHighlight(line.Start, line.Length, kind)).ToArray();
    }

    public static IReadOnlyList<MergeLineHighlight> Result(string text, MergeDocument? generated = null) {
        var highlights = new List<MergeLineHighlight>();
        MergeLineKind? side = null;
        int width = 0;
        foreach (var line in Lines(text)) {
            var value = text.AsSpan(line.Start, line.Length).TrimEnd("\r\n");
            int markerWidth = Run(value, '<');
            if (markerWidth >= 7 && LabelAfter(value, markerWidth)) {
                width = markerWidth; side = MergeLineKind.Ours;
                highlights.Add(new(line.Start, line.Length, MergeLineKind.Marker));
            } else if (side != null && Run(value, '|') == width && LabelAfter(value, width)) {
                side = MergeLineKind.Base; highlights.Add(new(line.Start, line.Length, MergeLineKind.Marker));
            } else if (side != null && Run(value, '=') == width && value.Length == width) {
                side = MergeLineKind.Theirs; highlights.Add(new(line.Start, line.Length, MergeLineKind.Marker));
            } else if (side != null && Run(value, '>') == width && LabelAfter(value, width)) {
                highlights.Add(new(line.Start, line.Length, MergeLineKind.Marker)); side = null; width = 0;
            } else if (side is { } kind) highlights.Add(new(line.Start, line.Length, kind));
        }
        // Generated choices have reliable offsets. Manual edits invalidate those offsets.
        if (generated != null && generated.Render() == text) {
            foreach (var block in generated.Conflicts.Where(block => block.Choice != Resolution.Unresolved)) {
                var range = generated.Range(block.Id);
                if (range.Length > 0) highlights.Add(new(range.Start, range.Length, MergeLineKind.Resolved));
            }
        }
        return highlights.OrderBy(h => h.Start).ToArray();
    }

    static int Run(ReadOnlySpan<char> text, char marker) { int count = 0; while (count < text.Length && text[count] == marker) count++; return count; }
    static bool LabelAfter(ReadOnlySpan<char> text, int width) => text.Length == width || text[width] == ' ';
    static IEnumerable<(int Start, int Length)> Lines(string text) {
        for (int start = 0; start < text.Length;) {
            int newline = text.IndexOf('\n', start);
            int end = newline < 0 ? text.Length : newline + 1;
            yield return (start, end - start); start = end;
        }
    }
}
