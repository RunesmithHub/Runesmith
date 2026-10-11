using System.Text;
using Runesmith.Sdk.Analyzers.Fluent;

namespace Runesmith.Sdk.Analyzers.Tests;

public sealed class FluentWriterTests
{
    [Theory]
    [InlineData("Save all")]
    [InlineData("  Leading spaces")]
    [InlineData("Trailing spaces  ")]
    [InlineData("{Braces} and }")]
    [InlineData("Two\nlines")]
    [InlineData("[Bracket] first\n*star\n.dot\n  indented")]
    [InlineData("Quote \" and back\\slash")]
    [InlineData("Blank\n\nline")]
    [InlineData("[Bracket] on one line")]
    public void AnEscapedMessageShowsExactlyTheText(string text)
    {
        var fluent = FluentWriter.Message("text", FluentWriter.Escape(text));

        var resource = FluentParser.Parse(fluent + "\nnext = Next\n");

        Assert.Empty(resource.Errors);
        Assert.Equal(text, Show(resource.Messages.First().Value!));
        Assert.Equal("next", resource.Messages.Last().Id);
    }

    [Theory]
    [InlineData("Save all files", "save-all-files")]
    [InlineData("Ünïcödé text, here!", "unicode-text-here")]
    [InlineData("123 go", "text-123-go")]
    [InlineData("…", "text")]
    [InlineData("One two three four five six seven", "one-two-three-four-five")]
    public void AnIdIsMadeFromTheWords(string text, string id) => Assert.Equal(id, FluentWriter.Id(text, _ => false));

    [Fact]
    public void AnIdThatIsTakenGetsANumber()
    {
        var taken = new HashSet<string> { "save", "save-2" };

        Assert.Equal("save-3", FluentWriter.Id("Save", taken.Contains));
    }

    private static string Show(FluentPattern pattern)
    {
        var text = new StringBuilder();
        foreach (var element in pattern.Elements)
        {
            text.Append(element switch
            {
                FluentText part => part.Value,
                FluentPlaceable { Expression: FluentStringLiteral literal } => literal.Value,
                _ => throw new InvalidOperationException("Only text and quoted text are expected."),
            });
        }

        return text.ToString();
    }
}
