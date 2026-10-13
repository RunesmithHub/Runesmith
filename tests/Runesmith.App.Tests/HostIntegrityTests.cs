using Runesmith.Composition;

namespace Runesmith.App.Tests;

public sealed class HostIntegrityTests
{
    [Fact]
    public void ThisBuildOfRunesmithIsComplete() => Assert.Empty(HostIntegrity.Check());

    [Fact]
    public void AMismatchIsReportedWithBothVersions()
    {
        Assert.Equal(
            "Runesmith.Shell was built against Runesmith.Text 0.2.1.0, and this build has Runesmith.Text 0.1.3.0.",
            StartupReports.Describe(new HostMismatch("Runesmith.Text", new Version(0, 1, 3, 0), new Version(0, 2, 1, 0), "Runesmith.Shell")));
        Assert.Equal(
            "Runesmith.Text is 0.1.3.0, and the plugin API is 0.2.1.",
            StartupReports.Describe(new HostMismatch("Runesmith.Text", new Version(0, 1, 3, 0), new Version(0, 2, 1), null)));
    }
}
