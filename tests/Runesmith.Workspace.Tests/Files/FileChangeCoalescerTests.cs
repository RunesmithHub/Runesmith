using Runesmith.Sdk.Workspace;
using Runesmith.Workspace.Files;

namespace Runesmith.Workspace.Tests.Files;

public sealed class FileChangeCoalescerTests
{
    private static readonly string A = Path.Combine(Path.GetTempPath(), "w", "a.txt");
    private static readonly string B = Path.Combine(Path.GetTempPath(), "w", "b.txt");
    private static readonly string C = Path.Combine(Path.GetTempPath(), "w", "c.txt");
    private static readonly string Moved = Path.Combine(Path.GetTempPath(), "w", "sub", "a.txt");

    [Fact]
    public void AFileCreatedAndThenChangedIsCreated() =>
        Assert.Equal([new FileChangeEvent(FileChangeKind.Created, A)], Merge(Created(A), Changed(A), Changed(A)));

    [Fact]
    public void AFileCreatedAndThenDeletedIsNotReported() => Assert.Empty(Merge(Created(A), Changed(A), Deleted(A)));

    [Fact]
    public void AFileDeletedAndCreatedAgainIsChanged() =>
        Assert.Equal([new FileChangeEvent(FileChangeKind.Changed, A)], Merge(Deleted(A), Created(A)));

    [Fact]
    public void RenamesInARowAreOneRename() =>
        Assert.Equal([new FileChangeEvent(FileChangeKind.Renamed, C) { OldPath = A }], Merge(Renamed(A, B), Renamed(B, C)));

    [Fact]
    public void ARenameAndItsWayBackLeaveOnlyAChangeInBetween()
    {
        Assert.Empty(Merge(Renamed(A, B), Renamed(B, A)));
        Assert.Equal([new FileChangeEvent(FileChangeKind.Changed, A)], Merge(Renamed(A, B), Changed(B), Renamed(B, A)));
    }

    [Fact]
    public void AChangeAfterARenameIsReportedAfterIt() =>
        Assert.Equal([new FileChangeEvent(FileChangeKind.Renamed, B) { OldPath = A }, new FileChangeEvent(FileChangeKind.Changed, B)],
            Merge(Renamed(A, B), Changed(B)));

    [Fact]
    public void ARenamedFileThatIsDeletedIsDeletedUnderItsOldPath() =>
        Assert.Equal([new FileChangeEvent(FileChangeKind.Deleted, A)], Merge(Renamed(A, B), Deleted(B)));

    [Fact]
    public void SavingThroughATemporaryFileIsAChangeOfTheFile()
    {
        var temporary = A + ".tmp";

        Assert.Equal([new FileChangeEvent(FileChangeKind.Changed, A)], Merge(Created(temporary), Changed(temporary), Deleted(A), Renamed(temporary, A)));
    }

    [Fact]
    public void ADeletionAndACreationOfTheSameNameInAnotherFolderAreAMove() =>
        Assert.Equal([new FileChangeEvent(FileChangeKind.Renamed, Moved) { OldPath = A }], Merge(Deleted(A), Created(Moved)));

    [Fact]
    public void DeletionsAndCreationsThatCannotBePairedStayApart()
    {
        var other = Path.Combine(Path.GetTempPath(), "w", "other", "a.txt");

        var events = Merge(Deleted(A), Created(Moved), Created(other));

        Assert.Equal([FileChangeKind.Deleted, FileChangeKind.Created, FileChangeKind.Created], events.Select(e => e.Kind));
    }

    private static List<FileChangeEvent> Merge(params FileChange[] changes)
    {
        var coalescer = new FileChangeCoalescer();
        foreach (var change in changes)
            coalescer.Add(change);
        var events = coalescer.Drain();
        Assert.True(coalescer.IsEmpty);
        return events;
    }

    private static FileChange Created(string path) => new(WatcherChangeTypes.Created, path);

    private static FileChange Changed(string path) => new(WatcherChangeTypes.Changed, path);

    private static FileChange Deleted(string path) => new(WatcherChangeTypes.Deleted, path);

    private static FileChange Renamed(string from, string to) => new(WatcherChangeTypes.Renamed, to, from);
}
