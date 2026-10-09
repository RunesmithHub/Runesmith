using Runesmith.Shell.Pages;

namespace Runesmith.Shell.Tests.Pages;

public sealed class WelcomePageTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0.5, "Just now")]
    [InlineData(1, "1 minute ago")]
    [InlineData(45, "45 minutes ago")]
    [InlineData(60, "1 hour ago")]
    [InlineData(300, "5 hours ago")]
    [InlineData(1500, "Yesterday")]
    [InlineData(4 * 1440, "4 days ago")]
    public void SaysHowLongAgoAProjectWasOpened(double minutes, string expected) =>
        Assert.Equal(expected, WelcomePage.Ago(Now.AddMinutes(-minutes), Now));

    [Fact]
    public void GivesTheDateOfProjectsOpenedLongAgo() =>
        Assert.Contains("2026", WelcomePage.Ago(Now.AddDays(-90), Now), StringComparison.Ordinal);

    [Fact]
    public void WritesPathsUnderTheHomeFolderFromATilde()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        Assert.Equal(Path.Combine("~", "Projects", "Runesmith"), WelcomePage.HomeRelative(Path.Combine(home, "Projects", "Runesmith")));
        Assert.Equal(home + "-other", WelcomePage.HomeRelative(home + "-other"));
    }
}
