using Runesmith.Workspace.Files;

namespace Runesmith.Workspace.Tests.Files;

public sealed class RecentFoldersTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("runesmith-recent-").FullName;

    private string File => Path.Combine(folder, "recent.json");

    public void Dispose() => Directory.Delete(folder, recursive: true);

    [Fact]
    public void RemembersWhenEachFolderWasOpenedNewestFirst()
    {
        var time = new SteppedTime { Now = new DateTimeOffset(2026, 10, 8, 9, 0, 0, TimeSpan.Zero) };
        var recent = new RecentFolders(File, time);

        recent.Add("/work/one");
        time.Now = time.Now.AddHours(1);
        recent.Add("/work/two");

        var reread = new RecentFolders(File, time);
        Assert.Equal([Path.GetFullPath("/work/two"), Path.GetFullPath("/work/one")], reread.Items);
        Assert.Equal(time.GetUtcNow(), reread.Entries[0].Opened);
        Assert.Equal(time.GetUtcNow().AddHours(-1), reread.Entries[1].Opened);
    }

    [Fact]
    public void ReadsTheListOfPathsOlderVersionsWrote()
    {
        System.IO.File.WriteAllText(File, """["/work/one", "/work/two"]""");

        var recent = new RecentFolders(File, TimeProvider.System);

        Assert.Equal(["/work/one", "/work/two"], recent.Items);
        Assert.All(recent.Entries, e => Assert.Null(e.Opened));
    }

    private sealed class SteppedTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; }

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
