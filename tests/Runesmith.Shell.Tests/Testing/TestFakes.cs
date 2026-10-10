using Runesmith.Sdk.Build;
using Runesmith.Sdk.Running;
using Runesmith.Sdk.Testing;
using Runesmith.Shell.Testing;
using Runesmith.Shell.Tests.Running;
using Runesmith.Text;

namespace Runesmith.Shell.Tests.Testing;

/// <summary>A test provider whose tests and run the test sets.</summary>
internal sealed class FakeTestProvider(string name = "Fake") : ITestProvider
{
    public string Name => name;

    public bool CanDebug { get; set; }

    public IReadOnlyList<TestItem> Items { get; set; } = [];

    /// <summary>Gets or sets what a file's discovery returns; null makes Runesmith discover the whole folder.</summary>
    public Func<TestDocumentContext, IReadOnlyList<TestItem>?> Document { get; set; } = _ => null;

    /// <summary>Gets or sets what a run does.</summary>
    public Func<TestRunRequest, ITestRun, CancellationToken, Task> Run { get; set; } = (_, _, _) => Task.CompletedTask;

    public List<TestRunRequest> Requests { get; } = [];

    public int Discoveries { get; private set; }

    public event EventHandler<TestsChangedEventArgs>? TestsChanged;

    public Task<IReadOnlyList<TestItem>> DiscoverAsync(TestDiscoveryContext context, CancellationToken cancellationToken)
    {
        Discoveries++;
        return Task.FromResult(Items);
    }

    public Task<IReadOnlyList<TestItem>?> DiscoverDocumentAsync(TestDocumentContext context, CancellationToken cancellationToken) => Task.FromResult(Document(context));

    public Task RunAsync(TestRunRequest request, ITestRun run, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Run(request, run, cancellationToken);
    }

    public void RaiseChanged(string? path) => TestsChanged?.Invoke(this, new TestsChangedEventArgs(path));
}

/// <summary>Keeps what each source reports, by file.</summary>
internal sealed class RecordingDiagnostics : IDiagnosticService
{
    private readonly Dictionary<(string Source, string File), IReadOnlyList<Diagnostic>> sets = [];

    public IReadOnlyList<Diagnostic> All
    {
        get
        {
            lock (sets)
                return [.. sets.Values.SelectMany(s => s)];
        }
    }

    public event EventHandler<IReadOnlyCollection<string>>? Changed;

    public void Set(string source, string filePath, IReadOnlyList<Diagnostic> diagnostics)
    {
        lock (sets)
            sets[(source, filePath)] = diagnostics;
        Changed?.Invoke(this, [filePath]);
    }

    public void Clear(string source)
    {
        lock (sets)
        {
            foreach (var key in sets.Keys.Where(k => k.Source == source).ToList())
                sets.Remove(key);
        }
    }

    public IReadOnlyList<Diagnostic> Get(string filePath)
    {
        lock (sets)
            return [.. sets.Where(s => s.Key.File == filePath).SelectMany(s => s.Value)];
    }
}

/// <summary>Test items as a C# test project would have them.</summary>
internal static class Items
{
    public const string File = "/src/CalculatorTests.cs";
    public const string OtherFile = "/src/ParserTests.cs";

    public static TestItem Project(params TestItem[] children) => new("Project", "Calculator.Tests", TestItemKind.Project) { Children = children };

    public static TestItem Namespace(params TestItem[] children) => new("Calculator.Tests", "Calculator.Tests", TestItemKind.Namespace) { Children = children };

    public static TestItem Class(string name, string file, int line, params TestItem[] children) =>
        new($"Calculator.Tests.{name}", name, TestItemKind.Class) { FilePath = file, Start = new TextPosition(line, 0), End = new TextPosition(line + 40, 0), Children = children };

    public static TestItem Test(string className, string name, string file, int line, params string[] tags) =>
        new($"Calculator.Tests.{className}.{name}", name, TestItemKind.Test) { FilePath = file, Start = new TextPosition(line, 4), End = new TextPosition(line + 3, 5), Tags = tags };

    public static IReadOnlyList<TestItem> Calculator(params string[] tests) =>
        [Project(Namespace(Class("CalculatorTests", File, 2, [.. tests.Select((t, i) => Test("CalculatorTests", t, File, 4 + (i * 6)))])))];

    public static string Id(string test) => $"Calculator.Tests.CalculatorTests.{test}";

    public static TestService Service(FakeTestProvider provider, IDiagnosticService? diagnostics = null, string root = "/") =>
        new([new Lazy<ITestProvider>(() => provider)], new FakeWorkspace(root), diagnostics ?? new RecordingDiagnostics(), () => false, _ => { }, null, () => null);
}
