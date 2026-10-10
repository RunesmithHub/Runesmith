using Runesmith.Composition;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Languages;
using Runesmith.Sdk.Messaging;
using Runesmith.Shell.Editors;
using Runesmith.Shell.Editors.FileOperations;
using Runesmith.Shell.Tests.Plugins;
using Runesmith.Shell.Tests.Running;
using Runesmith.Text;
using Runesmith.Workspace.Documents;

namespace Runesmith.Shell.Tests.Editors;

public sealed class FileOperationsTests : IDisposable
{
    private readonly string root = Path.TrimEndingDirectorySeparator(Directory.CreateTempSubdirectory("runesmith-file-operations-").FullName);
    private readonly string scratch = Directory.CreateTempSubdirectory("runesmith-file-operations-scratch-").FullName;
    private readonly FakeNotifications notifications = new();
    private readonly MessageBus messages = new();
    private readonly List<FilesChangedMessage> changed = [];
    private readonly List<string> log = [];
    private readonly List<IFileOperationParticipant> participants = [];
    private DocumentService? documents;
    private OpeningEditors? editors;
    private PluginInfo? caller;

    public FileOperationsTests() => messages.Subscribe<FilesChangedMessage>(changed.Add);

    private string Trash => Path.Combine(scratch, "trash");

    private string Stash => Path.Combine(scratch, "stash");

    public void Dispose()
    {
        documents?.Dispose();
        TestFolders.Delete(root);
        TestFolders.Delete(scratch);
    }

    [Fact]
    public Task CreatesRenamesAndDeletesFilesWithTheirTextAsOneStepThatUndoesAndRedoes() => Run(async () =>
    {
        var service = Create();
        var other = Write("A.cs", "class A { Old o; }");
        var old = Write("Old.cs", "class Old {}");
        var gone = Write("Gone.txt", "bye");
        var created = Path.Combine(root, "New", "C.cs");
        var renamed = Path.Combine(root, "Models", "Renamed.cs");
        var opened = await documents!.OpenAsync(old);
        var edit = new WorkspaceEdit(
        [
            new DocumentEdit(old, [Replace(6, 3, "Renamed")]),
            new DocumentEdit(other, [Replace(10, 3, "Renamed")]),
            new DocumentEdit(created, [Insert(6, "C")]),
        ])
        {
            FileOperations =
            [
                new CreateFileOperation(created) { Content = "class \n{\n}\n" },
                new RenameFileOperation(old, renamed),
                new DeleteFileOperation(gone),
            ],
        };

        Assert.True(await service.ApplyAsync(edit, new WorkspaceEditOptions { Confirmation = WorkspaceEditConfirmation.Never }));

        Assert.Equal(renamed, opened.FilePath);
        Assert.Equal("class Renamed {}", opened.Buffer.Current.GetText());
        Assert.False(File.Exists(old));
        Assert.Equal("class Old {}", File.ReadAllText(renamed));
        Assert.Equal("class C\n{\n}\n", documents.Find(created)!.Buffer.Current.GetText());
        Assert.Equal("class A { Renamed o; }", documents.Find(other)!.Buffer.Current.GetText());
        Assert.False(File.Exists(gone));
        Assert.Equal("bye", File.ReadAllText(Trashed("Gone.txt")));
        if (SystemTrash.UsesFreedesktopTrash)
            Assert.Equal(gone, TrashedFrom("Gone.txt"));
        Assert.Contains(renamed, Assert.Single(changed).Paths);
        Assert.Contains("3 file operations", notifications.Notified);
        Assert.True(service.CanUndo);

        Assert.True(await service.UndoAsync());

        Assert.Equal(old, opened.FilePath);
        Assert.Equal("class Old {}", opened.Buffer.Current.GetText());
        Assert.True(File.Exists(old));
        Assert.False(File.Exists(renamed));
        Assert.False(File.Exists(created));
        Assert.Null(documents.Find(created));
        Assert.False(Directory.Exists(Path.Combine(root, "New")));
        Assert.False(Directory.Exists(Path.Combine(root, "Models")));
        Assert.Equal("class A { Old o; }", documents.Find(other)!.Buffer.Current.GetText());
        Assert.Equal("bye", File.ReadAllText(gone));
        Assert.False(File.Exists(Trashed("Gone.txt")));
        Assert.False(File.Exists(Path.Combine(Trash, "info", "Gone.txt.trashinfo")));
        Assert.True(service.CanRedo);

        Assert.True(await service.RedoAsync());

        Assert.Equal(renamed, opened.FilePath);
        Assert.Equal("class Renamed {}", opened.Buffer.Current.GetText());
        Assert.Equal("class A { Renamed o; }", documents.Find(other)!.Buffer.Current.GetText());
        Assert.Equal("class C\n{\n}\n", documents.Find(created)!.Buffer.Current.GetText());
        Assert.False(File.Exists(gone));
    });

    [Fact]
    public Task NothingChangesWhenAnyOperationOrTextChangeCannotBeMade() => Run(async () =>
    {
        var service = Create();
        var existing = Write("Existing.cs", "x");
        var created = Path.Combine(root, "Created.cs");

        Assert.False(await service.ApplyAsync(Edit(new CreateFileOperation(created), new RenameFileOperation(Path.Combine(root, "Missing.cs"), Path.Combine(root, "B.cs")))));
        Assert.False(await service.ApplyAsync(Edit(new CreateFileOperation(created), new CreateFileOperation(existing))));
        Assert.False(await service.ApplyAsync(Edit(new DeleteFileOperation(Path.Combine(root, "Missing.cs")))));
        var opened = await documents!.OpenAsync(existing);
        var stale = opened.Buffer.Current;
        opened.Buffer.Insert(0, "y");
        Assert.False(await service.ApplyAsync(new WorkspaceEdit([new DocumentEdit(existing, [Insert(0, "z")]) { Snapshot = stale }])
        {
            FileOperations = [new CreateFileOperation(created)],
        }));

        Assert.False(File.Exists(created));
        Assert.Equal("yx", opened.Buffer.Current.GetText());
        Assert.False(service.CanUndo);
        Assert.Equal(4, notifications.Notified.Count(n => n == "The edit was not applied"));
    });

    [Fact]
    public Task TheOptionsSkipOrReplaceWhatExists() => Run(async () =>
    {
        var service = Create();
        var existing = Write("Existing.cs", "old");
        var target = Write("Target.cs", "target");
        var folder = Directory.CreateDirectory(Path.Combine(root, "Folder")).FullName;
        Write(Path.Combine("Folder", "Inside.cs"), "inside");

        Assert.True(await service.ApplyAsync(Edit(
            new CreateFileOperation(existing) { Content = "ignored", IgnoreIfExists = true },
            new DeleteFileOperation(Path.Combine(root, "Missing.cs")) { IgnoreIfMissing = true })));
        Assert.Equal("old", File.ReadAllText(existing));

        Assert.False(await service.ApplyAsync(Edit(new DeleteFileOperation(folder))));
        Assert.True(await service.ApplyAsync(Edit(
            new CreateFileOperation(existing) { Content = "new", Overwrite = true },
            new RenameFileOperation(existing, target) { Overwrite = true },
            new DeleteFileOperation(folder) { Recursive = true, UseTrash = false })));

        Assert.Equal("new", File.ReadAllText(target));
        Assert.False(Directory.Exists(folder));
        Assert.False(Directory.Exists(Trash) && Directory.EnumerateFiles(Trash, "*", SearchOption.AllDirectories).Any());

        Assert.True(await service.UndoAsync());

        Assert.Equal("old", File.ReadAllText(existing));
        Assert.Equal("target", File.ReadAllText(target));
        Assert.Equal("inside", File.ReadAllText(Path.Combine(folder, "Inside.cs")));
    });

    [Fact]
    public Task TakesBackTheOperationsMadeWhenTheDiskRefusesOneHalfway() => Run(async () =>
    {
        var service = Create();
        var a = Write("A.cs", "a");
        var blocker = Write("blocker", "a file where a folder is needed");

        Assert.False(await service.ApplyAsync(Edit(new RenameFileOperation(a, Path.Combine(root, "B.cs")), new CreateFileOperation(Path.Combine(blocker, "C.cs")))));

        Assert.Equal("a", File.ReadAllText(a));
        Assert.False(File.Exists(Path.Combine(root, "B.cs")));
        Assert.False(service.CanUndo);
    });

    [Fact]
    public Task RefusesToDeleteOrReplaceAFileWithUnsavedChanges() => Run(async () =>
    {
        var service = Create();
        var file = Write("A.cs", "a");
        var document = await documents!.OpenAsync(file);
        document.History.Push(document.Buffer.Apply([Insert(0, "unsaved ")]), null, null);

        Assert.False(await service.ApplyAsync(Edit(new DeleteFileOperation(file))));
        Assert.False(await service.ApplyAsync(Edit(new CreateFileOperation(file) { Overwrite = true })));

        Assert.True(File.Exists(file));
        Assert.Contains(documents.Documents, d => d == document);
    });

    [Fact]
    public Task CreatesFoldersThatUndoWhileTheyAreEmpty() => Run(async () =>
    {
        var service = Create();
        var folder = Path.Combine(root, "Models", "Billing");
        var file = Write("File.txt", "a file");

        Assert.True(await service.ApplyAsync(Edit(new CreateFolderOperation(folder))));
        Assert.True(Directory.Exists(folder));
        Assert.Contains("Create Billing", notifications.Notified);
        Assert.True(await service.ApplyAsync(Edit(new CreateFolderOperation(folder) { IgnoreIfExists = true })));
        Assert.False(await service.ApplyAsync(Edit(new CreateFolderOperation(folder))));
        Assert.False(await service.ApplyAsync(Edit(new CreateFolderOperation(file) { IgnoreIfExists = true })));

        Assert.True(await service.UndoAsync());
        Assert.False(Directory.Exists(Path.Combine(root, "Models")));
        Assert.False(service.CanUndo);

        Assert.True(await service.RedoAsync());
        Assert.True(Directory.Exists(folder));
        File.WriteAllText(Path.Combine(folder, "Order.cs"), "class Order {}");
        Assert.False(await service.UndoAsync());
        Assert.True(File.Exists(Path.Combine(folder, "Order.cs")));
        Assert.Contains("The edit was not undone", notifications.Notified);
    });

    [Fact]
    public Task ParticipantsHearOfAnUndoneFolderDeleteAsAFolderCreated() => Run(async () =>
    {
        var participant = new RecordingParticipant();
        participants.Add(participant);
        var service = Create();
        var folder = Path.Combine(root, "Old");
        Write(Path.Combine("Old", "A.cs"), "a");

        Assert.True(await service.ApplyAsync(Edit(new DeleteFileOperation(folder) { Recursive = true })));
        Assert.IsType<DeleteFileOperation>(Assert.Single(await participant.NextDidAsync()));
        Assert.True(await service.UndoAsync());

        Assert.Equal(new CreateFolderOperation(folder), Assert.Single(await participant.NextDidAsync()));
        Assert.Equal("a", File.ReadAllText(Path.Combine(folder, "A.cs")));
    });

    [Fact]
    public Task ClosesTheDocumentsOfDeletedFiles() => Run(async () =>
    {
        var service = Create();
        var folder = Path.Combine(root, "Old");
        var file = Write(Path.Combine("Old", "A.cs"), "a");
        var document = await documents!.OpenAsync(file);

        Assert.True(await service.ApplyAsync(Edit(new DeleteFileOperation(folder) { Recursive = true })));

        Assert.DoesNotContain(documents.Documents, d => d == document);
        Assert.Contains(document, editors!.Closed);
    });

    [Fact]
    public Task AsksFirstWhenTheEditIsLargeOrWhenToldTo() => Run(async () =>
    {
        var service = Create();
        notifications.Confirm = false;
        var folder = Path.Combine(root, "Many");
        for (var i = 0; i < 3; i++)
            Write(Path.Combine("Many", $"{i}.cs"), "x");

        Assert.False(await service.ApplyAsync(Edit(new RenameFileOperation(folder, Path.Combine(root, "Lots"))), new WorkspaceEditOptions { LargeEditThreshold = 2 }));
        Assert.True(Directory.Exists(folder));
        Assert.Equal(1, notifications.Confirmations);

        Assert.True(await service.ApplyAsync(Edit(new RenameFileOperation(folder, Path.Combine(root, "Lots"))), new WorkspaceEditOptions { LargeEditThreshold = 3 }));
        Assert.Equal(1, notifications.Confirmations);

        notifications.Confirm = true;
        Assert.True(await service.ApplyAsync(Edit(new CreateFileOperation(Path.Combine(root, "One.cs"))), new WorkspaceEditOptions { Confirmation = WorkspaceEditConfirmation.Always }));
        Assert.Equal(2, notifications.Confirmations);
    });

    [Fact]
    public Task APluginNeedsTheFilesystemCapabilityForFilesOutsideTheOpenFolder() => Run(async () =>
    {
        var service = Create();
        var outside = Path.Combine(scratch, "outside.txt");
        caller = TestCallers.Plugin("acme.files", "network");

        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ApplyAsync(Edit(new CreateFileOperation(outside)), new WorkspaceEditOptions(), TestContext.Current.CancellationToken));

        Assert.Contains("filesystem", exception.Message, StringComparison.Ordinal);
        Assert.Equal(exception.Message, Assert.Single(log));
        Assert.False(File.Exists(outside));
        Assert.True(await service.ApplyAsync(Edit(new CreateFileOperation(Path.Combine(root, "inside.txt"))), new WorkspaceEditOptions()));

        caller = TestCallers.Plugin("acme.files", "filesystem");
        Assert.True(await service.ApplyAsync(Edit(new CreateFileOperation(outside)), new WorkspaceEditOptions()));
        Assert.True(File.Exists(outside));
    });

    [Fact]
    public Task ParticipantsAddTextChangesToTheStepAndHearOfWhatWasDoneAndUndone() => Run(async () =>
    {
        var file = Write(Path.Combine("Models", "Order.cs"), "namespace App.Models;\nclass Order {}\n");
        var moved = Path.Combine(root, "Billing", "Order.cs");
        var namespaces = new RecordingParticipant
        {
            Will = operations => operations is [RenameFileOperation rename]
                ? new WorkspaceEdit([new DocumentEdit(rename.NewPath, [Replace(14, 6, "Billing")])])
                : null,
        };
        participants.Add(namespaces);
        var service = Create();

        Assert.True(await service.ApplyAsync(Edit(new RenameFileOperation(file, moved))));

        Assert.Equal("namespace App.Billing;\nclass Order {}\n", documents!.Find(moved)!.Buffer.Current.GetText());
        Assert.IsType<RenameFileOperation>(Assert.Single(await namespaces.NextDidAsync()));

        Assert.True(await service.UndoAsync());

        Assert.Equal("namespace App.Models;\nclass Order {}\n", documents.Find(file)!.Buffer.Current.GetText());
        Assert.Equal(new RenameFileOperation(moved, file), Assert.Single(await namespaces.NextDidAsync()));
    });

    [Fact]
    public Task ParticipantsThatAreTooSlowOrFailAreSkippedAndReported() => Run(async () =>
    {
        participants.Add(new RecordingParticipant { Will = _ => throw new InvalidOperationException("Broken participant") });
        participants.Add(new RecordingParticipant { Delay = TimeSpan.FromSeconds(30), Will = _ => new WorkspaceEdit([new DocumentEdit(Path.Combine(root, "A.cs"), [Insert(0, "late")])]) });
        var service = Create(budget: TimeSpan.FromMilliseconds(200));
        var file = Write("A.cs", "a");
        var started = DateTime.UtcNow;

        Assert.True(await service.ApplyAsync(Edit(new RenameFileOperation(file, Path.Combine(root, "B.cs")))));

        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(10));
        Assert.Equal("a", File.ReadAllText(Path.Combine(root, "B.cs")));
        Assert.Contains(log, line => line.Contains("Broken participant", StringComparison.Ordinal));
        Assert.Contains(log, line => line.Contains("took longer than 0.2 seconds", StringComparison.Ordinal));
    });

    [Fact]
    public Task ParticipantChangesThatDoNotFitTheEditAreLeftOutAndTheEditIsMade() => Run(async () =>
    {
        var file = Write("A.cs", "class A {}");
        var gone = Write("Gone.cs", "class Gone {}");
        var renamed = Path.Combine(root, "B.cs");
        participants.Add(new RecordingParticipant
        {
            Will = _ => new WorkspaceEdit(
            [
                new DocumentEdit(file, [Replace(6, 1, "B")]),
                new DocumentEdit(renamed, [Insert(0, "// moved\n")]),
                new DocumentEdit(gone, [Insert(0, "// gone\n")]),
            ]),
        });
        var service = Create();

        Assert.True(await service.ApplyAsync(new WorkspaceEdit([new DocumentEdit(file, [Replace(6, 1, "B")])])
        {
            FileOperations = [new RenameFileOperation(file, renamed), new DeleteFileOperation(gone)],
        }));

        Assert.Equal("// moved\nclass B {}", documents!.Find(renamed)!.Buffer.Current.GetText());
        Assert.False(File.Exists(gone));
        Assert.Contains(log, line => line.Contains("changes to B.cs that were left out: B.cs has overlapping changes", StringComparison.Ordinal));
        Assert.Contains(log, line => line.Contains("left out: Gone.cs is deleted by the same edit.", StringComparison.Ordinal));

        Assert.True(await service.UndoAsync());

        Assert.Equal("class A {}", File.ReadAllText(file));
        Assert.Equal("class Gone {}", File.ReadAllText(gone));
    });

    [Fact]
    public Task UndoingInTheEditorTakesBackTheWholeEditWhenItWasTheLastChange() => Run(async () =>
    {
        var service = Create();
        var file = Write("A.cs", "class A {}");
        var document = await documents!.OpenAsync(file);
        var renamed = Path.Combine(root, "B.cs");

        Assert.True(await service.ApplyAsync(new WorkspaceEdit([new DocumentEdit(file, [Replace(6, 1, "B")])])
        {
            FileOperations = [new RenameFileOperation(file, renamed)],
        }));
        Assert.True(service.TryUndoWith(document));
        await WaitUntil(() => !service.CanUndo);

        Assert.Equal(file, document.FilePath);
        Assert.Equal("class A {}", document.Buffer.Current.GetText());
        Assert.True(service.TryRedoWith(document));
        await WaitUntil(() => service.CanUndo);
        Assert.Equal(renamed, document.FilePath);

        document.Buffer.Insert(0, "// typed\n");
        Assert.False(service.TryUndoWith(document));
    });

    [Fact]
    public Task UndoIsRefusedWhenAFileChangedSince() => Run(async () =>
    {
        var service = Create();
        var file = Write("A.cs", "a");
        var renamed = Path.Combine(root, "B.cs");
        Assert.True(await service.ApplyAsync(Edit(new RenameFileOperation(file, renamed))));
        File.WriteAllText(file, "a new file where the old one was");

        Assert.False(await service.UndoAsync());

        Assert.True(File.Exists(renamed));
        Assert.Contains("The edit was not undone", notifications.Notified);
    });

    [Fact]
    public Task FilesKeptForUndoAreDeletedForGoodWhenTheHistoryIsDisposed() => Run(async () =>
    {
        var service = Create();
        Write("A.cs", "a");

        Assert.True(await service.ApplyAsync(Edit(new DeleteFileOperation(Path.Combine(root, "A.cs")) { UseTrash = false })));
        Assert.Single(Directory.EnumerateFiles(Stash, "*", SearchOption.AllDirectories));

        service.Dispose();

        Assert.Empty(Directory.EnumerateFiles(Stash, "*", SearchOption.AllDirectories));
        Assert.False(Directory.Exists(Trash) && Directory.EnumerateFiles(Trash, "*", SearchOption.AllDirectories).Any());
    });

    [Fact]
    public void TheDesktopTrashRecordsWhereEachFileCameFromAndPicksFreeNames()
    {
        Assert.SkipUnless(SystemTrash.UsesFreedesktopTrash, "Runesmith uses the freedesktop trash only on Linux and the BSDs.");
        var trash = new FreedesktopTrash(Trash);
        var first = Write("Same name.txt", "1");

        var one = trash.TryMoveToTrash(first);
        Write("Same name.txt", "2");
        var two = trash.TryMoveToTrash(first);

        Assert.Equal(Path.Combine(Trash, "files", "Same name.txt"), one);
        Assert.Equal(Path.Combine(Trash, "files", "Same name.2.txt"), two);
        Assert.Contains("Same%20name.txt", File.ReadAllText(Path.Combine(Trash, "info", "Same name.2.txt.trashinfo")), StringComparison.Ordinal);
        Assert.Equal(first, TrashedFrom("Same name.2.txt"));

        trash.Restore(two!, first);
        Assert.Equal("2", File.ReadAllText(first));
        Assert.False(File.Exists(Path.Combine(Trash, "info", "Same name.2.txt.trashinfo")));
    }

    private WorkspaceEditService Create(TimeSpan? budget = null)
    {
        documents = new DocumentService(new LineFeedSettings(), new PlainTextLanguages());
        editors = new OpeningEditors(documents);
        return new WorkspaceEditService(documents, new Lazy<IEditorService>(() => editors), notifications, new FakeWorkspace(root), messages,
            [.. participants.Select(p => new Lazy<IFileOperationParticipant>(() => p))], documents, () => caller, log.Add,
            new Lazy<FileStash>(() => new FileStash(Stash, SystemTrash.UsesFreedesktopTrash ? new FreedesktopTrash(Trash) : new FolderTrash(Trash))))
        {
            ParticipantBudget = budget ?? TimeSpan.FromSeconds(5),
        };
    }

    // Where the tests' trash keeps a deleted file: the freedesktop trash where the system uses it, and a plain folder in place of the others.
    private string Trashed(string name) => SystemTrash.UsesFreedesktopTrash ? Path.Combine(Trash, "files", name) : Path.Combine(Trash, name);

    // Reads where a file in the freedesktop trash came from, which its note percent-encodes.
    private string TrashedFrom(string name)
    {
        var note = File.ReadAllLines(Path.Combine(Trash, "info", name + ".trashinfo"));
        return Uri.UnescapeDataString(note.Single(line => line.StartsWith("Path=", StringComparison.Ordinal))["Path=".Length..]);
    }

    private string Write(string relative, string text)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    private static WorkspaceEdit Edit(params FileOperation[] operations) => new([]) { FileOperations = operations };

    private static TextChange Replace(int start, int length, string text) => new(new TextSpan(start, length), text);

    private static TextChange Insert(int start, string text) => new(new TextSpan(start, 0), text);

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
            await Task.Delay(25);
        Assert.True(condition());
    }

    private static Task<bool> Run(Func<Task> test) =>
        HeadlessSession.Value.Dispatch(async () =>
        {
            await test();
            return true;
        }, TestContext.Current.CancellationToken);
}
