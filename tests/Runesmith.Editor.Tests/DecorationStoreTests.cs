using Runesmith.Editor.Decorations;
using Runesmith.Sdk.Documents;
using Runesmith.Text;

namespace Runesmith.Editor.Tests;

public sealed class DecorationStoreTests
{
    private static readonly object Source = new();
    private static readonly object Other = new();

    [Fact]
    public void FollowsInsertionsBeforeAndInsideASpan()
    {
        var store = new DecorationStore();
        store.Replace(Source, [new TextHighlight(new TextSpan(10, 5))], 100);

        store.Map([new TextChange(new TextSpan(2, 0), "abc")]);
        Assert.Equal(new TextSpan(13, 5), Single(store));

        store.Map([new TextChange(new TextSpan(15, 0), "xy")]);
        Assert.Equal(new TextSpan(13, 7), Single(store));

        store.Map([new TextChange(new TextSpan(40, 3), "")]);
        Assert.Equal(new TextSpan(13, 7), Single(store));
    }

    [Fact]
    public void DropsDecorationsWhoseTextIsDeleted()
    {
        var store = new DecorationStore();
        store.Replace(Source, [new TextHighlight(new TextSpan(10, 5)), new GutterMarker(new TextSpan(30, 2), "flag"), new InlayHint(50, "x:")], 100);

        store.Map([new TextChange(new TextSpan(8, 10), ""), new TextChange(new TextSpan(48, 4), "")]);

        var left = store.Of(Source);
        Assert.Single(left);
        Assert.IsType<GutterMarker>(left[0].Decoration);
        Assert.Equal(new TextSpan(20, 2), left[0].Span);
    }

    [Fact]
    public void KeepsAnInlayHintWhenTextIsTypedAtIt()
    {
        var store = new DecorationStore();
        store.Replace(Source, [new InlayHint(5, "count:")], 20);

        store.Map([new TextChange(new TextSpan(5, 0), "12")]);

        Assert.Equal(7, Single(store).Start);
    }

    [Fact]
    public void KeepsSourcesApartAndReplacesOnlyOne()
    {
        var store = new DecorationStore();
        store.Replace(Source, [new CodeLens(new TextSpan(0, 1), "3 references")], 20);
        store.Replace(Other, [new InlayHint(4, ": int")], 20);
        Assert.True(store.HasCodeLenses);
        Assert.True(store.HasInlayHints);

        Assert.False(store.Replace(Other, [new InlayHint(4, ": int")], 20));
        Assert.True(store.Replace(Source, [], 20));

        Assert.False(store.HasCodeLenses);
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void FindsLongSpansThatStartBeforeTheRange()
    {
        var store = new DecorationStore();
        store.Replace(Source, [new TextHighlight(new TextSpan(0, 100)), new TextHighlight(new TextSpan(5, 1)), new TextHighlight(new TextSpan(60, 2))], 200);

        var found = store.In<TextHighlight>(new TextSpan(50, 5)).Select(p => p.Span).ToList();

        Assert.Equal([new TextSpan(0, 100)], found);
    }

    [Fact]
    public void PlacesAnAnswerForAnOlderTextThroughTheEditsMadeSince()
    {
        var since = new List<IReadOnlyList<TextChange>>
        {
            new[] { new TextChange(new TextSpan(0, 0), "// header\n") },
            new[] { new TextChange(new TextSpan(20, 4), "") },
        };

        var placed = DecorationStore.Place([new InlayHint(12, "a:"), new GutterMarker(new TextSpan(30, 3), "flag"), new InlayHint(80, "too far")], 40, since);

        Assert.Equal([new TextSpan(36, 3)], placed.Select(p => p.Span));
    }

    private static TextSpan Single(DecorationStore store) => Assert.Single(store.Of(Source)).Span;
}
