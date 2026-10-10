using System.Composition;
using Runesmith.Sdk.Commands;
using Runesmith.Sdk.Running;
using Runesmith.Sdk.ToolWindows;
using Runesmith.Shell.Editors;

namespace Runesmith.Shell.Testing;

/// <summary>The commands that run and debug tests: all of them, the failed ones, the one at the caret, and one from its gutter marker or code
/// lens; in the Run menu.</summary>
[Export(typeof(ICommandContributor))]
[Shared]
[method: ImportingConstructor]
public sealed class TestCommands(TestService tests, Lazy<EditorService> editors, Lazy<IToolWindowManager> toolWindows, Lazy<TestExplorerToolWindow> explorer)
    : ICommandContributor
{
    public const string RunAll = "test.runAll";
    public const string DebugAll = "test.debugAll";
    public const string RerunFailed = "test.rerunFailed";
    public const string RerunLast = "test.rerunLast";
    public const string RunAtCaret = "test.runAtCaret";
    public const string DebugAtCaret = "test.debugAtCaret";
    public const string RunFile = "test.runFile";
    public const string Stop = "test.stop";
    public const string Refresh = "test.refresh";
    public const string RunItem = "test.runItem";
    public const string DebugItem = "test.debugItem";
    public const string ShowItem = "test.showItem";

    private const string Category = "Test";
    private const string Group = "3-tests";

    public void Contribute(ICommandRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        TestIcons.Register();
        registry.Add(
            new CommandDefinition(RunAll, "Run All Tests", Category) { Icon = TestIcons.RunAll, KeyBinding = "Ctrl+Alt+U", Description = "Run every test in the folder." },
            _ => Show(tests.RunAllAsync()),
            _ => HasTests);
        registry.Add(
            new CommandDefinition(DebugAll, "Debug All Tests", Category) { Icon = TestIcons.Debug, Description = "Debug every test in the folder." },
            _ => Show(tests.RunAllAsync(RunMode.Debug)),
            _ => HasTests && tests.Providers.Any(p => p.CanDebug));
        registry.Add(
            new CommandDefinition(RerunFailed, "Rerun Failed Tests", Category) { Icon = TestIcons.RerunFailed, KeyBinding = "Ctrl+Alt+Shift+U", Description = "Run the tests that failed again." },
            _ => Show(tests.RerunFailedAsync()),
            _ => HasFailures);
        registry.Add(
            new CommandDefinition(RerunLast, "Rerun Last Tests", Category) { Icon = "rotate-cw", Description = "Run the tests of the last run again." },
            _ => Show(tests.RerunLastAsync()),
            _ => tests.HasLastRun);
        registry.Add(
            new CommandDefinition(RunAtCaret, "Run Tests at Caret", Category) { Icon = TestIcons.Run, KeyBinding = "Ctrl+Shift+F10", Description = "Run the test or class the caret is in." },
            _ => AtCaret(RunMode.Run),
            _ => NodeAtCaret() is not null);
        registry.Add(
            new CommandDefinition(DebugAtCaret, "Debug Tests at Caret", Category) { Icon = TestIcons.Debug, KeyBinding = "Ctrl+Shift+F9", Description = "Debug the test or class the caret is in." },
            _ => AtCaret(RunMode.Debug),
            _ => NodeAtCaret() is { Provider.CanDebug: true });
        registry.Add(
            new CommandDefinition(RunFile, "Run Tests in File", Category) { Icon = TestIcons.Run, Description = "Run the tests of the file in the editor." },
            _ => Show(tests.RunAsync(TestsInActiveFile())),
            _ => TestsInActiveFile().Count > 0);
        registry.Add(
            new CommandDefinition(Stop, "Stop Tests", Category) { Icon = "stop", Description = "Stop the tests that run." },
            _ =>
            {
                tests.Stop();
                return Task.CompletedTask;
            },
            _ => tests.IsRunning);
        registry.Add(
            new CommandDefinition(Refresh, "Refresh Tests", Category) { Icon = "refresh", Description = "Find the folder's tests again." },
            _ => tests.DiscoverAllAsync(),
            _ => tests.Providers.Count > 0);
        registry.Add(
            new CommandDefinition(RunItem, "Run Test", Category) { Icon = TestIcons.Run, ShowInPalette = false },
            argument => argument is TestNode node ? Show(tests.RunAsync([node])) : Task.CompletedTask,
            argument => argument is TestNode);
        registry.Add(
            new CommandDefinition(DebugItem, "Debug Test", Category) { Icon = TestIcons.Debug, ShowInPalette = false },
            argument => argument is TestNode node ? Show(tests.RunAsync([node], RunMode.Debug)) : Task.CompletedTask,
            argument => argument is TestNode { Provider.CanDebug: true });
        registry.Add(
            new CommandDefinition(ShowItem, "Show Test Result", Category) { Icon = TestIcons.Tests, ShowInPalette = false },
            argument =>
            {
                if (argument is TestNode node)
                    explorer.Value.Reveal(node);
                return Task.CompletedTask;
            },
            argument => argument is TestNode);

        registry.AddMenuItem(new MenuItemDefinition(Menus.Run, RunAll, Group, 0));
        registry.AddMenuItem(new MenuItemDefinition(Menus.Run, DebugAll, Group, 1));
        registry.AddMenuItem(new MenuItemDefinition(Menus.Run, RerunFailed, Group, 2));
        registry.AddMenuItem(new MenuItemDefinition(Menus.Run, RunAtCaret, Group, 3));
        registry.AddMenuItem(new MenuItemDefinition(Menus.Run, DebugAtCaret, Group, 4));
        registry.AddMenuItem(new MenuItemDefinition(Menus.Run, Stop, Group, 5));
    }

    private bool HasTests => tests.Tree.AllRoots.Count > 0;

    private bool HasFailures
    {
        get
        {
            var counts = tests.Tree.Count();
            return counts.GetValueOrDefault(TestState.Failed) + counts.GetValueOrDefault(TestState.Errored) > 0;
        }
    }

    private TestNode? NodeAtCaret()
    {
        if (editors.Value.Active is not { Document.FilePath: { } path } editor)
            return null;

        var line = editor.Document.Buffer.Current.GetLineFromPosition(Math.Min(editor.CaretOffset, editor.Document.Buffer.Current.Length)).LineNumber;
        return tests.FindAt(path, line);
    }

    private IReadOnlyList<TestNode> TestsInActiveFile()
    {
        if (editors.Value.Active is not { Document.FilePath: { } path })
            return [];

        lock (tests.Tree.Gate)
            return [.. tests.Tree.InFile(path).Where(n => n.Parent is null || n.Parent.Item.FilePath != path)];
    }

    private Task AtCaret(RunMode mode) => NodeAtCaret() is { } node ? Show(tests.RunAsync([node], mode)) : Task.CompletedTask;

    private async Task Show(Task<TestRunSession?> starting)
    {
        if (await starting is not null)
            toolWindows.Value.Show(TestExplorerToolWindow.Id);
    }
}
