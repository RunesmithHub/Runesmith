using System.Runtime.InteropServices;
using System.Text;
using Runesmith.Shell.Editors.FileOperations;

namespace Runesmith.Shell.Tests.Editors;

public sealed class SystemTrashTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("runesmith-trash-").FullName;
    private int recycled;

    private string Bin => Path.Combine(root, "$Recycle.Bin", "S-1-5-21-1");

    public void Dispose() => TestFolders.Delete(root);

    [Fact]
    public void EachSystemKeepsItsOwnTrash()
    {
        var home = Path.Combine(root, "home");
        var data = Path.Combine(root, "data");

        Assert.Equal((TrashKind.RecycleBin, null), SystemTrash.Choose(OSPlatform.Windows, home, data));
        Assert.Equal((TrashKind.MacTrash, Path.Combine(home, ".Trash")), SystemTrash.Choose(OSPlatform.OSX, home, data));
        Assert.Equal((TrashKind.Freedesktop, Path.Combine(home, ".local", "share", "Trash")), SystemTrash.Choose(OSPlatform.Linux, home, null));
        Assert.Equal((TrashKind.Freedesktop, Path.Combine(data, "Trash")), SystemTrash.Choose(OSPlatform.Linux, home, data));
        Assert.Equal((TrashKind.Freedesktop, Path.Combine(home, ".local", "share", "Trash")), SystemTrash.Choose(OSPlatform.FreeBSD, home, "relative"));
        Assert.Equal((TrashKind.None, null), SystemTrash.Choose(OSPlatform.Create("BROWSER"), home, data));
    }

    [Fact]
    public void ThisSystemGetsTheTrashChosenForIt()
    {
        var expected = SystemTrash.Current.Kind switch
        {
            TrashKind.RecycleBin => typeof(RecycleBin),
            TrashKind.MacTrash => typeof(FolderTrash),
            TrashKind.Freedesktop => typeof(FreedesktopTrash),
            _ => typeof(NoTrash),
        };

        Assert.IsType(expected, SystemTrash.Create());
        Assert.Equal(OperatingSystem.IsWindows(), SystemTrash.Current.Kind == TrashKind.RecycleBin);
        Assert.Equal(OperatingSystem.IsLinux() || OperatingSystem.IsFreeBSD(), SystemTrash.UsesFreedesktopTrash);
    }

    [Theory]
    [InlineData(@"C:\Users\me\Order.cs", DriveType.Fixed, true, true)]
    [InlineData("D:/work/Order.cs", DriveType.Fixed, true, true)]
    [InlineData(@"E:\Order.cs", DriveType.Removable, true, false)]
    [InlineData(@"Z:\Order.cs", DriveType.Network, true, false)]
    [InlineData(@"\\server\share\Order.cs", DriveType.Fixed, true, false)]
    [InlineData("Order.cs", DriveType.Fixed, true, false)]
    [InlineData(@"C:\Users\me\Order.cs", DriveType.Fixed, false, false)]
    public void OnlyFilesOnLocalFixedDrivesGoToTheRecycleBin(string path, DriveType type, bool is64BitProcess, bool recyclable) =>
        Assert.Equal(recyclable, RecycleBin.CanRecycle(path, _ => type, is64BitProcess));

    [Fact]
    public void ReadsWhereARecycledItemCameFromInBothVersionsOfItsRecord()
    {
        Assert.Equal(@"C:\Users\me\Old Order.cs", RecycleBin.OriginalPath(Record(2, @"C:\Users\me\Old Order.cs")));
        Assert.Equal(@"C:\Users\me\Old Order.cs", RecycleBin.OriginalPath(Record(1, @"C:\Users\me\Old Order.cs")));
        Assert.Null(RecycleBin.OriginalPath(Record(3, @"C:\Users\me\Old Order.cs")));
        Assert.Null(RecycleBin.OriginalPath([2, 0, 0]));
        Assert.Null(RecycleBin.OriginalPath([.. Record(2, @"C:\a.cs").Take(30)]));
    }

    [Fact]
    public void FindsTheItemItRecycledAmongOlderOnesAndPutsItBack()
    {
        var file = Write("Order.cs", "older");
        var trash = new RecycleBin(_ => true, _ => Bin, Recycle);
        var older = trash.TryMoveToTrash(file);
        Write("Order.cs", "newer");

        var trashed = trash.TryMoveToTrash(file);

        Assert.NotEqual(older, trashed);
        Assert.Equal("newer", File.ReadAllText(trashed!));
        Assert.False(File.Exists(file));

        trash.Restore(trashed!, file);

        Assert.Equal("newer", File.ReadAllText(file));
        Assert.False(File.Exists(trashed));
        Assert.False(File.Exists(Path.Combine(Bin, "$I" + Path.GetFileName(trashed)![2..])));
        Assert.Equal("older", File.ReadAllText(older!));
        Assert.True(File.Exists(Path.Combine(Bin, "$I" + Path.GetFileName(older)![2..])));
    }

    [Fact]
    public void LeavesWhatItCannotRecycleToTheEditAndNoticesWhatWindowsDeletedForGood()
    {
        var file = Write("Order.cs", "order");

        Assert.Null(new RecycleBin(_ => false, _ => Bin, Recycle).TryMoveToTrash(file));
        Assert.Null(new RecycleBin(_ => true, _ => null, Recycle).TryMoveToTrash(file));
        Assert.Null(new RecycleBin(_ => true, _ => Bin, Recycle).TryMoveToTrash(file, Path.Combine(root, "Elsewhere.cs")));
        Assert.Null(new RecycleBin(_ => true, _ => Bin, _ => false).TryMoveToTrash(file));
        Assert.Equal("order", File.ReadAllText(file));
        Assert.Equal(0, recycled);

        var gone = new RecycleBin(_ => true, _ => Bin, path =>
        {
            File.Delete(path);
            return true;
        }).TryMoveToTrash(file);

        Assert.NotNull(gone);
        Assert.False(File.Exists(gone));
    }

    // Recycles as the Windows shell does: the item moves to $R and an id, and its record, with the path it came from, to $I and the same id.
    private bool Recycle(string path)
    {
        Directory.CreateDirectory(Bin);
        var id = $"{++recycled:D6}{Path.GetExtension(path)}";
        File.Move(path, Path.Combine(Bin, "$R" + id));
        File.WriteAllBytes(Path.Combine(Bin, "$I" + id), Record(2, path));
        return true;
    }

    private string Write(string name, string text)
    {
        var path = Path.Combine(root, "work", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    private static byte[] Record(long version, string path)
    {
        var name = Encoding.Unicode.GetBytes(path + '\0');
        var record = new List<byte>();
        record.AddRange(BitConverter.GetBytes(version));
        record.AddRange(BitConverter.GetBytes(5L));
        record.AddRange(BitConverter.GetBytes(new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc).ToFileTimeUtc()));
        if (version == 1)
        {
            var fixedName = new byte[520];
            name.CopyTo(fixedName, 0);
            record.AddRange(fixedName);
        }
        else
        {
            record.AddRange(BitConverter.GetBytes(path.Length + 1));
            record.AddRange(name);
        }

        return [.. record];
    }
}
