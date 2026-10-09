using Runesmith.Text;

namespace Runesmith.LanguageServices.Tests;

public sealed class DocumentHistoryTests
{
    [Fact]
    public void MapsOffsetsAndSpansThroughLaterVersions()
    {
        var history = new DocumentHistory(new SourceDocument("/a.cs", "csharp", 1, TextSnapshot.Create("hello world")));
        var first = history.Latest.Snapshot;
        history.Add(first.Apply([new TextChange(new TextSpan(0, 0), ">> ")]), [new TextChange(new TextSpan(0, 0), ">> ")]);
        var second = history.Latest.Snapshot;
        history.Add(second.Apply([new TextChange(new TextSpan(3, 5), "HELLO")]), [new TextChange(new TextSpan(3, 5), "HELLO")]);

        Assert.Equal(3, history.Latest.Version);
        Assert.Equal(9, history.MapOffset(1, 3, 6));
        Assert.Equal(new TextSpan(9, 5), history.MapSpan(1, 3, new TextSpan(6, 5)));
        Assert.Null(history.MapOffset(3, 1, 0));
    }

    [Fact]
    public void FindsTheVersionOfASnapshot()
    {
        var history = new DocumentHistory(new SourceDocument("/a.cs", "csharp", 1, TextSnapshot.Create("a")));
        var first = history.Latest.Snapshot;
        var second = history.Add(first.Apply([new TextChange(new TextSpan(1, 0), "b")]), [new TextChange(new TextSpan(1, 0), "b")]);

        Assert.Equal(1, history.Find(first)?.Version);
        Assert.Equal(2, history.Find(second.Snapshot)?.Version);
        Assert.Null(history.Find(TextSnapshot.Create("a")));
    }

    [Fact]
    public void ForgetsOldVersions()
    {
        var history = new DocumentHistory(new SourceDocument("/a.cs", "csharp", 1, TextSnapshot.Create("")));
        for (var i = 0; i < 40; i++)
        {
            var snapshot = history.Latest.Snapshot;
            history.Add(snapshot.Apply([new TextChange(new TextSpan(snapshot.Length, 0), "x")]), [new TextChange(new TextSpan(snapshot.Length, 0), "x")]);
        }

        Assert.Null(history.MapOffset(1, history.Latest.Version, 0));
        Assert.NotNull(history.MapOffset(history.Latest.Version - 5, history.Latest.Version, 0));
    }
}
