namespace Gitland.Core;

public sealed record HistoryEdge(int From, int To);
public sealed record HistoryGraphRow(int Column, int Incoming, bool NewLane, IReadOnlyList<HistoryEdge> Edges, int Width);
public static class HistoryGraph {
    public static IReadOnlyList<HistoryGraphRow> Layout(IReadOnlyList<GitCommit> commits) {
        var lanes = new List<string>(); var result = new List<HistoryGraphRow>();
        foreach (var commit in commits) {
            int column = lanes.IndexOf(commit.Hash); bool fresh = column < 0;
            if (fresh) { column = lanes.Count; lanes.Add(commit.Hash); }
            var before = lanes.ToArray(); lanes.RemoveAt(column);
            var parents = commit.Parents.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int insertion = Math.Min(column, lanes.Count);
            foreach (string parent in parents) if (!lanes.Contains(parent)) lanes.Insert(insertion++, parent);
            var edges = new List<HistoryEdge>();
            for (int i = 0; i < before.Length; i++) {
                if (i == column) { foreach (var parent in parents) edges.Add(new(i, lanes.IndexOf(parent))); }
                else edges.Add(new(i, lanes.IndexOf(before[i])));
            }
            result.Add(new(column, before.Length, fresh, edges, Math.Max(before.Length, lanes.Count)));
        }
        return result;
    }
}
