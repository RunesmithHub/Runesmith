using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Runesmith.Editor.Rendering;
using Runesmith.Sdk.Languages;

namespace Runesmith.Editor.Tests;

public sealed class LineTextSourceTests
{
    private static readonly SyntaxStyle Keyword = new(Colors.Purple);
    private static readonly SyntaxStyle Type = new(Colors.Teal);

    [Fact]
    public Task NeighborsDrawnAlikeShareARunAndRunsCanBeAskedForAgainFromTheStart() => HeadlessSession.Value.Dispatch(() =>
    {
        var formatting = new TextFormatting(EditorOptions.Default, Brushes.White);
        var source = new LineTextSource("let shape = 12", [new(0, 3, Keyword), new(3, 1, Keyword), new(4, 5, Type), new(10, 0, Type), new(12, 1, Keyword)], formatting);

        var runs = Runs(source);
        Assert.Equal(
        [
            (0, 4, formatting.Get(Keyword)), (4, 5, formatting.Get(Type)), (9, 3, formatting.Default), (12, 1, formatting.Get(Keyword)), (13, 1, formatting.Default),
        ], runs);
        Assert.Equal(runs, Runs(source));
        return Task.FromResult(true);
    }, TestContext.Current.CancellationToken);

    private static List<(int Start, int Length, TextRunProperties? Properties)> Runs(LineTextSource source)
    {
        var runs = new List<(int, int, TextRunProperties?)>();
        for (var at = 0; source.GetTextRun(at) is TextCharacters run; at += run.Length)
            runs.Add((at, run.Length, run.Properties));
        return runs;
    }
}
