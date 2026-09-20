namespace Gitland.Core;

public enum ChangeKind { Equal, Added, Removed, Modified, Fold }
public sealed record DiffRow(int? LeftNumber, string? Left, int? RightNumber, string? Right, ChangeKind Kind, int Hidden = 0);
public sealed record DiffResult(IReadOnlyList<DiffRow> Rows, int Added, int Removed) {
    public IReadOnlyList<int> ChangeStarts => Rows.Select((r, i) => (r, i))
        .Where(x => x.r.Kind != ChangeKind.Equal && (x.i == 0 || Rows[x.i - 1].Kind == ChangeKind.Equal)).Select(x => x.i).ToArray();
}

public static class DiffEngine {
    public static string[] Lines(string text) => text.Length == 0 ? [] : text.Replace("\r\n", "\n").TrimEndOneNewline().Split('\n');
    private static string TrimEndOneNewline(this string text) => text.EndsWith('\n') ? text[..^1] : text;

    // Myers' shortest edit script. The bounded frontier avoids unbounded memory on unrelated generated files.
    public static DiffResult Compare(string left, string right, bool ignoreWhitespace = false) {
        var a = Lines(left); var b = Lines(right);
        string Key(string s) => ignoreWhitespace ? string.Concat(s.Where(c => !char.IsWhiteSpace(c))) : s;
        var ka = a.Select(Key).ToArray(); var kb = b.Select(Key).ToArray();
        var edits = new List<(ChangeKind Kind, int A, int B)>();
        int prefix = 0;
        while (prefix < a.Length && prefix < b.Length && ka[prefix] == kb[prefix]) { edits.Add((ChangeKind.Equal, prefix, prefix)); prefix++; }
        int suffix = 0;
        while (suffix < a.Length - prefix && suffix < b.Length - prefix && ka[a.Length - suffix - 1] == kb[b.Length - suffix - 1]) suffix++;
        int n = a.Length - prefix - suffix, m = b.Length - prefix - suffix;
        var trace = new List<Dictionary<int, int>>(); var v = new Dictionary<int, int> { [1] = 0 };
        bool found = false;
        for (int d = 0; d <= Math.Min(n + m, 1400); d++) {
            trace.Add(new(v));
            for (int k = -d; k <= d; k += 2) {
                int x = k == -d || (k != d && v.GetValueOrDefault(k - 1, -1) < v.GetValueOrDefault(k + 1, -1)) ? v.GetValueOrDefault(k + 1) : v.GetValueOrDefault(k - 1) + 1;
                int y = x - k;
                while (x < n && y < m && ka[prefix + x] == kb[prefix + y]) { x++; y++; }
                v[k] = x;
                if (x >= n && y >= m) { found = true; break; }
            }
            if (found) break;
        }
        var middle = new List<(ChangeKind Kind, int A, int B)>();
        if (found) {
            int x = n, y = m;
            for (int d = trace.Count - 1; d >= 0; d--) {
                var prev = trace[d]; int k = x - y;
                int pk = k == -d || (k != d && prev.GetValueOrDefault(k - 1, -1) < prev.GetValueOrDefault(k + 1, -1)) ? k + 1 : k - 1;
                int px = prev.GetValueOrDefault(pk), py = px - pk;
                while (x > px && y > py) { x--; y--; middle.Add((ChangeKind.Equal, prefix + x, prefix + y)); }
                if (d == 0) break;
                if (x == px) { y--; middle.Add((ChangeKind.Added, -1, prefix + y)); }
                else { x--; middle.Add((ChangeKind.Removed, prefix + x, -1)); }
            }
            middle.Reverse();
        } else {
            for (int i = 0; i < n; i++) middle.Add((ChangeKind.Removed, prefix + i, -1));
            for (int i = 0; i < m; i++) middle.Add((ChangeKind.Added, -1, prefix + i));
        }
        edits.AddRange(middle);
        for (int i = suffix; i > 0; i--) edits.Add((ChangeKind.Equal, a.Length - i, b.Length - i));
        var rows = new List<DiffRow>(); int added = 0, removed = 0;
        for (int i = 0; i < edits.Count;) {
            var e = edits[i];
            if (e.Kind == ChangeKind.Equal) { rows.Add(new(e.A + 1, a[e.A], e.B + 1, b[e.B], ChangeKind.Equal)); i++; continue; }
            var deletes = new List<int>(); var inserts = new List<int>();
            while (i < edits.Count && edits[i].Kind != ChangeKind.Equal) {
                e = edits[i++]; if (e.Kind == ChangeKind.Removed) deletes.Add(e.A); else inserts.Add(e.B);
            }
            added += inserts.Count; removed += deletes.Count;
            for (int j = 0; j < Math.Max(deletes.Count, inserts.Count); j++) {
                int? ai = j < deletes.Count ? deletes[j] : null, bi = j < inserts.Count ? inserts[j] : null;
                rows.Add(new(ai + 1, ai is int av ? a[av] : null, bi + 1, bi is int bv ? b[bv] : null,
                    ai != null && bi != null ? ChangeKind.Modified : ai != null ? ChangeKind.Removed : ChangeKind.Added));
            }
        }
        return new(rows, added, removed);
    }

    public static IReadOnlyList<DiffRow> Fold(IReadOnlyList<DiffRow> rows, int context = 3) {
        var result = new List<DiffRow>();
        for (int i = 0; i < rows.Count;) {
            if (rows[i].Kind != ChangeKind.Equal) { result.Add(rows[i++]); continue; }
            int start = i; while (i < rows.Count && rows[i].Kind == ChangeKind.Equal) i++;
            int head = start == 0 ? 0 : context, tail = i == rows.Count ? 0 : context;
            if (i - start <= head + tail + 2) result.AddRange(rows.Skip(start).Take(i - start));
            else {
                result.AddRange(rows.Skip(start).Take(head));
                result.Add(new(null, null, null, null, ChangeKind.Fold, i - start - head - tail));
                result.AddRange(rows.Skip(i - tail).Take(tail));
            }
        }
        return result;
    }

    public static (int Start, int Length) ChangedSpan(string text, string other) {
        int prefix = 0; while (prefix < Math.Min(text.Length, other.Length) && text[prefix] == other[prefix]) prefix++;
        int suffix = 0; while (suffix < Math.Min(text.Length, other.Length) - prefix && text[^(suffix + 1)] == other[^(suffix + 1)]) suffix++;
        return (prefix, text.Length - prefix - suffix);
    }
}
