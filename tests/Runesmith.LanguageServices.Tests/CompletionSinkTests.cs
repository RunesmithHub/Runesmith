using Runesmith.Text;

namespace Runesmith.LanguageServices.Tests;

public sealed class CompletionSinkTests
{
    [Fact]
    public void TheWordIsTheIdentifierBeforeTheOffset()
    {
        var snapshot = TextSnapshot.Create("Console.Wri");
        var sink = new CompletionSink(snapshot, snapshot.Length);

        Assert.Equal(new TextSpan(8, 3), sink.WordSpan);
        Assert.Equal("Wri", sink.Typed);
    }

    [Fact]
    public void KeepsOnlyMatchesAndRanksThemByScoreThenGroupThenLabel()
    {
        var snapshot = TextSnapshot.Create("x.Wr");
        var sink = new CompletionSink(snapshot, snapshot.Length);
        sink.Add(new CompletionCandidate("Read", CompletionKind.Method));
        sink.Add(new CompletionCandidate("Write", CompletionKind.Method, SortGroup: 1));
        sink.Add(new CompletionCandidate("Wrap", CompletionKind.Method, SortGroup: 1));
        sink.Add(new CompletionCandidate("Wrap", CompletionKind.Keyword, SortGroup: 0));

        var result = sink.ToResult(version: 3);

        Assert.Equal(4, sink.Added);
        Assert.Equal(["Wrap", "Wrap", "Write"], result.Items.Select(i => i.Label));
        Assert.Equal(CompletionKind.Keyword, result.Items[0].Kind);
        Assert.Equal(3, result.Version);
        Assert.False(result.IsIncomplete);
    }

    [Fact]
    public void ACutListIsIncomplete()
    {
        var snapshot = TextSnapshot.Create("");
        var sink = new CompletionSink(snapshot, 0);
        for (var i = 0; i < 10; i++)
            sink.Add(new CompletionCandidate($"Item{i}", CompletionKind.Field));

        var result = sink.ToResult(version: 1, capacity: 4);

        Assert.Equal(4, result.Items.Count);
        Assert.True(result.IsIncomplete);
    }

    [Fact]
    public void FiltersByTheFilterTextWhenGiven()
    {
        var snapshot = TextSnapshot.Create("ov");
        var sink = new CompletionSink(snapshot, 2);
        sink.Add(new CompletionCandidate("public override string ToString()", CompletionKind.Method, FilterText: "override ToString"));

        Assert.Single(sink.ToResult(1).Items);
    }

    [Fact]
    public void AWordSpanAfterTheOffsetIsRejected()
    {
        var snapshot = TextSnapshot.Create("abc");
        var sink = new CompletionSink(snapshot, 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => sink.SetWordSpan(new TextSpan(2, 1)));
    }
}
