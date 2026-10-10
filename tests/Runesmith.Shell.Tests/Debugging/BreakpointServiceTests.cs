using Runesmith.Sdk.Commands;
using Runesmith.Sdk.Documents;
using Runesmith.Shell.Debugging;
using Runesmith.Shell.Tests.Running;
using Runesmith.Text;
using static Runesmith.Shell.Tests.Debugging.DebugTesting;

namespace Runesmith.Shell.Tests.Debugging;

public sealed class BreakpointServiceTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("runesmith-breakpoints-").FullName;
    private readonly string other = Directory.CreateTempSubdirectory("runesmith-breakpoints-other-").FullName;
    private readonly string state = Directory.CreateTempSubdirectory("runesmith-breakpoints-state-").FullName;

    public void Dispose()
    {
        Directory.Delete(root, recursive: true);
        Directory.Delete(other, recursive: true);
        Directory.Delete(state, recursive: true);
    }

    private string File(string name) => Path.Combine(root, "src", name);

    [Fact]
    public void TogglingAddsAndRemovesABreakpointAndSaysWhichFileChanged()
    {
        var breakpoints = Breakpoints(root, state);
        var changed = new List<string?>();
        breakpoints.Changed += (_, path) => changed.Add(path);

        var added = breakpoints.Toggle(File("Program.cs"), 4);
        breakpoints.Toggle(File("Program.cs"), 9);
        var removed = breakpoints.Toggle(File("Program.cs"), 4);

        Assert.Equal(new LineBreakpoint(File("Program.cs"), 4), added);
        Assert.Null(removed);
        Assert.Equal([9], breakpoints.In(File("Program.cs")).Select(b => b.Line));
        Assert.Equal([File("Program.cs"), File("Program.cs"), File("Program.cs")], changed);
    }

    [Fact]
    public async Task BreakpointsWatchesAndExceptionChoicesAreSavedPerFolder()
    {
        var workspace = new FakeWorkspace(root);
        var breakpoints = new BreakpointService(workspace, null, state);
        breakpoints.Toggle(File("Program.cs"), 4);
        breakpoints.Set(new LineBreakpoint(File("Util.cs"), 0) { Condition = "x > 1", HitCondition = "3", IsEnabled = false });
        breakpoints.Set(new LineBreakpoint(File("Util.cs"), 7) { LogMessage = "x is {x}" });
        breakpoints.AddWatch(" count * 2 ");
        breakpoints.AddWatch("person.Name");
        breakpoints.SetExceptionFilter("all", true, ["unhandled"]);

        var reopened = new BreakpointService(new FakeWorkspace(root), null, state);

        Assert.Equal(breakpoints.All.OrderBy(b => b.Path).ThenBy(b => b.Line), reopened.All.OrderBy(b => b.Path).ThenBy(b => b.Line));
        Assert.Equal(["count * 2", "person.Name"], reopened.Watches);
        Assert.Equal(["all", "unhandled"], reopened.ExceptionFilters!.Order());
        var json = System.IO.File.ReadAllText(Directory.GetFiles(Path.Combine(state, "debug")).Single());
        Assert.Contains("\"src/Program.cs\"", json, StringComparison.Ordinal);
        Assert.Contains("\"line\": 5", json, StringComparison.Ordinal);

        await workspace.OpenAsync(other);
        Assert.Empty(breakpoints.All);
        Assert.Empty(breakpoints.Watches);
        Assert.Null(breakpoints.ExceptionFilters);
        await workspace.OpenAsync(root);
        Assert.Equal(3, breakpoints.All.Count);
    }

    [Fact]
    public void ADamagedStateFileReadsAsNoBreakpoints()
    {
        var breakpoints = Breakpoints(root, state);
        breakpoints.Toggle(File("Program.cs"), 1);
        System.IO.File.WriteAllText(Directory.GetFiles(Path.Combine(state, "debug")).Single(), "{ not json");

        Assert.Empty(Breakpoints(root, state).All);
    }

    [Theory]
    [InlineData("a\nb\nc\nd\n", 0, 0, "x\n", 3, 4)]
    [InlineData("a\nb\nc\nd\n", 2, 2, "", 3, 2)]
    [InlineData("a\nb\nc\nd\n", 4, 2, "", 3, 2)]
    [InlineData("a\nb\nc\nd\n", 6, 1, "XYZ", 3, 3)]
    [InlineData("a\nb\nc\nd\n", 7, 0, "\n\n", 3, 3)]
    [InlineData("a\nb\nc\nd\n", 6, 0, "\n", 3, 4)]
    public void ABreakpointMovesWithTheTextOfItsLine(string text, int start, int length, string inserted, int line, int expected)
    {
        var buffer = new TextBuffer(text);

        var changeSet = buffer.Replace(new TextSpan(start, length), inserted);

        Assert.Equal(expected, BreakpointService.MapLine(changeSet, line));
    }

    [Fact]
    public void EditsInAnOpenDocumentMoveItsBreakpointsAndMergeOnesThatMeet()
    {
        var documents = new FakeDocuments();
        var document = documents.Add(File("Program.cs"), "one\ntwo\nthree\nfour\n");
        var breakpoints = new BreakpointService(new FakeWorkspace(root), documents, state);
        breakpoints.Toggle(File("Program.cs"), 1);
        breakpoints.Toggle(File("Program.cs"), 2);
        breakpoints.Toggle(File("Other.cs"), 1);

        document.Buffer.Insert(0, "zero\n");
        Assert.Equal([2, 3], breakpoints.In(File("Program.cs")).Select(b => b.Line));

        document.Buffer.Delete(new TextSpan(5, "one\ntwo\n".Length));
        Assert.Equal([1], breakpoints.In(File("Program.cs")).Select(b => b.Line));
        Assert.Equal([1], breakpoints.In(File("Other.cs")).Select(b => b.Line));
    }

    [Fact]
    public void StatusesChangeWithoutChangingTheBreakpoints()
    {
        var breakpoints = Breakpoints(root, state);
        var breakpoint = breakpoints.Toggle(File("Program.cs"), 3)!;
        var changed = 0;
        var statuses = 0;
        breakpoints.Changed += (_, _) => changed++;
        breakpoints.StatusesChanged += (_, _) => statuses++;

        breakpoints.SetStatuses(new Dictionary<LineBreakpoint, BreakpointStatus> { [breakpoint] = new(false, "Not loaded") });
        Assert.Equal("Not loaded", breakpoints.StatusOf(breakpoint)!.Message);
        breakpoints.ClearStatuses();

        Assert.Null(breakpoints.StatusOf(breakpoint));
        Assert.Equal(0, changed);
        Assert.Equal(2, statuses);
    }

    [Fact]
    public void TheToggleCommandTakesTheGuttersLineOrTheCaretLine()
    {
        var breakpoints = Breakpoints(root, state);
        var commands = new DebugCommands(breakpoints, null!, null!, null!, null!, new Lazy<Shell.Editors.EditorService>(() => throw new InvalidOperationException("No editor in this test.")),
            null!, null!);
        var registry = new CapturingRegistry();
        commands.Contribute(registry);
        var target = new ContextMenuTarget(File("Program.cs")) { Lines = (7, 7) };

        Assert.True(registry.CanRun(CommandIds.ToggleBreakpoint, target));
        registry.Run(CommandIds.ToggleBreakpoint, target);
        Assert.Equal([6], breakpoints.In(File("Program.cs")).Select(b => b.Line));
        registry.Run(CommandIds.ToggleBreakpoint, target);
        Assert.Empty(breakpoints.All);
        Assert.False(registry.CanRun(CommandIds.ToggleBreakpoint, new ContextMenuTarget(root, IsDirectory: true)));
        Assert.Equal("F9", registry.Definitions[CommandIds.ToggleBreakpoint].KeyBinding);
        Assert.Equal("F5", registry.Definitions[CommandIds.Continue].KeyBinding);
        Assert.Equal("F10", registry.Definitions[CommandIds.StepOver].KeyBinding);
        Assert.Equal("F11", registry.Definitions[CommandIds.StepInto].KeyBinding);
        Assert.Equal("Shift+F11", registry.Definitions[CommandIds.StepOut].KeyBinding);
    }

    [Fact]
    public void TheEditorShowsBreakpointsAndThePausedLine()
    {
        var breakpoints = Breakpoints(root, state);
        var path = File("Program.cs");
        breakpoints.Toggle(path, 0);
        breakpoints.Set(new LineBreakpoint(path, 1) { Condition = "x > 1" });
        breakpoints.Set(new LineBreakpoint(path, 2) { IsEnabled = false });
        breakpoints.Set(new LineBreakpoint(path, 3) { LogMessage = "x" });
        breakpoints.Toggle(path, 40);
        var snapshot = TextSnapshot.Create("a\nb\nc\nd\n    paused();\n");

        var decorations = DebugDecorations.Decorate(path, snapshot, breakpoints, (path, 4));

        var markers = decorations.OfType<GutterMarker>().ToList();
        Assert.Equal([0, 2, 4, 6, 8], markers.Select(m => m.Span.Start));
        Assert.Equal([DecorationTone.Error, DecorationTone.Warning, DecorationTone.Neutral, DecorationTone.Information, DecorationTone.Warning], markers.Select(m => m.Tone));
        Assert.Equal([true, true, false, true, true], markers.Select(m => m.IsFilled));
        Assert.All(markers, m => Assert.Equal(CommandIds.ToggleBreakpoint, m.CommandId));
        var line = Assert.Single(decorations.OfType<TextHighlight>());
        Assert.Equal(TextSpan.FromBounds(12, 21), line.Span);
        Assert.Empty(DebugDecorations.Decorate(File("Other.cs"), snapshot, breakpoints, (path, 4)));
    }

    private sealed class FakeDocuments : IDocumentService
    {
        private readonly List<IDocument> open = [];

        public event EventHandler<DocumentEventArgs>? Opened;

        public event EventHandler<DocumentEventArgs>? Saved;

        public event EventHandler<DocumentEventArgs>? Closed;

        public event EventHandler<DocumentEventArgs>? ChangedOnDisk;

        public IReadOnlyList<IDocument> Documents => open;

        public Editor.Tests.FileTestDocument Add(string path, string text)
        {
            var document = new Editor.Tests.FileTestDocument(path, text);
            open.Add(document);
            Opened?.Invoke(this, new DocumentEventArgs(document));
            return document;
        }

        public IDocument? Find(string filePath) => open.FirstOrDefault(d => d.FilePath == filePath);

        public Task<IDocument> OpenAsync(string filePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IDocument CreateUntitled(string? languageId = null) => throw new NotSupportedException();

        public Task SaveAsync(IDocument document, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task SaveAsAsync(IDocument document, string filePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task ReloadAsync(IDocument document, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public void Close(IDocument document)
        {
            open.Remove(document);
            Closed?.Invoke(this, new DocumentEventArgs(document));
            Saved?.Invoke(this, new DocumentEventArgs(document));
            ChangedOnDisk?.Invoke(this, new DocumentEventArgs(document));
        }
    }

    private sealed class CapturingRegistry : ICommandRegistry
    {
        private readonly Dictionary<string, (Func<object?, Task> Execute, Func<object?, bool>? CanExecute)> handlers = [];

        public Dictionary<string, CommandDefinition> Definitions { get; } = [];

        public void Add(CommandDefinition command, Func<object?, Task> execute, Func<object?, bool>? canExecute = null)
        {
            Definitions[command.Id] = command;
            handlers[command.Id] = (execute, canExecute);
        }

        public void AddMenuItem(MenuItemDefinition item)
        {
        }

        public void AddMenu(MenuDefinition menu)
        {
        }

        public bool CanRun(string id, object? argument) => handlers[id].CanExecute?.Invoke(argument) ?? true;

        public void Run(string id, object? argument) => handlers[id].Execute(argument).Wait(Token);
    }
}
