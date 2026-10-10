using System.Composition;
using System.Globalization;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Languages;
using Runesmith.Sdk.Workspace;
using Runesmith.Shell.Debugging;
using Runesmith.Text;

namespace Runesmith.Shell.Testing;

/// <summary>Puts a run marker with the last result beside each test and group in the gutter, Run and Debug code lenses above them, and a
/// failed test's message at the line it failed on.</summary>
[Export(typeof(IDecorationProvider))]
[Languages(LanguagesAttribute.Any)]
[Shared]
public sealed class TestDecorations : IDecorationProvider
{
    private readonly TestService tests;
    private readonly IWorkspace workspace;
    private readonly HashSet<string> pending = new(PathKey.Comparer);
    private bool everything;
    private bool scheduled;

    [ImportingConstructor]
    public TestDecorations(TestService tests, IWorkspace workspace)
    {
        TestIcons.Register();
        this.tests = tests;
        this.workspace = workspace;
        tests.Tree.Changed += (_, files) => Raise(files);
        tests.Tree.ResultsChanged += (_, files) => Raise(files);
    }

    public event EventHandler<DecorationsChangedEventArgs>? Changed;

    public Task<IReadOnlyList<Decoration>> GetDecorationsAsync(DecorationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.FromResult(request.Document.FilePath is { } path ? Decorate(path, request.Snapshot, tests.Tree, workspace.RootPath) : []);
    }

    /// <summary>Gets a file's decorations: a gutter marker and code lenses for each test and group declared in it, and a highlight with the
    /// message at each line a test failed on.</summary>
    internal static IReadOnlyList<Decoration> Decorate(string path, TextSnapshot snapshot, TestTree tree, string? root)
    {
        var decorations = new List<Decoration>();
        var marked = new HashSet<int>();
        lock (tree.Gate)
        {
            foreach (var node in tree.InFile(path).Where(n => n.Item.Start is not null).OrderBy(n => n.Item.Start!.Value.Line))
            {
                var line = node.Item.Start!.Value.Line;
                if (line < 0 || line >= snapshot.LineCount)
                    continue;

                var span = new TextSpan(snapshot.GetLine(line).Start, 0);
                var state = node.State;
                if (marked.Add(line))
                    decorations.Add(Marker(node, state, span));
                decorations.Add(new CodeLens(span, "Run") { CommandId = TestCommands.RunItem, CommandArgument = node, ToolTip = $"Run {node.Item.Label}" });
                if (node.Provider.CanDebug)
                    decorations.Add(new CodeLens(span, "Debug") { CommandId = TestCommands.DebugItem, CommandArgument = node, ToolTip = $"Debug {node.Item.Label}" });
                if (Status(node, state) is { } status)
                    decorations.Add(new CodeLens(span, status) { CommandId = TestCommands.ShowItem, CommandArgument = node, ToolTip = "Show the result in the Test Explorer" });
            }

            var failedLines = new HashSet<int>();
            foreach (var node in tree.AllNodes)
            {
                if (node.Result is not { State: TestState.Failed or TestState.Errored, Failure: { } failure } || TestFailures.Location(failure, root) is not { } location
                    || !PathKey.Equals(location.FilePath, path) || location.Position.Line < 0 || location.Position.Line >= snapshot.LineCount
                    || !failedLines.Add(location.Position.Line))
                    continue;

                var line = snapshot.GetLine(location.Position.Line);
                var text = snapshot.GetLineText(location.Position.Line);
                var indent = text.Length - text.TrimStart().Length;
                var summary = TestFailures.Summary(failure);
                decorations.Add(new TextHighlight(TextSpan.FromBounds(line.Start + indent, line.End))
                {
                    Background = true,
                    Underline = UnderlineStyle.None,
                    Tone = DecorationTone.Error,
                    ShowsInOverviewRuler = true,
                    ToolTip = $"**{node.Item.Label} failed**\n\n{failure.Message}",
                });
                decorations.Add(new InlayHint(line.End, $"  {node.Item.Label}: {summary}") { Side = InlayHintSide.After });
            }
        }

        return decorations;
    }

    private static GutterMarker Marker(TestNode node, TestState state, TextSpan span)
    {
        var (icon, tone, filled) = state switch
        {
            TestState.Passed => (TestIcons.Passed, DecorationTone.Success, false),
            TestState.Failed or TestState.Errored => (TestIcons.Failed, DecorationTone.Error, false),
            TestState.Skipped => (TestIcons.Skipped, DecorationTone.Neutral, false),
            TestState.Running or TestState.Queued => (TestIcons.Queued, DecorationTone.Accent, false),
            _ => (TestIcons.Run, DecorationTone.Success, true),
        };
        var what = node.IsTest ? "test" : "tests";
        var status = Status(node, state);
        return new GutterMarker(span, icon)
        {
            Tone = tone,
            IsFilled = filled,
            CommandId = TestCommands.RunItem,
            CommandArgument = node,
            ToolTip = status is null ? $"**{node.Item.Label}**\n\nClick to run the {what}." : $"**{node.Item.Label}**\n\n{status}.\n\nClick to run the {what} again.",
        };
    }

    /// <summary>Gets what a node's last result was, such as "Passed in 12 ms", or null when it has none.</summary>
    internal static string? Status(TestNode node, TestState state) => state switch
    {
        TestState.Passed => node.Duration is { } duration ? $"Passed in {Format(duration)}" : "Passed",
        TestState.Failed => node.IsTest && node.Result?.Failure is { } failure ? $"Failed: {TestFailures.Summary(failure)}" : $"{FailedCount(node)} failed",
        TestState.Errored => node.IsTest && node.Result?.Failure is { } error ? $"Error: {TestFailures.Summary(error)}" : $"{FailedCount(node)} failed",
        TestState.Skipped => node.Result?.SkipReason is { Length: > 0 } reason ? $"Skipped: {reason}" : "Skipped",
        TestState.Running => "Running",
        TestState.Queued => "Queued",
        _ => null,
    };

    /// <summary>Writes a duration the way the Test Explorer shows it, such as <c>12 ms</c> or <c>1.4 s</c>.</summary>
    internal static string Format(TimeSpan duration) =>
        duration.TotalMilliseconds < 1000
            ? string.Create(CultureInfo.CurrentCulture, $"{Math.Max(0, Math.Round(duration.TotalMilliseconds))} ms")
            : string.Create(CultureInfo.CurrentCulture, $"{duration.TotalSeconds:0.0} s");

    private static int FailedCount(TestNode node) => node.Tests().Count(t => t.State is TestState.Failed or TestState.Errored);

    // Results come in quickly during a run; editors are asked again at most every 100 ms.
    private void Raise(IReadOnlyCollection<string>? files)
    {
        lock (pending)
        {
            if (files is null)
                everything = true;
            else
                pending.UnionWith(files);
            if (scheduled)
                return;
            scheduled = true;
        }

        _ = Task.Delay(100).ContinueWith(_ => Flush(), TaskScheduler.Default);
    }

    private void Flush()
    {
        string[] files;
        bool all;
        lock (pending)
        {
            files = [.. pending];
            all = everything;
            pending.Clear();
            everything = false;
            scheduled = false;
        }

        if (all)
            Changed?.Invoke(this, new DecorationsChangedEventArgs(null));
        else
        {
            foreach (var file in files)
                Changed?.Invoke(this, new DecorationsChangedEventArgs(file));
        }
    }
}
