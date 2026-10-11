using Linguini.Syntax.Ast;
using Linguini.Syntax.Parser;
using Runesmith.Sdk.Analyzers.Fluent;

namespace Runesmith.Sdk.Analyzers.Tests;

public sealed class FluentParserTests
{
    public static TheoryData<string> Valid { get; } = new()
    {
        "hello = Hello, world!",
        "run-exited = The program exited with code { $code }.",
        "files = { $count ->\n    [0] No files\n    [one] One file\n   *[other] { $count } files\n}",
        "save = Save\n    .tooltip = Save the file\n    .key = S",
        "only-attributes =\n    .title = A title",
        "-brand = Runesmith\n    .gender = neuter\nabout = About { -brand }",
        "pronoun = { -brand.gender ->\n    [neuter] It\n   *[other] They\n}\n-brand = Runesmith\n    .gender = neuter",
        "multi =\n    First line\n      indented more\n    Last line",
        "start = Begins here\n    and goes on",
        "with-blank =\n    One\n\n    Three",
        "crlf = One\r\n    two\r\nnext = Three\r\n",
        "quoted = {\"{\"} and {\"\\u0041\"} and {\"\\\\\"}",
        "number = { NUMBER($n, minimumFractionDigits: 2) }",
        "date = { DATETIME($when, dateStyle: \"long\") }",
        "nested = { { $x } }",
        "term-call = { -brand(case: \"genitive\") }\n-brand = Runesmith",
        "## Group\n# About a\na = A\n\n### Resource\nb = B",
        "\uFEFFbom = After a byte order mark",
        "negative = { -3.5 }",
        "selector-literal = { \"a\" ->\n   *[a] A\n}",
        "block-placeable =\n    { $x }\n    text",
        "placeable-at-start =\n{ $x }",
        "message-ref = { other } and { other.title }\nother = Other\n    .title = Title",
    };

    public static TheoryData<string> Invalid { get; } = new()
    {
        "hello Hello",
        "hello =",
        "hello = { $x",
        "hello = text }",
        "hello = { $n ->\n    [one] One\n    [other] Many\n}",
        "hello = { other ->\n   *[a] A\n}",
        "hello = { -brand ->\n   *[a] A\n}",
        "hello = { -brand.gender }",
        "hello = { number($x) }",
        "hello = One\n\ttwo",
        "#No space",
        "hello = { \"unclosed }\n",
        "hello = { \"bad \\q escape\" }",
        "hello = { NUMBER(minimumFractionDigits: 2, $x) }",
        "hello = { NUMBER($x, style: \"percent\", style: \"decimal\") }",
        "-term =",
        "hello = { $n ->\n   *[one] One\n   *[other] Many\n}",
        "1hello = digits first",
        "    indented = at the start",
    };

    [Theory]
    [MemberData(nameof(Valid))]
    public void ValidFluentParsesWithoutErrors(string text)
    {
        var resource = FluentParser.Parse(text);

        Assert.Empty(resource.Errors);
        Assert.DoesNotContain(resource.Entries, e => e is FluentJunk);
    }

    [Theory]
    [MemberData(nameof(Invalid))]
    public void InvalidFluentHasAnErrorAndBecomesJunk(string text)
    {
        var resource = FluentParser.Parse(text);

        Assert.NotEmpty(resource.Errors);
        Assert.Contains(resource.Entries, e => e is FluentJunk);
    }

    [Theory]
    [MemberData(nameof(Valid))]
    [MemberData(nameof(Invalid))]
    public void TheParserAgreesWithTheRuntimeOnWhatParsesAndOnTheMessages(string text)
    {
        var ours = FluentParser.Parse(text);
        var runtime = LinguiniParser.FromFragment(text.TrimStart('\uFEFF')).ParseWithComments();

        Assert.Equal(runtime.Errors.Count > 0, ours.Errors.Count > 0);
        Assert.Equal(runtime.Entries.OfType<AstMessage>().Select(m => m.Id.ToString()), ours.Messages.Select(m => m.Id));
        Assert.Equal(
            runtime.Entries.OfType<AstMessage>().SelectMany(m => m.Attributes.Select(a => m.Id + "." + a.Id)),
            ours.Messages.SelectMany(m => m.Attributes.Select(a => m.Id + "." + a.Id)));
    }

    [Theory]
    [InlineData("hello = Hello, world!", "Hello, world!")]
    [InlineData("multi =\n    First line\n      indented more\n    Last line", "First line\n  indented more\nLast line")]
    [InlineData("start = Begins here\n    and goes on", "Begins here\nand goes on")]
    [InlineData("with-blank =\n    One\n\n    Three", "One\n\nThree")]
    [InlineData("crlf = One\r\n    two\r\n", "One\ntwo")]
    [InlineData("trailing = Text   \n\n", "Text")]
    public void TextKeepsItsLinesAndLosesTheCommonIndentation(string text, string expected)
    {
        var message = Assert.Single(FluentParser.Parse(text).Messages);

        Assert.Equal(expected, string.Concat(message.Value!.Elements.OfType<FluentText>().Select(t => t.Value)));
    }

    [Theory]
    [InlineData("hello Hello", 0, 6, "Expected = after the message id hello.")]
    [InlineData("hello =", 0, 0, "The message hello has no value")]
    [InlineData("a = A\nhello = { $x", 1, 8, "The placeable is not closed")]
    [InlineData("hello = text }", 0, 13, "A } closes nothing here")]
    [InlineData("hello = { $n ->\n    [one] One\n    [other] Many\n}", 3, 0, "needs a default variant")]
    [InlineData("hello = { other ->\n   *[a] A\n}", 0, 10, "The message other can't choose a variant")]
    [InlineData("hello = { -brand.gender }", 0, 10, "can only choose a variant")]
    [InlineData("hello = { number($x) }", 0, 10, "number is not a function")]
    [InlineData("hello = One\n\ttwo", 1, 0, "Fluent indents with spaces, not tabs.")]
    [InlineData("#No space", 0, 1, "A comment starts with #, ## or ### and a space.")]
    [InlineData("hello = { \"bad \\q escape\" }", 0, 15, "Unknown escape \\q")]
    [InlineData("hello = { NUMBER(minimumFractionDigits: 2, $x) }", 0, 43, "Positional arguments come before named ones.")]
    [InlineData("hello = { NUMBER($x, style: \"a\", style: \"b\") }", 0, 33, "The argument style is given twice.")]
    [InlineData("hello = { NUMBER($x, style: $s) }", 0, 28, "is a number or quoted text")]
    public void AnErrorSaysWhatIsWrongWhere(string text, int line, int column, string message)
    {
        var resource = FluentParser.Parse(text);

        var error = Assert.Single(resource.Errors);
        Assert.Contains(message, error.Message, StringComparison.Ordinal);
        Assert.Equal((line, column), resource.Position(error.Span.Start));
    }

    [Fact]
    public void AfterAnErrorTheNextMessageStillParses()
    {
        var resource = FluentParser.Parse("broken = { $x\nfine = Fine\n  .bad\nlast = Last");

        Assert.Equal(["fine", "last"], resource.Messages.Select(m => m.Id));
        Assert.Equal(2, resource.Errors.Count);
    }

    [Fact]
    public void ACommentDirectlyAboveAMessageBelongsToIt()
    {
        var resource = FluentParser.Parse("# The first line\n# and the second.\nhello = Hi\n\n# Apart\n\nbye = Bye\n## Group\nother = Other");

        var messages = resource.Messages.ToList();
        Assert.Equal("The first line\nand the second.", messages[0].Comment);
        Assert.Null(messages[1].Comment);
        Assert.Null(messages[2].Comment);
        Assert.Contains(resource.Entries.OfType<FluentComment>(), c => c.Level == 2 && c.Text == "Group");
        Assert.Contains(resource.Entries.OfType<FluentComment>(), c => c.Level == 1 && c.Text == "Apart");
    }

    [Fact]
    public void AVariantsValueAndItsKeyAreRead()
    {
        var message = Assert.Single(FluentParser.Parse("files = { $count ->\n    [0] No files\n   *[other] { $count } files\n}").Messages);

        var select = Assert.IsType<FluentSelectExpression>(Assert.IsType<FluentPlaceable>(Assert.Single(message.Value!.Elements)).Expression);
        Assert.Equal("count", Assert.IsType<FluentVariableReference>(select.Selector).Name);
        Assert.Equal([("0", true, false), ("other", false, true)], select.Variants.Select(v => (v.Key, v.IsNumber, v.IsDefault)));
    }

    [Fact]
    public void QuotedTextResolvesItsEscapes()
    {
        var message = Assert.Single(FluentParser.Parse("quoted = {\"\\u0041\\\"\\\\\\U01F600\"}").Messages);

        var literal = Assert.IsType<FluentStringLiteral>(Assert.IsType<FluentPlaceable>(Assert.Single(message.Value!.Elements)).Expression);
        Assert.Equal("A\"\\\U0001F600", literal.Value);
    }
}
