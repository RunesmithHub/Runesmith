using Runesmith.Sdk.Running;
using Runesmith.Shell.Running;

namespace Runesmith.Shell.Tests.Running;

public sealed class ConsoleTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("runesmith-console-").FullName;

    [Fact]
    public void AnsiColorsAndBoldBecomeStylesAndTheEscapesAreRemoved()
    {
        var console = new ConsoleBuffer();

        console.Append("plain \u001b[1;31mbold red\u001b[0m \u001b[92mbright green\u001b[39m done\n", ConsoleSource.Output);

        var line = Assert.Single(console.Drain().Lines);
        Assert.Equal("plain bold red bright green done", line.Text);
        Assert.Equal(
            [
                new ConsoleSpan(0, 6, ConsoleStyle.Default),
                new ConsoleSpan(6, 8, new ConsoleStyle(1, -1, true)),
                new ConsoleSpan(14, 1, ConsoleStyle.Default),
                new ConsoleSpan(15, 12, new ConsoleStyle(10, -1, false)),
                new ConsoleSpan(27, 5, ConsoleStyle.Default),
            ],
            line.Spans);
    }

    [Fact]
    public void StylesAndEscapesCarryAcrossPiecesAndLines()
    {
        var console = new ConsoleBuffer();

        console.Append("\u001b[3", ConsoleSource.Output);
        console.Append("4mblue\nstill blue\u001b]0;title\u0007\u001b[m\n", ConsoleSource.Output);

        var lines = console.Drain().Lines;
        Assert.Equal(["blue", "still blue"], lines.Select(l => l.Text));
        Assert.All(lines, l => Assert.Equal(4, l.Spans[0].Style.Foreground));
    }

    [Fact]
    public void AnUnfinishedLineShowsAndIsReplacedAsItGrows()
    {
        var console = new ConsoleBuffer();
        var view = new ConsoleLineList();

        console.Append("Enter your name: ", ConsoleSource.Output);
        view.Apply(console.Drain(all: true));
        Assert.Equal(["Enter your name: "], view.Select(l => l.Text));

        console.Append("Ada\nHello", ConsoleSource.Output);
        console.Append(", Ada\n", ConsoleSource.Output);
        view.Apply(console.Drain());

        Assert.Equal(["Enter your name: Ada", "Hello, Ada"], view.Select(l => l.Text));
    }

    [Fact]
    public void ACarriageReturnStartsTheLineAgainButCarriageReturnLineFeedEndsIt()
    {
        var console = new ConsoleBuffer();

        console.Append("10%\r50%\r", ConsoleSource.Output);
        console.Append("\n", ConsoleSource.Output);
        console.Append("windows line\r\nnext\r\n", ConsoleSource.Output);

        Assert.Equal(["50%", "windows line", "next"], console.Drain().Lines.Select(l => l.Text));
    }

    [Fact]
    public void ErrorOutputKeepsItsOwnLines()
    {
        var console = new ConsoleBuffer();

        console.Append("out ", ConsoleSource.Output);
        console.Append("error\n", ConsoleSource.Error);
        console.Append("line\n", ConsoleSource.Output);

        var lines = console.Drain().Lines;
        Assert.Equal([("out line", ConsoleSource.Output), ("error", ConsoleSource.Error)], lines.Select(l => (l.Text, l.Source)));
    }

    [Fact]
    public void KeepsAtMostTheMaximumAndTheViewFollows()
    {
        var console = new ConsoleBuffer(maximumLines: 100);
        var view = new ConsoleLineList();

        for (var i = 0; i < 250; i++)
        {
            console.AppendLine($"line {i}", ConsoleSource.Output);
            if (i % 7 == 0)
                view.Apply(console.Drain());
        }

        view.Apply(console.Drain());
        Assert.True(console.Count <= 100);
        Assert.Equal(console.Count, view.Count);
        Assert.Equal("line 249", view[^1].Text);

        console.Clear();
        view.Apply(console.Drain());
        Assert.Empty(view);
    }

    [Theory]
    [InlineData("/src/app/main.ts(12,5): error TS2304: Cannot find name 'x'", "/src/app/main.ts", 12, 5)]
    [InlineData("main.ts(3): warning", "main.ts", 3, 0)]
    [InlineData("src/main.go:7: syntax error", "src/main.go", 7, 0)]
    [InlineData(@"C:\src\app\main.go:9:4: undefined: x", @"C:\src\app\main.go", 9, 4)]
    public void FindsPathsWithALine(string line, string path, int number, int column)
    {
        var link = Assert.Single(ConsoleLinks.Find(line));

        Assert.Equal(path, link.Path);
        Assert.Equal(number, link.Line);
        Assert.Equal(column, link.Column);
        Assert.False(link.IsSourceRelative);
        Assert.StartsWith(path, line[link.Start..], StringComparison.Ordinal);
    }

    [Fact]
    public void IgnoresLinesWithoutLocations()
    {
        Assert.Empty(ConsoleLinks.Find("Console.WriteLine(12) is not a file"));
        Assert.Empty(ConsoleLinks.Find("Hello, world"));
        Assert.Empty(ConsoleLinks.Find("time: 12:30"));
    }

    [Fact]
    public void ResolvesLinksAgainstTheFoldersAndSourceRoots()
    {
        var source = Path.Combine(root, "src", "main", "com", "example");
        Directory.CreateDirectory(source);
        var nested = Path.Combine(source, "Main.kt");
        var top = Path.Combine(root, "main.go");
        File.WriteAllText(nested, "");
        File.WriteAllText(top, "");
        IReadOnlyList<string>? files = null;
        var resolver = new ConsoleLinkResolver(() => [root], () => files);

        Assert.Equal(top, resolver.Resolve(new ConsoleLink(0, 1, "main.go", 1, 0)));
        Assert.Equal(top, resolver.Resolve(new ConsoleLink(0, 1, top, 1, 0)));
        Assert.Null(resolver.Resolve(new ConsoleLink(0, 1, "missing.go", 1, 0)));
        Assert.Null(resolver.Resolve(new ConsoleLink(0, 1, "com/example/Main.kt", 1, 0, IsSourceRelative: true)));

        files = [top, nested];
        Assert.Equal(nested, resolver.Resolve(new ConsoleLink(0, 1, "com/example/Main.kt", 1, 0, IsSourceRelative: true)));
        Assert.Null(resolver.Resolve(new ConsoleLink(0, 1, "lang/Thread.kt", 1, 0, IsSourceRelative: true)));
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
