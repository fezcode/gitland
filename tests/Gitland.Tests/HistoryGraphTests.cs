using Gitland.Core;
using Xunit;

namespace Gitland.Tests;

public class HistoryGraphTests {
    static GitCommit Commit(string hash, string parents) => new(hash, hash, "", "", "", "", parents);
    [Fact] public void MergeSplitsAndReconvergesWithoutConnectingUnrelatedCommits() {
        var graph = HistoryGraph.Layout([Commit("merge", "left right"), Commit("left", "base"), Commit("right", "base"), Commit("base", ""), Commit("unrelated", "")]);
        Assert.Equal(new[] { new HistoryEdge(0, 0), new HistoryEdge(0, 1) }, graph[0].Edges);
        Assert.Equal(2, graph[0].Width); Assert.Equal(1, graph[2].Column);
        Assert.Contains(new HistoryEdge(1, 0), graph[2].Edges);
        Assert.Empty(graph[3].Edges); Assert.True(graph[4].NewLane); Assert.Empty(graph[4].Edges);
    }
    [Fact] public void LinearHistoryStaysInOneLane() {
        var graph = HistoryGraph.Layout([Commit("a", "b"), Commit("b", "c"), Commit("c", "")]);
        Assert.All(graph, row => Assert.Equal(0, row.Column)); Assert.All(graph, row => Assert.Equal(1, row.Width));
        Assert.True(graph[0].NewLane); Assert.False(graph[1].NewLane); Assert.Empty(graph[2].Edges);
    }
    [Fact] public void LaneIdentitySurvivesColumnChangesAndMatchesEveryConnector() {
        var graph = HistoryGraph.Layout([Commit("merge", "left right third"), Commit("left", "base"), Commit("right", "base"), Commit("third", "base"), Commit("base", "")]);
        Assert.Equal(3, graph[0].EdgeLanes.Distinct().Count());
        for (int i = 0; i < graph.Count - 1; i++)
            for (int e = 0; e < graph[i].Edges.Count; e++)
                Assert.Equal(graph[i].EdgeLanes[e], graph[i + 1].IncomingLanes[graph[i].Edges[e].To]);
        Assert.Equal(graph[0].Lane, graph[^1].Lane);
        Assert.NotEqual(graph[1].Lane, graph[2].Lane);
        Assert.NotEqual(graph[2].Lane, graph[3].Lane);
    }
}
