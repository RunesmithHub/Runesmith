using System.Text;

namespace Runesmith.App.Tests;

public sealed class CommandLineTests
{
    [Fact]
    public void ALinkIsKeptApartFromThePaths()
    {
        var command = CommandLine.Parse(["src/Program.cs:12", "runesmith://hub/plugin/lumen.todo?version=1.0.0"]);

        Assert.Equal(["src/Program.cs:12"], command.Paths);
        Assert.Equal("runesmith://hub/plugin/lumen.todo?version=1.0.0", command.Link);
    }

    [Fact]
    public void FilesFromTheMenuEntryArriveAsAddressesAndOpenAsPaths()
    {
        var path = Path.Combine(Path.GetTempPath(), "notes.txt");

        var command = CommandLine.Parse([new Uri(path).AbsoluteUri]);

        Assert.Equal([path], command.Paths);
        Assert.Null(command.Link);
    }

    [Fact]
    public void SafeModeLeavesOutTheUsersPlugins()
    {
        var command = CommandLine.Parse(["--safe-mode"]);

        Assert.True(command.IsSafeMode);
        Assert.True(command.ArePluginsDisabled);
        Assert.Null(command.Error);
    }

    [Fact]
    public void AfterTwoDashesEverythingIsAPath() =>
        Assert.Equal(["runesmith://hub/updates"], CommandLine.Parse(["--", "runesmith://hub/updates"]).Paths);

    [Fact]
    public void TheRunningInstanceReadsOnlyHandoversOfLimitedLength()
    {
        Assert.Equal("runesmith://hub/updates", SingleInstance.Read(Encoding.UTF8.GetBytes("""{"Paths":[],"WorkingDirectory":"/","Link":"runesmith://hub/updates"}"""))?.Link);
        Assert.Null(SingleInstance.Read(Encoding.UTF8.GetBytes("not json")));
        Assert.Null(SingleInstance.Read(Encoding.UTF8.GetBytes("""{"Link":"runesmith://hub/updates"}""")));
        Assert.Null(SingleInstance.Read(new byte[SingleInstance.MaxMessageBytes + 1]));

        var tooLong = SingleInstance.Read(Encoding.UTF8.GetBytes($$"""{"Paths":[],"WorkingDirectory":"/","Link":"runesmith://hub/plugin/{{new string('a', 600)}}"}"""));
        Assert.NotNull(tooLong);
        Assert.Null(tooLong.Link);
    }

    [Fact]
    public void WindowsOpensLinksWithTheExecutable() =>
        Assert.Equal("\"C:\\Runesmith\\runesmith.exe\" \"%1\"", LinkRegistration.Command(@"C:\Runesmith\runesmith.exe"));
}
