using System.Text;
using System.Text.RegularExpressions;

namespace Gitland.Core;

public enum Resolution { Unresolved, Ours, Theirs, Both, Base, Smart }
public sealed class ConflictBlock {
    public required int Id { get; init; }
    public required string Ours { get; init; }
    public required string Theirs { get; init; }
    public string? Base { get; set; }
    public SmartSuggestion? Suggestion { get; set; }
    public required string Original { get; init; }
    public Resolution Choice { get; set; }
    public string Result => Choice switch { Resolution.Ours => Ours, Resolution.Theirs => Theirs, Resolution.Both => Ours + Theirs, Resolution.Base => Base ?? Ours, Resolution.Smart => Suggestion?.Text ?? Original, _ => Original };
}

public sealed class MergeDocument {
    readonly List<object> _parts = [];
    public List<ConflictBlock> Conflicts { get; } = [];
    public int Unresolved => Conflicts.Count(c => c.Choice == Resolution.Unresolved);
    public string Render() => string.Concat(_parts.Select(p => p is ConflictBlock c ? c.Result : (string)p));
    public (int Start, int Length) Range(int id) {
        int offset = 0;
        foreach (var part in _parts) {
            var value = part is ConflictBlock c ? c.Result : (string)part;
            if (part is ConflictBlock block && block.Id == id) return (offset, value.Length);
            offset += value.Length;
        }
        return (0, 0);
    }
    public void AddBaseHints(MergeDocument reconstructed) {
        static string Normalize(string s) => s.Replace("\r\n", "\n");
        foreach (var block in Conflicts.Where(c => c.Base == null)) {
            var candidates = reconstructed.Conflicts.Where(c => Normalize(c.Ours) == Normalize(block.Ours) && Normalize(c.Theirs) == Normalize(block.Theirs)).ToArray();
            // Ambiguous repetitions stay manual. Never replace existing working-tree content with a reconstruction.
            if (candidates.Length == 1) block.Base = candidates[0].Base?.Replace("\r\n", "\n").Replace("\n", block.Original.Contains("\r\n") ? "\r\n" : "\n");
        }
    }

    public static MergeDocument Parse(string text) {
        var doc = new MergeDocument();
        var lines = Regex.Matches(text, @"[^\n]*\n|[^\n]+$").Select(m => m.Value).ToArray();
        var plain = new StringBuilder();
        for (int i = 0; i < lines.Length;) {
            var open = Regex.Match(lines[i], @"^(<{7,})(?: |\r?$)");
            if (!open.Success) { plain.Append(lines[i++]); continue; }
            int start = i++, width = open.Groups[1].Length;
            var ours = new StringBuilder(); var ancestor = new StringBuilder(); var theirs = new StringBuilder();
            int state = 0; bool hasBase = false, closed = false;
            for (; i < lines.Length; i++) {
                var line = lines[i];
                if (state == 0 && Marker(line, '|', width)) { state = 1; hasBase = true; continue; }
                if (state < 2 && Marker(line, '=', width)) { state = 2; continue; }
                if (state == 2 && Marker(line, '>', width)) { i++; closed = true; break; }
                (state == 0 ? ours : state == 1 ? ancestor : theirs).Append(line);
            }
            if (!closed) throw new InvalidOperationException("The file contains an incomplete conflict block. Refresh after Git finishes writing it.");
            if (plain.Length > 0) { doc._parts.Add(plain.ToString()); plain.Clear(); }
            var block = new ConflictBlock { Id = doc.Conflicts.Count + 1, Ours = ours.ToString(), Theirs = theirs.ToString(), Base = hasBase ? ancestor.ToString() : null, Original = string.Concat(lines[start..i]) };
            doc.Conflicts.Add(block); doc._parts.Add(block);
        }
        if (plain.Length > 0) doc._parts.Add(plain.ToString());
        return doc;
    }
    static bool Marker(string line, char c, int width) => line.StartsWith(new string(c, width)) && (line.Length == width || line[width] is ' ' or '\r' or '\n');
    public static bool HasMarkers(string text) => Regex.IsMatch(text, @"^(?:<{7,}|>{7,}|\|{7,})(?: |\r?$)|^={7,}\r?$", RegexOptions.Multiline);
}
