namespace Runesmith.Languages.Java.Syntax.Tests;

public sealed class LiteralTests
{
    [Theory]
    [InlineData("\"abc\"", "abc")]
    [InlineData("\"a\\tb\\n\"", "a\tb\n")]
    [InlineData("\"\\\"quoted\\\"\"", "\"quoted\"")]
    [InlineData("\"\\101\\60\"", "A0")]
    [InlineData("\"\\s\\\\\"", " \\")]
    public void ReadsStringValues(string literal, string value) => Assert.Equal(value, JavaLiterals.StringValue(literal));

    [Theory]
    [InlineData("'a'", "a")]
    [InlineData("'\\''", "'")]
    [InlineData("'\\0'", "\0")]
    [InlineData("'\\377'", "\u00ff")]
    public void ReadsCharacterValues(string literal, string value) => Assert.Equal(value, JavaLiterals.CharacterValue(literal));

    [Fact]
    public void StripsATextBlocksIncidentalIndentation()
    {
        var literal = "\"\"\"\n        <html>\n            <body>\\tx</body>   \n        </html>\n        \"\"\"";
        Assert.Equal("<html>\n    <body>\tx</body>\n</html>\n", JavaLiterals.TextBlockValue(literal));
    }

    [Fact]
    public void ATextBlockClosedOnItsLastLineHasNoFinalLineBreak()
    {
        var literal = "\"\"\"\n    a\n      b\\\n    c\"\"\"";
        Assert.Equal("a\n  bc", JavaLiterals.TextBlockValue(literal));
    }

    [Theory]
    [InlineData("42", 42ul)]
    [InlineData("1_000", 1000ul)]
    [InlineData("0x7fff_ffffL", 0x7fffffffUL)]
    [InlineData("0b101", 5ul)]
    [InlineData("017", 15ul)]
    [InlineData("0", 0ul)]
    [InlineData("0xFFFFFFFFFFFFFFFFL", ulong.MaxValue)]
    public void ReadsIntegerValues(string literal, ulong value) => Assert.Equal(value, JavaLiterals.IntegerValue(literal));

    [Fact]
    public void AnIntegerTooLargeHasNoValue() => Assert.Null(JavaLiterals.IntegerValue("0x1_0000_0000_0000_0000"));
}
