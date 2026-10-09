using Runesmith.Git.Git;

namespace Runesmith.Git.Views.History;

/// <summary>A line of the graph in one half of a row: from a lane at the half's top to a lane at its bottom.</summary>
/// <param name="From">The lane at the top of the half.</param>
/// <param name="To">The lane at the bottom of the half.</param>
/// <param name="Color">The line's color index.</param>
internal readonly record struct GraphEdge(int From, int To, int Color);

/// <summary>What the graph draws in one commit's row: the commit's dot and the lines through the row's upper and lower halves.</summary>
/// <param name="Lane">The lane of the commit's dot.</param>
/// <param name="Color">The dot's color index.</param>
/// <param name="Top">Lines from the row's top edge to its middle.</param>
/// <param name="Bottom">Lines from the row's middle to its bottom edge.</param>
internal sealed record GraphRow(int Lane, int Color, IReadOnlyList<GraphEdge> Top, IReadOnlyList<GraphEdge> Bottom)
{
    /// <summary>Gets the number of lanes the row uses.</summary>
    public int Width { get; } = Math.Max(Lane + 1, Math.Max(Top.Count == 0 ? 0 : Top.Max(e => Math.Max(e.From, e.To)) + 1, Bottom.Count == 0 ? 0 : Bottom.Max(e => Math.Max(e.From, e.To)) + 1));
}

/// <summary>Lays out the branch graph in lanes one commit at a time, so each page of the log adds its rows without laying out the pages before.</summary>
/// <remarks>Commits must come with children before parents, as <c>git log --date-order</c> gives them. Each lane waits for one commit: the
/// parent of the commit above it in that lane. Lanes that end close up to the left in the next row.</remarks>
internal sealed class GraphLayout
{
    private List<Lane?> lanes = [];
    private int nextColor;

    /// <summary>Adds the next commit and returns what its row draws.</summary>
    public GraphRow Add(GitCommit commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        var node = lanes.FindIndex(l => l is { } lane && lane.Sha == commit.Sha);
        int color;
        if (node < 0)
        {
            node = FreeSlot(-1);
            color = nextColor++;
        }
        else
        {
            color = lanes[node]!.Value.Color;
        }

        var top = new List<GraphEdge>();
        for (var i = 0; i < lanes.Count; i++)
        {
            if (lanes[i] is { } lane)
                top.Add(new GraphEdge(i, lane.Sha == commit.Sha ? node : i, lane.Color));
        }

        for (var i = 0; i < lanes.Count; i++)
        {
            if (lanes[i] is { } lane && lane.Sha == commit.Sha)
                lanes[i] = null;
        }

        var mergeTargets = new List<int>();
        if (commit.Parents.Count > 0)
        {
            Set(node, new Lane(commit.Parents[0], color));
            foreach (var parent in commit.Parents.Skip(1))
            {
                var existing = lanes.FindIndex(l => l is { } lane && lane.Sha == parent);
                if (existing >= 0)
                {
                    mergeTargets.Add(existing);
                    continue;
                }

                var slot = FreeSlot(node);
                Set(slot, new Lane(parent, nextColor++));
                mergeTargets.Add(slot);
            }
        }

        var position = new int[lanes.Count];
        var compacted = new List<Lane?>();
        for (var i = 0; i < lanes.Count; i++)
        {
            if (lanes[i] is not null)
            {
                position[i] = compacted.Count;
                compacted.Add(lanes[i]);
            }
        }

        var bottom = new List<GraphEdge>();
        for (var i = 0; i < lanes.Count; i++)
        {
            if (lanes[i] is not { } lane)
                continue;

            var isNew = i == node || (mergeTargets.Contains(i) && !top.Any(e => e.From == i && e.To == i));
            bottom.Add(new GraphEdge(isNew ? node : i, position[i], lane.Color));
            if (!isNew && mergeTargets.Contains(i))
                bottom.Add(new GraphEdge(node, position[i], lane.Color));
        }

        lanes = compacted;
        return new GraphRow(node, color, top, bottom);
    }

    /// <summary>Forgets every lane, to lay out a new log from its first commit.</summary>
    public void Reset()
    {
        lanes = [];
        nextColor = 0;
    }

    private int FreeSlot(int except)
    {
        for (var i = 0; i < lanes.Count; i++)
        {
            if (lanes[i] is null && i != except)
                return i;
        }

        lanes.Add(null);
        return lanes.Count - 1;
    }

    private void Set(int index, Lane lane)
    {
        while (lanes.Count <= index)
            lanes.Add(null);
        lanes[index] = lane;
    }

    private readonly record struct Lane(string Sha, int Color);
}
