using Runesmith.Sdk.Running;
using Runesmith.Text;

namespace Runesmith.Sdk.Testing;

/// <summary>What an item of the test tree is.</summary>
/// <remarks>Added in plugin API 0.1.2.</remarks>
public enum TestItemKind
{
    /// <summary>A test project, or another unit that builds and runs its tests together.</summary>
    Project,

    /// <summary>A namespace or package.</summary>
    Namespace,

    /// <summary>A class, module or file that holds tests.</summary>
    Class,

    /// <summary>A test.</summary>
    Test,

    /// <summary>One case of a parameterized test, such as one row of its data.</summary>
    Case,
}

/// <summary>A test, or a group of tests such as a project, a namespace or a class, in the tree the Test Explorer shows.</summary>
/// <remarks>Added in plugin API 0.1.2.</remarks>
/// <param name="Id">An id that is unique among the provider's items and stays the same between discoveries, such as a test's full name;
/// results refer to it.</param>
/// <param name="Label">The name shown, such as a method's name.</param>
/// <param name="Kind">What the item is.</param>
public sealed record TestItem(string Id, string Label, TestItemKind Kind)
{
    /// <summary>Gets the full path of the file the item is declared in, or null when it has none, such as a project.</summary>
    public string? FilePath { get; init; }

    /// <summary>Gets where the item's declaration starts in <see cref="FilePath"/>, counted from 0; the editor shows the item's run marker
    /// on this line.</summary>
    public TextPosition? Start { get; init; }

    /// <summary>Gets where the item's declaration ends, or null when only its start is known.</summary>
    public TextPosition? End { get; init; }

    /// <summary>Gets a short text shown after the label, such as a case's arguments.</summary>
    public string? Description { get; init; }

    /// <summary>Gets the item's tags or categories, such as <c>slow</c>; the Test Explorer's filter finds them with <c>@slow</c>.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Gets the items it holds, such as a class's tests or a test's cases.</summary>
    public IReadOnlyList<TestItem> Children { get; init; } = [];
}

/// <summary>What a test provider gets when it discovers the tests of the open folder.</summary>
/// <remarks>Added in plugin API 0.1.2.</remarks>
/// <param name="RootPath">The open folder.</param>
public sealed record TestDiscoveryContext(string RootPath);

/// <summary>What a test provider gets when it discovers the tests of one file, such as after it changed.</summary>
/// <remarks>Added in plugin API 0.1.2.</remarks>
/// <param name="RootPath">The open folder.</param>
/// <param name="FilePath">The file's full path.</param>
/// <param name="Snapshot">The file's text: the open document's, with changes not saved yet, or the file's on disk.</param>
public sealed record TestDocumentContext(string RootPath, string FilePath, TextSnapshot Snapshot);

/// <summary>Says which tests a provider has to discover again.</summary>
/// <remarks>Added in plugin API 0.1.2.</remarks>
/// <param name="FilePath">The file whose tests changed, or null when the tests of the whole folder did.</param>
public sealed class TestsChangedEventArgs(string? FilePath) : EventArgs
{
    public string? FilePath { get; } = FilePath;
}

/// <summary>The tests to run or debug.</summary>
/// <remarks>Added in plugin API 0.1.2.</remarks>
/// <param name="Tests">The items to run: tests, cases, or groups whose tests all run. When the user runs every test, these are the provider's
/// top items.</param>
/// <param name="Mode">Whether to run the tests or debug them.</param>
/// <param name="RootPath">The open folder.</param>
public sealed record TestRunRequest(IReadOnlyList<TestItem> Tests, RunMode Mode, string RootPath);

/// <summary>A frame of a failed test's stack trace.</summary>
/// <remarks>Added in plugin API 0.1.2.</remarks>
/// <param name="Text">The frame as the test framework writes it, such as <c>at Calculator.Tests.Add() in /src/CalculatorTests.cs:line 12</c>.</param>
public sealed record TestStackFrame(string Text)
{
    /// <summary>Gets the full path of the frame's source file, or null when it has none.</summary>
    public string? FilePath { get; init; }

    /// <summary>Gets the frame's place in <see cref="FilePath"/>, counted from 0.</summary>
    public TextPosition? Position { get; init; }
}

/// <summary>Why a test failed: the assertion's message, the values it compared and where it failed.</summary>
/// <remarks>Added in plugin API 0.1.2.</remarks>
/// <param name="Message">What went wrong, such as <c>Assert.Equal() Failure: Values differ</c>.</param>
public sealed record TestFailure(string Message)
{
    /// <summary>Gets the value the assertion expected, or null.</summary>
    public string? Expected { get; init; }

    /// <summary>Gets the value the test got, or null.</summary>
    public string? Actual { get; init; }

    /// <summary>Gets the stack trace, innermost frame first.</summary>
    public IReadOnlyList<TestStackFrame> StackTrace { get; init; } = [];

    /// <summary>Gets the file the test failed in; when null, the first frame of <see cref="StackTrace"/> with a file inside the open folder
    /// is used.</summary>
    public string? FilePath { get; init; }

    /// <summary>Gets where in <see cref="FilePath"/> the test failed, counted from 0.</summary>
    public TextPosition? Position { get; init; }
}

/// <summary>A run of tests that a provider reports to as the tests start and end; the Test Explorer, the editor and the Problems panel
/// show what it reports.</summary>
/// <remarks>Added in plugin API 0.1.2. Its members can be called from any thread. Reports name tests by <see cref="TestItem.Id"/>; reports
/// about ids the run does not know and reports after the run ended are ignored. Tests of the request that get no result keep the one they
/// had.</remarks>
public interface ITestRun
{
    /// <summary>Marks a test as running.</summary>
    void Started(string testId);

    /// <summary>Marks a test as passed.</summary>
    void Passed(string testId, TimeSpan? duration = null);

    /// <summary>Marks a test as failed: an assertion did not hold.</summary>
    void Failed(string testId, TestFailure failure, TimeSpan? duration = null);

    /// <summary>Marks a test as errored: it could not run, or threw something other than a failed assertion.</summary>
    void Errored(string testId, TestFailure failure, TimeSpan? duration = null);

    /// <summary>Marks a test as skipped.</summary>
    void Skipped(string testId, string? reason = null);

    /// <summary>Adds text to the run's output, or to a test's own output when <paramref name="testId"/> is given.</summary>
    void AppendOutput(string text, string? testId = null);

    /// <summary>Adds an item found while running, such as a case of a parameterized test, under the item with <paramref name="parentId"/>.</summary>
    void AddItem(string parentId, TestItem item);

    /// <summary>Starts a launch plan under the debugger its <see cref="LaunchPlan.Debug"/> names, the same way a run configuration is
    /// debugged, and waits until the debug session ends.</summary>
    /// <returns>The program's exit code, or null when it did not start or its exit code is unknown; the run's output says why.</returns>
    Task<int?> DebugAsync(LaunchPlan plan, CancellationToken cancellationToken);
}

/// <summary>Finds and runs the tests of a kind of project, such as .NET test projects. Export it with
/// <c>[Export(typeof(ITestProvider))]</c>.</summary>
/// <remarks>Added in plugin API 0.1.2. Runesmith asks for the folder's tests when it opens and when the provider raises
/// <see cref="TestsChanged"/>, and for one file's tests when the file is saved, changes on disk, or changes in an open editor. All methods are
/// called on a background thread.</remarks>
public interface ITestProvider
{
    /// <summary>Gets the name shown for the provider's tests when more than one provider has tests, such as ".NET".</summary>
    string Name { get; }

    /// <summary>Gets whether the provider can debug tests; when it can, a request in <see cref="RunMode.Debug"/> starts the tests with
    /// <see cref="ITestRun.DebugAsync"/>.</summary>
    bool CanDebug => false;

    /// <summary>Finds the tests of the open folder, as the top items of a tree.</summary>
    Task<IReadOnlyList<TestItem>> DiscoverAsync(TestDiscoveryContext context, CancellationToken cancellationToken);

    /// <summary>Finds the tests of one file, as top items with the same ids as <see cref="DiscoverAsync"/> gives them, holding only the file's
    /// tests; an empty list says the file has none. Returns null when the provider cannot tell from the file alone, and Runesmith asks for
    /// the whole folder's tests instead.</summary>
    Task<IReadOnlyList<TestItem>?> DiscoverDocumentAsync(TestDocumentContext context, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TestItem>?>(null);

    /// <summary>Runs or debugs tests, reporting each test's result to <paramref name="run"/>; canceling the token stops the run.</summary>
    Task RunAsync(TestRunRequest request, ITestRun run, CancellationToken cancellationToken);

    /// <summary>Raised, on any thread, when the provider knows its tests changed, such as after a build; Runesmith then discovers them
    /// again.</summary>
    event EventHandler<TestsChangedEventArgs>? TestsChanged
    {
        add { }
        remove { }
    }
}
