using Avalonia;
using Avalonia.Headless;

namespace Runesmith.Languages.Tests;

/// <summary>The one headless Avalonia session of the test project, for what posts to the UI thread; Avalonia starts only once per process.</summary>
internal static class HeadlessSession
{
    private static readonly Lazy<HeadlessUnitTestSession> Session = new(() => HeadlessUnitTestSession.StartNew(typeof(TestApp)));

    public static HeadlessUnitTestSession Value => Session.Value;

    private sealed class TestApp : Application
    {
        public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
    }
}
