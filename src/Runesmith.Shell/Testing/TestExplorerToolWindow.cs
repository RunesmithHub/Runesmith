using System.Composition;
using System.Globalization;
using Avalonia.Controls;
using Runesmith.Sdk.Commands;
using Runesmith.Sdk.ToolWindows;
using Runesmith.Sdk.Workspace;
using Runesmith.Shell.Editors;
using Runesmith.Text;

namespace Runesmith.Shell.Testing;

/// <summary>The Test Explorer tool window: the tests of the open folder with their results, the toolbar that runs them, and the result and
/// output of the selected test. It shows when a run starts.</summary>
[Export(typeof(IToolWindowProvider))]
[Export]
[Shared]
public sealed class TestExplorerToolWindow : IToolWindowProvider
{
    /// <summary>The tool window's id.</summary>
    public const string Id = "tests";

    private readonly TestService tests;
    private readonly Lazy<IToolWindowManager> toolWindows;
    private readonly Lazy<EditorService> editors;
    private readonly IWorkspace workspace;
    private readonly Lazy<ICommandService> commands;
    private TestExplorerView? view;
    private TestNode? pendingReveal;
    private int badgePending;

    [ImportingConstructor]
    public TestExplorerToolWindow(TestService tests, Lazy<IToolWindowManager> toolWindows, Lazy<EditorService> editors, IWorkspace workspace,
        Lazy<ICommandService> commands)
    {
        TestIcons.Register();
        this.tests = tests;
        this.toolWindows = toolWindows;
        this.editors = editors;
        this.workspace = workspace;
        this.commands = commands;
        tests.RunStarted += (_, _) => UiThread.Run(() => toolWindows.Value.Show(Id));
        tests.Tree.ResultsChanged += (_, _) => UpdateBadge();
        tests.Tree.Changed += (_, _) => UpdateBadge();
    }

    public ToolWindowDefinition Definition { get; } = new(Id, "Test Explorer", TestIcons.Tests, DockSide.Bottom) { Order = 3, KeyBinding = "Ctrl+Shift+J" };

    public Control CreateContent()
    {
        view = new TestExplorerView(tests, commands.Value, () => workspace.RootPath, Open);
        if (pendingReveal is { } node)
            view.Reveal(node);
        pendingReveal = null;
        return view;
    }

    /// <summary>Shows the window with a test or group selected.</summary>
    public void Reveal(TestNode node) => UiThread.Run(() =>
    {
        toolWindows.Value.Show(Id);
        if (view is null)
            pendingReveal = node;
        else
            view.Reveal(node);
    });

    private void Open(string path, TextPosition position) => _ = editors.Value.OpenAsync(path, position);

    // Results come in quickly during a run; the badge is counted again at most a few times a second.
    private void UpdateBadge()
    {
        if (Interlocked.Exchange(ref badgePending, 1) != 0)
            return;

        _ = Task.Delay(250).ContinueWith(_ =>
        {
            Interlocked.Exchange(ref badgePending, 0);
            var counts = tests.Tree.Count();
            var failed = counts.GetValueOrDefault(TestState.Failed) + counts.GetValueOrDefault(TestState.Errored);
            toolWindows.Value.SetBadge(Id, failed == 0 ? null : failed > 99 ? "99+" : failed.ToString(CultureInfo.CurrentCulture));
        }, TaskScheduler.Default);
    }
}
