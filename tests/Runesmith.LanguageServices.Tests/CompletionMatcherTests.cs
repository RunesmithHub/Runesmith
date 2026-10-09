namespace Runesmith.LanguageServices.Tests;

public sealed class CompletionMatcherTests
{
    [Theory]
    [InlineData("Wri", "WriteLine")]
    [InlineData("wri", "WriteLine")]
    [InlineData("WL", "WriteLine")]
    [InlineData("wrln", "WriteLine")]
    [InlineData("ab", "cab")]
    [InlineData("", "anything")]
    public void Matches(string typed, string candidate) => Assert.NotNull(new CompletionMatcher(typed).Score(candidate));

    [Theory]
    [InlineData("xyz", "WriteLine")]
    [InlineData("lw", "WriteLine")]
    [InlineData("Writes", "WriteLine")]
    public void DoesNotMatch(string typed, string candidate) => Assert.Null(new CompletionMatcher(typed).Score(candidate));

    [Fact]
    public void RanksExactCasePrefixOverOtherCasePrefixOverWordStartsOverScatteredLetters()
    {
        var matcher = new CompletionMatcher("Wri");
        var exact = matcher.Score("WriteLine")!.Value;
        var otherCase = matcher.Score("writer")!.Value;
        var wordStarts = new CompletionMatcher("WL").Score("WriteLine")!.Value;
        var scattered = new CompletionMatcher("wtn").Score("WriteLine")!.Value;

        Assert.True(exact > otherCase);
        Assert.True(otherCase > wordStarts);
        Assert.True(wordStarts > scattered);
    }

    [Fact]
    public void FallsBackWhenJumpingToWordStartsLeavesNoRoom() => Assert.NotNull(new CompletionMatcher("ab").Score("cabAc"));

    [Fact]
    public void RejectsByMaskWithoutScoring()
    {
        var matcher = new CompletionMatcher("qz");
        Assert.Null(matcher.Score("WriteLine", CompletionMatcher.MaskOf("WriteLine")));
    }

    [Fact]
    public void ShorterCandidatesWinAmongEqualPrefixes()
    {
        var matcher = new CompletionMatcher("Get");
        Assert.True(matcher.Score("GetType") > matcher.Score("GetHashCode"));
    }
}
