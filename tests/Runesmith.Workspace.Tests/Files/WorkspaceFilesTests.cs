using System.Threading.Channels;
using Runesmith.Composition;
using Runesmith.Sdk.Workspace;
using Runesmith.Workspace.Files;
using Runesmith.Workspace.Settings;

namespace Runesmith.Workspace.Tests.Files;

public sealed class WorkspaceFilesTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    [ThreadStatic]
    private static bool reporting;

    private readonly TestWorkspace workspace = new();
    private readonly List<string> log = [];
    private readonly string outside = Path.TrimEndingDirectorySeparator(Directory.CreateTempSubdirectory("runesmith-outside-").FullName);
    private PluginInfo? caller;
    private WorkspaceFiles? files;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private WorkspaceFiles Files => files ??= new WorkspaceFiles(workspace.Workspace, workspace.Settings, () => caller, line =>
    {
        lock (log)
            log.Add(line);
    });

    public void Dispose()
    {
        files?.Dispose();
        workspace.Dispose();
        Directory.Delete(outside, recursive: true);
    }

    [Fact]
    public async Task ReportsTheChangesThatMatchTheGlobsMergedIntoOneBatch()
    {
        await workspace.Workspace.OpenAsync(workspace.Root);
        var batches = new Batches();
        using var watcher = Files.Watch(["**/*.cs"], batches.Add);
        var program = Path.Combine(workspace.Root, "src", "Program.cs");

        Files.Report(workspace.Root, [Created(program), Changed(program), Created(Path.Combine(workspace.Root, "notes.md"))], overflowed: false);

        Assert.Equal([new FileChangeEvent(FileChangeKind.Created, program)], await batches.NextAsync());
        Assert.True(watcher.IsWatching);
        Assert.Equal(workspace.Root, watcher.Folder);
    }

    [Fact]
    public async Task LeavesOutTheExcludedFoldersUnlessAskedAndTheExtraExcludes()
    {
        await workspace.Workspace.OpenAsync(workspace.Root);
        var plain = new Batches();
        var everything = new Batches();
        using var first = Files.Watch([], plain.Add, new FileWatchOptions { Exclude = ["*.log"] });
        using var second = Files.Watch([], everything.Add, new FileWatchOptions { IncludeExcluded = true });
        var output = Path.Combine(workspace.Root, "bin", "Debug", "App.dll");
        var buildLog = Path.Combine(workspace.Root, "build.log");
        var source = Path.Combine(workspace.Root, "App.cs");

        Files.Report(workspace.Root, [Created(output), Created(Path.Combine(workspace.Root, ".git", "index")), Created(buildLog), Changed(source)], overflowed: false);

        Assert.Equal([new FileChangeEvent(FileChangeKind.Changed, source)], await plain.NextAsync());
        Assert.Equal([output, Path.Combine(workspace.Root, ".git", "index"), buildLog, source], (await everything.NextAsync()).Select(e => e.Path));
    }

    [Fact]
    public async Task ARenameThatLeavesOrEntersTheGlobsIsADeletionOrACreation()
    {
        await workspace.Workspace.OpenAsync(workspace.Root);
        var batches = new Batches();
        using var watcher = Files.Watch(["*.cs"], batches.Add);
        var code = Path.Combine(workspace.Root, "A.cs");
        var text = Path.Combine(workspace.Root, "A.txt");
        var other = Path.Combine(workspace.Root, "B.cs");

        Files.Report(workspace.Root, [Renamed(code, text)], overflowed: false);
        Assert.Equal([new FileChangeEvent(FileChangeKind.Deleted, code)], await batches.NextAsync());

        Files.Report(workspace.Root, [Renamed(text, code)], overflowed: false);
        Assert.Equal([new FileChangeEvent(FileChangeKind.Created, code)], await batches.NextAsync());

        Files.Report(workspace.Root, [Renamed(code, other)], overflowed: false);
        Assert.Equal([new FileChangeEvent(FileChangeKind.Renamed, other) { OldPath = code }], await batches.NextAsync());
    }

    [Fact]
    public async Task LostChangesAreReportedAsOneRescanOfTheWatchedFolder()
    {
        await workspace.Workspace.OpenAsync(workspace.Root);
        var batches = new Batches();
        var src = Directory.CreateDirectory(Path.Combine(workspace.Root, "src")).FullName;
        using var watcher = Files.Watch([], batches.Add, new FileWatchOptions { Folder = src, Delay = TimeSpan.FromMilliseconds(300) });

        Files.Report(workspace.Root, [Created(Path.Combine(src, "A.cs"))], overflowed: false);
        Files.Report(workspace.Root, [], overflowed: true);

        Assert.Equal([new FileChangeEvent(FileChangeKind.Rescan, src)], await batches.NextAsync());
    }

    [Fact]
    public async Task ADelayGathersBatchesUntilChangesStop()
    {
        await workspace.Workspace.OpenAsync(workspace.Root);
        var batches = new Batches();
        using var watcher = Files.Watch([], batches.Add, new FileWatchOptions { Delay = TimeSpan.FromMilliseconds(200) });
        var file = Path.Combine(workspace.Root, "A.cs");

        Files.Report(workspace.Root, [Created(file)], overflowed: false);
        Files.Report(workspace.Root, [Changed(file)], overflowed: false);
        Files.Report(workspace.Root, [Renamed(file, file + ".bak")], overflowed: false);

        Assert.Equal([new FileChangeEvent(FileChangeKind.Created, file + ".bak")], await batches.NextAsync());
        Assert.False(await batches.AnyWithinAsync(TimeSpan.FromMilliseconds(400)));
    }

    [Fact]
    public async Task DeliversOffTheCallingThreadOneBatchAtATimeAndSurvivesAHandlerThatThrows()
    {
        await workspace.Workspace.OpenAsync(workspace.Root);
        var batches = new Batches();
        var calls = 0;
        var inside = 0;
        var overlapped = false;
        using var watcher = Files.Watch([], changes =>
        {
            overlapped |= Interlocked.Increment(ref inside) > 1;
            Thread.Sleep(20);
            Interlocked.Decrement(ref inside);
            if (Interlocked.Increment(ref calls) == 1)
                throw new InvalidOperationException("Broken handler");
            batches.Add(changes);
        });

        for (var i = 0; i < 5; i++)
        {
            reporting = true;
            Files.Report(workspace.Root, [Created(Path.Combine(workspace.Root, $"{i}.txt"))], overflowed: false);
            reporting = false;
        }

        for (var i = 1; i < 5; i++)
            Assert.Equal(Path.Combine(workspace.Root, $"{i}.txt"), Assert.Single(await batches.NextAsync()).Path);
        Assert.False(overlapped);
        Assert.Contains(log, line => line.Contains("Broken handler", StringComparison.Ordinal));
        Assert.All(batches.Inline, Assert.False);
    }

    [Fact]
    public async Task StopsReportingWhenDisposed()
    {
        await workspace.Workspace.OpenAsync(workspace.Root);
        var batches = new Batches();
        var watcher = Files.Watch([], batches.Add);

        watcher.Dispose();
        Files.Report(workspace.Root, [Created(Path.Combine(workspace.Root, "A.cs"))], overflowed: false);

        Assert.False(watcher.IsWatching);
        Assert.False(await batches.AnyWithinAsync(TimeSpan.FromMilliseconds(200)));
    }

    [Fact]
    public async Task FollowsRealChangesOnDiskWithRenamesAndTheFilesOfAFolder()
    {
        var old = workspace.Write(Path.Combine("src", "Old.cs"), "class Old {}");
        workspace.Write(Path.Combine("lib", "One.cs"));
        workspace.Write(Path.Combine("lib", "Two.cs"));
        await workspace.Workspace.OpenAsync(workspace.Root);
        await workspace.Workspace.GetFilesAsync(Token);
        var events = new Batches();
        using var watcher = Files.Watch(["**/*.cs"], events.Add);
        var renamed = Path.Combine(workspace.Root, "src", "New.cs");

        File.Move(old, renamed);
        var rename = await events.UntilAsync(e => e.Kind == FileChangeKind.Renamed);
        Assert.Equal(old, rename.OldPath);
        Assert.Equal(renamed, rename.Path);

        Directory.Move(Path.Combine(workspace.Root, "lib"), Path.Combine(workspace.Root, "core"));
        var moved = await events.UntilAsync(e => e.Path == Path.Combine(workspace.Root, "core", "Two.cs"));
        Assert.Equal(Path.Combine(workspace.Root, "lib", "Two.cs"), moved.OldPath);
    }

    [Fact]
    public async Task AFolderThatIsDeletedOrRenamedComesWithTheFilesTheListKnewInsideItAsTheBatchLeftThem()
    {
        var order = workspace.Write(Path.Combine("Billing", "Order.cs"));
        var invoice = workspace.Write(Path.Combine("Billing", "Invoice.cs"));
        await workspace.Workspace.OpenAsync(workspace.Root);
        var index = new FileIndex(workspace.Root, GlobMatcher.Parse(CoreSettings.DefaultExclude));
        await index.GetFilesAsync(Token);
        var billing = Path.Combine(workspace.Root, "Billing");
        var moved = Path.Combine(workspace.Root, "Models", "Order.cs");

        var expanded = Runesmith.Workspace.Files.Workspace.Expand(index,
            [new FileChange(WatcherChangeTypes.Renamed, moved, order), new FileChange(WatcherChangeTypes.Deleted, billing)]);

        Assert.Equal(
            [
                new FileChange(WatcherChangeTypes.Renamed, moved, order),
                new FileChange(WatcherChangeTypes.Deleted, billing),
                new FileChange(WatcherChangeTypes.Deleted, invoice),
            ],
            expanded);
    }

    [Fact]
    public async Task WatchingAFolderOutsideTheOpenFolderNeedsTheFilesystemCapability()
    {
        await workspace.Workspace.OpenAsync(workspace.Root);
        caller = TestPlugins.Plugin("acme.watch", "network");

        var exception = Assert.Throws<UnauthorizedAccessException>(() => Files.Watch([], _ => { }, new FileWatchOptions { Folder = outside }));

        Assert.Contains("filesystem", exception.Message, StringComparison.Ordinal);
        Assert.Equal(exception.Message, Assert.Single(log));
        using var inside = Files.Watch([], _ => { }, new FileWatchOptions { Folder = Directory.CreateDirectory(Path.Combine(workspace.Root, "src")).FullName });
        Assert.True(inside.IsWatching);
    }

    [Fact]
    public async Task APluginWithTheCapabilityWatchesAnotherFolderThroughOneSharedWatcher()
    {
        await workspace.Workspace.OpenAsync(workspace.Root);
        caller = TestPlugins.Plugin("acme.watch", "filesystem");
        var first = new Batches();
        var second = new Batches();
        using var one = Files.Watch([], first.Add, new FileWatchOptions { Folder = outside });
        using var two = Files.Watch(["*.json"], second.Add, new FileWatchOptions { Folder = outside });
        var settings = Path.Combine(outside, "settings.json");

        Files.Report(outside, [Created(settings), Created(Path.Combine(outside, "node_modules", "x.json"))], overflowed: false);

        Assert.Equal([new FileChangeEvent(FileChangeKind.Created, settings)], await first.NextAsync());
        Assert.Equal([new FileChangeEvent(FileChangeKind.Created, settings)], await second.NextAsync());
        Assert.Equal(1, Files.SharedWatcherCount);
        Assert.Equal(outside, one.Folder);
    }

    [Fact]
    public async Task AWatcherWithoutTheCapabilityStopsWhenItsFolderIsNoLongerInsideTheOpenFolder()
    {
        var src = Directory.CreateDirectory(Path.Combine(workspace.Root, "src")).FullName;
        await workspace.Workspace.OpenAsync(workspace.Root);
        caller = TestPlugins.Plugin("acme.watch");
        using var watcher = Files.Watch([], _ => { }, new FileWatchOptions { Folder = src });
        using var following = Files.Watch([], _ => { });
        Assert.True(watcher.IsWatching);

        await workspace.Workspace.OpenAsync(outside);

        Assert.False(watcher.IsWatching);
        Assert.Equal(outside, following.Folder);
        Assert.True(following.IsWatching);
        Assert.Equal(0, Files.SharedWatcherCount);
    }

    private static FileChange Created(string path) => new(WatcherChangeTypes.Created, path);

    private static FileChange Changed(string path) => new(WatcherChangeTypes.Changed, path);

    private static FileChange Renamed(string from, string to) => new(WatcherChangeTypes.Renamed, to, from);

    private sealed class Batches
    {
        private readonly Channel<IReadOnlyList<FileChangeEvent>> channel = Channel.CreateUnbounded<IReadOnlyList<FileChangeEvent>>();

        public List<bool> Inline { get; } = [];

        public void Add(IReadOnlyList<FileChangeEvent> batch)
        {
            lock (Inline)
                Inline.Add(reporting);
            channel.Writer.TryWrite(batch);
        }

        public async Task<IReadOnlyList<FileChangeEvent>> NextAsync() => await channel.Reader.ReadAsync(Token).AsTask().WaitAsync(Wait, Token);

        public async Task<bool> AnyWithinAsync(TimeSpan time)
        {
            await Task.Delay(time, Token);
            return channel.Reader.TryPeek(out _);
        }

        public async Task<FileChangeEvent> UntilAsync(Func<FileChangeEvent, bool> predicate)
        {
            while (true)
            {
                foreach (var change in await NextAsync())
                {
                    if (predicate(change))
                        return change;
                }
            }
        }
    }
}
