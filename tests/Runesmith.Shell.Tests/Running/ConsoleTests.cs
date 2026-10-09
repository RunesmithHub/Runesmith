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
    [InlineData("/src/App/Program.cs(12,5): error CS0103: The name 'x' does not exist", "/src/App/Program.cs", 12, 5)]
    [InlineData("Program.cs(3): warning CS0168", "Program.cs", 3, 0)]
    [InlineData("   at App.Program.Main(String[] args) in /home/me/App/Program.cs:line 17", "/home/me/App/Program.cs", 17, 0)]
    [InlineData(@"   at App.Program.Main() in C:\src\App\Program.cs:line 9", @"C:\src\App\Program.cs", 9, 0)]
    [InlineData("src/Main.java:7: error: ';' expected", "src/Main.java", 7, 0)]
    public void FindsCompilerMessagesAndDotnetStackFrames(string line, string path, int number, int column)
    {
        var link = Assert.Single(ConsoleLinks.Find(line));

        Assert.Equal(path, link.Path);
        Assert.Equal(number, link.Line);
        Assert.Equal(column, link.Column);
        Assert.False(link.IsSourceRelative);
        Assert.StartsWith(path, line[link.Start..], StringComparison.Ordinal);
    }

    [Fact]
    public void FindsJavaStackFramesRelativeToTheirPackage()
    {
        var links = ConsoleLinks.Find("\tat com.example.app.Main$Inner.run(Main.java:42)");
        var noPackage = ConsoleLinks.Find("\tat Main.main(Main.java:3)");
        var module = ConsoleLinks.Find("\tat java.base/java.lang.Thread.run(Thread.java:1583)");

        var link = Assert.Single(links);
        Assert.Equal("com/example/app/Main.java", link.Path);
        Assert.Equal(42, link.Line);
        Assert.True(link.IsSourceRelative);
        Assert.Equal("Main.java:42", "\tat com.example.app.Main$Inner.run(Main.java:42)".Substring(link.Start, link.Length));
        Assert.Equal("Main.java", Assert.Single(noPackage).Path);
        Assert.Equal("java/lang/Thread.java", Assert.Single(module).Path);
    }

    [Fact]
    public void IgnoresLinesWithoutLocations()
    {
        Assert.Empty(ConsoleLinks.Find("Console.WriteLine(12) is not a file"));
        Assert.Empty(ConsoleLinks.Find("Hello, world"));
        Assert.Empty(ConsoleLinks.Find("time: 12:30"));
    }

    [Fact]
    public void ResolvesLinksAgainstTheFoldersAndJavaSourceRoots()
    {
        var source = Path.Combine(root, "src", "main", "java", "com", "example");
        Directory.CreateDirectory(source);
        var java = Path.Combine(source, "Main.java");
        var csharp = Path.Combine(root, "Program.cs");
        File.WriteAllText(java, "");
        File.WriteAllText(csharp, "");
        IReadOnlyList<string>? files = null;
        var resolver = new ConsoleLinkResolver(() => [root], () => files);

        Assert.Equal(csharp, resolver.Resolve(new ConsoleLink(0, 1, "Program.cs", 1, 0)));
        Assert.Equal(csharp, resolver.Resolve(new ConsoleLink(0, 1, csharp, 1, 0)));
        Assert.Null(resolver.Resolve(new ConsoleLink(0, 1, "Missing.cs", 1, 0)));
        Assert.Null(resolver.Resolve(new ConsoleLink(0, 1, "com/example/Main.java", 1, 0, IsSourceRelative: true)));

        files = [csharp, java];
        Assert.Equal(java, resolver.Resolve(new ConsoleLink(0, 1, "com/example/Main.java", 1, 0, IsSourceRelative: true)));
        Assert.Null(resolver.Resolve(new ConsoleLink(0, 1, "java/lang/Thread.java", 1, 0, IsSourceRelative: true)));
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
