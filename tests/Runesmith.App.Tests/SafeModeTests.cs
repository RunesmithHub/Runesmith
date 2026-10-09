namespace Runesmith.App.Tests;

public sealed class SafeModeTests : IDisposable
{
    private static readonly SuspectPlugin Pixel = new("pixel.icons", "Pixel File Icons", "2.1.3");
    private static readonly SuspectPlugin Lumen = new("lumen.todo", "Lumen Todo", "1.0.0");

    private readonly string folder = Directory.CreateTempSubdirectory("runesmith-startups-").FullName;
    private readonly Clock time = new(new DateTimeOffset(2026, 10, 9, 9, 0, 0, TimeSpan.Zero));

    private string File => Path.Combine(folder, "startups.json");

    public void Dispose() => Directory.Delete(folder, recursive: true);

    [Fact]
    public void TwoStartsThatNeverShowedTheWindowOfferSafeModeWithThePluginsThatWereLoading()
    {
        Begin().Loaded([Pixel]);
        Begin().Loaded([Pixel, Lumen]);

        var offer = Begin().Offer;

        Assert.NotNull(offer);
        Assert.Equal("Runesmith failed to start twice in a row.", offer.Reason);
        Assert.Equal([Pixel, Lumen], offer.Plugins);
    }

    [Fact]
    public void TwoCrashesWithinAMinuteNameThePluginsOnTheStack()
    {
        for (var i = 0; i < 2; i++)
        {
            var guard = Begin();
            guard.Loaded([Pixel, Lumen]);
            guard.Shown();
            time.Now += TimeSpan.FromSeconds(20);
            guard.Crashed(Thrown(), assembly => assembly == typeof(SafeModeTests).Assembly ? Lumen : null);
        }

        var offer = Begin().Offer!;

        Assert.Equal("Runesmith closed unexpectedly twice while starting.", offer.Reason);
        Assert.Equal([Lumen], offer.Plugins);
    }

    [Fact]
    public void AStartThatRanForAMinuteOrClosedNormallyIsGood()
    {
        var stable = Begin();
        stable.Shown();
        stable.Stable();
        Begin();
        Assert.Null(Begin().Offer);

        var closed = Begin();
        closed.Shown();
        closed.Exited();
        Begin();
        Assert.Null(Begin().Offer);
    }

    [Fact]
    public void AStartInSafeModeNeitherOffersSafeModeNorCountsAsBad()
    {
        Begin();
        Begin();

        Assert.Null(StartupGuard.Begin(File, time, safeMode: true).Offer);
        Assert.Null(Begin().Offer);
    }

    [Fact]
    public void AcceptingTheOfferMakesTheNextNormalStartNotAskAgain()
    {
        Begin();
        Begin();
        var offered = Begin();
        Assert.NotNull(offered.Offer);

        Assert.Null(Begin().Offer);
    }

    private StartupGuard Begin() => StartupGuard.Begin(File, time, safeMode: false);

    private static InvalidOperationException Thrown()
    {
        try
        {
            throw new InvalidOperationException("The plugin crashed.");
        }
        catch (InvalidOperationException exception)
        {
            return exception;
        }
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
