namespace Gitland.Core;

public sealed record ThreeWayRow(int? LeftNumber, string? Left, int? BaseNumber, string? Base, int? RightNumber, string? Right, bool LeftChanged, bool RightChanged, int Hidden = 0, bool IgnoreWhitespace = false) {
    public bool Changed => LeftChanged || RightChanged;
    public bool Divergent => LeftChanged && RightChanged && Left != Right && !(IgnoreWhitespace && Left != null && Right != null && Left.Where(c => !char.IsWhiteSpace(c)).SequenceEqual(Right.Where(c => !char.IsWhiteSpace(c))));
}
public sealed record ThreeWayResult(IReadOnlyList<ThreeWayRow> Rows) {
    public IReadOnlyList<int> ChangeStarts => Rows.Select((r, i) => (r, i)).Where(x => x.r.Changed && (x.i == 0 || !Rows[x.i - 1].Changed)).Select(x => x.i).ToArray();
}
public sealed record ThreeWayRevisions(string Base, string Left, string Right, IReadOnlyList<GitChange> Files);
public sealed record ThreeWayFile(string Path, string Base, string Left, string Right, bool BaseExists = true, bool LeftExists = true, bool RightExists = true);

public static class ThreeWayDiff {
    // Anchor both edit scripts to the same ancestor. Insertions occupy their own slots,
    // so a deleted line on one side cannot shift all subsequent lines on the other.
    public static ThreeWayResult Compare(string ancestor, string left, string right, bool ignoreWhitespace = false) {
        var basis = DiffEngine.Lines(ancestor);
        (Dictionary<int, DiffRow> Lines, Dictionary<int, List<DiffRow>> Inserts) Map(string text) {
            var lines = new Dictionary<int, DiffRow>(); var inserts = new Dictionary<int, List<DiffRow>>(); int slot = 0;
            foreach (var row in DiffEngine.Compare(ancestor, text, ignoreWhitespace).Rows) {
                if (row.LeftNumber is int n) { lines[n] = row; slot = n; }
                else { if (!inserts.TryGetValue(slot, out var list)) inserts[slot] = list = []; list.Add(row); }
            }
            return (lines, inserts);
        }
        var a = Map(left); var b = Map(right); var result = new List<ThreeWayRow>();
        for (int slot = 0; slot <= basis.Length; slot++) {
            var ai = a.Inserts.GetValueOrDefault(slot) ?? []; var bi = b.Inserts.GetValueOrDefault(slot) ?? [];
            // Align simultaneous inserted blocks to each other, including equal inserted lines.
            if (ai.Count > 0 || bi.Count > 0) foreach (var insertion in DiffEngine.Compare(string.Join('\n', ai.Select(r => r.Right)) + (ai.Count > 0 ? "\n" : ""), string.Join('\n', bi.Select(r => r.Right)) + (bi.Count > 0 ? "\n" : ""), ignoreWhitespace).Rows) {
                var ar = insertion.LeftNumber is int an ? ai[an - 1] : null; var br = insertion.RightNumber is int bn ? bi[bn - 1] : null;
                result.Add(new(ar?.RightNumber, ar?.Right, null, null, br?.RightNumber, br?.Right, ar != null, br != null, IgnoreWhitespace: ignoreWhitespace));
            }
            if (slot == basis.Length) break;
            var l = a.Lines[slot + 1]; var r = b.Lines[slot + 1];
            result.Add(new(l.RightNumber, l.Right, slot + 1, basis[slot], r.RightNumber, r.Right, l.Kind != ChangeKind.Equal, r.Kind != ChangeKind.Equal, IgnoreWhitespace: ignoreWhitespace));
        }
        return new(result);
    }
}

public sealed partial class GitRepository {
    public async Task<ThreeWayRevisions> ThreeWayRevisionsAsync(string left, string right, string? ancestor = null) {
        string a = await ResolveRef(left), b = await ResolveRef(right);
        string basis = string.IsNullOrWhiteSpace(ancestor) ? (await Git("merge-base", a, b)).Trim() : await ResolveRef(ancestor);
        if (basis.Length == 0) throw new InvalidOperationException("These revisions have no common ancestor. Choose a base revision explicitly.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (string target in new[] { a, b }) foreach (string path in (await Git("diff", "--name-only", "--no-renames", "-z", basis, target, "--")).Split('\0', StringSplitOptions.RemoveEmptyEntries)) names.Add(path);
        return new(basis, a, b, names.Order(StringComparer.Ordinal).Select(p => new GitChange(p, null, 'M', ' ')).ToArray());
    }
    public async Task<ThreeWayFile> ThreeWayFileAsync(ThreeWayRevisions revisions, string path) {
        ValidatePath(path);
        async Task<(string Text, bool Exists)> Read(string revision) {
            string spec = revision + ":" + path;
            if ((await RunResultAsync(["cat-file", "-e", spec])).ExitCode != 0) return ("", false);
            string text = await ReadObject(spec);
            if (text.Contains('\0')) throw new InvalidOperationException("This is a binary file. Three-way comparison currently supports text files.");
            return (text, true);
        }
        var basis = await Read(revisions.Base); var left = await Read(revisions.Left); var right = await Read(revisions.Right);
        return new(path, basis.Text, left.Text, right.Text, basis.Exists, left.Exists, right.Exists);
    }
}
