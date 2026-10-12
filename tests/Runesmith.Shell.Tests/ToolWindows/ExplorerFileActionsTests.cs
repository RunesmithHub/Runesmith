using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Languages;
using Runesmith.Sdk.Messaging;
using Runesmith.Sdk.Shell;
using Runesmith.Shell.Editors;
using Runesmith.Shell.Editors.FileOperations;
using Runesmith.Shell.Tests.Editors;
using Runesmith.Shell.Tests.Running;
using Runesmith.Shell.ToolWindows;
using Runesmith.Text;
using Runesmith.Workspace.Documents;

namespace Runesmith.Shell.Tests.ToolWindows;

public sealed class ExplorerFileActionsTests : IDisposable
{
    private readonly string root = Path.TrimEndingDirectorySeparator(Directory.CreateTempSubdirectory("runesmith-explorer-").FullName);
    private readonly string stash = Directory.CreateTempSubdirectory("runesmith-explorer-stash-").FullName;
    private readonly FakeNotifications notifications = new();
    private readonly RecordingParticipant participant = new();
    private DocumentService? documents;
    private WorkspaceEditService? edits;

    public void Dispose()
    {
        edits?.Dispose();
        documents?.Dispose();
        TestFolders.Delete(root);
        TestFolders.Delete(stash);
    }

    [Fact]
    public Task EachCommandIsOneStepThatUndoesAndRedoesWithoutANotification() => Run(async () =>
    {
        var explorer = Create();
        var file = Write("A.src", "class A {}");
        var created = Path.Combine(root, "New.src");
        var folder = Path.Combine(root, "Models");
        var renamed = Path.Combine(root, "B.src");

        await explorer.CreateAsync(root, "New.src", folder: false);
        await explorer.CreateAsync(root, "Models", folder: true);
        await explorer.RenameAsync(file, "B.src");
        await explorer.DeleteAsync(renamed, isFolder: false);

        Assert.True(File.Exists(created));
        Assert.NotNull(documents!.Find(created));
        Assert.True(Directory.Exists(folder));
        Assert.False(File.Exists(file) || File.Exists(renamed));
        Assert.Equal(
            [typeof(CreateFileOperation), typeof(CreateFolderOperation), typeof(RenameFileOperation), typeof(DeleteFileOperation)],
            participant.Asked.Select(operations => Assert.Single(operations).GetType()));
        Assert.Empty(notifications.Notified);

        Assert.True(await edits!.UndoAsync());
        Assert.Equal("class A {}", File.ReadAllText(renamed));
        Assert.True(await edits.UndoAsync());
        Assert.Equal("class A {}", File.ReadAllText(file));
        Assert.False(File.Exists(renamed));
        Assert.True(await edits.UndoAsync());
        Assert.False(Directory.Exists(folder));
        Assert.True(await edits.UndoAsync());
        Assert.False(File.Exists(created));
        Assert.False(edits.CanUndo);

        Assert.True(await edits.RedoAsync());
        Assert.True(await edits.RedoAsync());
        Assert.True(File.Exists(created));
        Assert.True(Directory.Exists(folder));
    });

    [Fact]
    public Task DeletingAsksFirstAsBeforeAndKeepsTheFilesWhenTheAnswerIsNo() => Run(async () =>
    {
        var explorer = Create();
        var folder = Path.Combine(root, "Models");
        var file = Write(Path.Combine("Models", "Order.src"), "class Order {}");
        notifications.Confirm = false;

        await explorer.DeleteAsync(folder, isFolder: true);
        await explorer.DeleteAsync(file, isFolder: false);

        Assert.Equal(
            [("Delete?", "Delete the folder Models and everything in it?", "Delete", true), ("Delete?", "Delete Order.src?", "Delete", true)],
            notifications.Questions);
        Assert.True(File.Exists(file));
        Assert.Empty(participant.Asked);
        Assert.False(edits!.CanUndo);

        notifications.Confirm = true;
        await explorer.DeleteAsync(folder, isFolder: true);

        Assert.False(Directory.Exists(folder));
        Assert.True(await edits.UndoAsync());
        Assert.Equal("class Order {}", File.ReadAllText(file));
    });

    [Fact]
    public Task WhatCannotBeDoneIsReportedWithTheExplorersMessages() => Run(async () =>
    {
        var explorer = Create();
        var a = Write("A.src", "a");
        var b = Write("B.src", "b");
        var document = await documents!.OpenAsync(a);
        document.History.Push(document.Buffer.Apply([new TextChange(new TextSpan(0, 0), "unsaved ")]), null, null);

        await explorer.CreateAsync(root, "B.src", folder: false);
        await explorer.RenameAsync(b, "A.src");
        await explorer.DeleteAsync(a, isFolder: false);

        Assert.Equal(
            [
                (NotificationKind.Error, "B.src could not be created", "B.src already exists."),
                (NotificationKind.Error, "B.src could not be renamed", "A.src already exists."),
                (NotificationKind.Error, "A.src could not be deleted", "A.src has unsaved changes."),
            ],
            notifications.Shown);
        Assert.Equal("b", File.ReadAllText(b));
        Assert.True(File.Exists(a));
        Assert.Empty(participant.Asked);
        Assert.False(edits!.CanUndo);
    });

    private ExplorerFileActions Create()
    {
        documents = new DocumentService(new LineFeedSettings(), new PlainTextLanguages());
        var editors = new OpeningEditors(documents);
        edits = new WorkspaceEditService(documents, new Lazy<IEditorService>(() => editors), notifications, new FakeWorkspace(root), new MessageBus(),
            [new Lazy<IFileOperationParticipant>(() => participant)], documents, () => null, _ => { },
            new Lazy<FileStash>(() => new FileStash(stash, NoTrash.Instance)));
        return new ExplorerFileActions(edits, editors, notifications);
    }

    private string Write(string relative, string text)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    private static Task<bool> Run(Func<Task> test) =>
        HeadlessSession.Value.Dispatch(async () =>
        {
            await test();
            return true;
        }, TestContext.Current.CancellationToken);
}
