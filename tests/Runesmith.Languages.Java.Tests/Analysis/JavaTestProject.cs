using Runesmith.Languages.Java.Analysis;
using Runesmith.Languages.Java.Tests.Jdk;
using Runesmith.LanguageServices;
using Runesmith.Text;

namespace Runesmith.Languages.Java.Tests.Analysis;

/// <summary>A folder of Java files on disk, and the Java analyzer working on it through the language service host.</summary>
internal sealed class JavaTestProject : IAsyncDisposable
{
    public const string Caret = "$$";

    private static readonly string Cache = Path.Combine(Path.GetTempPath(), "runesmith-java-tests", "cache");

    private readonly Dictionary<string, TextSnapshot> open = new(StringComparer.Ordinal);

    private JavaTestProject(string root, LanguageServiceHost host, JavaAnalyzer analyzer)
    {
        Root = root;
        Host = host;
        Analyzer = analyzer;
    }

    public string Root { get; }

    public LanguageServiceHost Host { get; }

    public JavaAnalyzer Analyzer { get; }

    /// <summary>Writes the files into a new folder and loads it; a file's <see cref="Caret"/> is removed.</summary>
    public static async Task<JavaTestProject> CreateAsync(IReadOnlyDictionary<string, string> files)
    {
        TestJdk.Require();
        var root = Path.Combine(Path.GetTempPath(), "runesmith-java-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        foreach (var (path, content) in files)
        {
            var full = Path.Combine(root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            await File.WriteAllTextAsync(full, content.Replace(Caret, "", StringComparison.Ordinal), TestContext.Current.CancellationToken);
        }

        var host = new LanguageServiceHost(Cache, (_, _) => { });
        var analyzer = new JavaAnalyzer();
        host.Add(analyzer);
        await host.OpenWorkspaceAsync(root, TestContext.Current.CancellationToken);
        await analyzer.Loaded;
        return new JavaTestProject(root, host, analyzer);
    }

    /// <summary>Opens a file with its text, where <see cref="Caret"/> marks an offset, and returns the offset.</summary>
    public int Open(string path, string text)
    {
        var offset = text.IndexOf(Caret, StringComparison.Ordinal);
        var snapshot = TextSnapshot.Create(text.Replace(Caret, "", StringComparison.Ordinal));
        var full = FullPath(path);
        open[full] = snapshot;
        Host.Open(full, JavaAnalyzer.LanguageId, snapshot);
        return offset;
    }

    /// <summary>Inserts text into an open file and returns the offset after it.</summary>
    public int Insert(string path, int offset, string text)
    {
        var full = FullPath(path);
        var changes = new[] { new TextChange(new TextSpan(offset, 0), text) };
        var snapshot = open[full].Apply(changes);
        open[full] = snapshot;
        Host.Change(full, snapshot, changes);
        return offset + text.Length;
    }

    public TextSnapshot Snapshot(string path) => open[FullPath(path)];

    public Task<CompletionResult> CompleteAsync(string path, int offset, CompletionTriggerKind trigger = CompletionTriggerKind.Invoked, char? character = null) =>
        Host.CompleteAsync(FullPath(path), Snapshot(path), offset, trigger, character, TestContext.Current.CancellationToken);

    /// <summary>Completes after a typed <c>.</c>, as the editor asks.</summary>
    public Task<CompletionResult> CompleteMemberAsync(string path, int offset) => CompleteAsync(path, offset, CompletionTriggerKind.Character, '.');

    public Task<HoverResult?> HoverAsync(string path, int offset) =>
        Host.HoverAsync(FullPath(path), Snapshot(path), offset, TestContext.Current.CancellationToken);

    public Task<IReadOnlyList<SourceLocation>> DefinitionAsync(string path, int offset) =>
        Host.DefinitionAsync(FullPath(path), Snapshot(path), offset, TestContext.Current.CancellationToken);

    public Task<SignatureHelpResult?> SignatureHelpAsync(string path, int offset) =>
        Host.SignatureHelpAsync(FullPath(path), Snapshot(path), offset, TestContext.Current.CancellationToken);

    public async Task<IReadOnlyList<Problem>> DiagnosticsAsync(string path) =>
        await Analyzer.DiagnosticsAsync(Host.GetDocument(FullPath(path))!, TestContext.Current.CancellationToken);

    public string FullPath(string path) => Path.GetFullPath(Path.Combine(Root, path));

    public async ValueTask DisposeAsync()
    {
        await Host.DisposeAsync();
        if (Directory.Exists(Root))
            Directory.Delete(Root, recursive: true);
    }

    /// <summary>A Maven build file for a release.</summary>
    public static string Pom(int release) => $"""
        <project>
          <modelVersion>4.0.0</modelVersion>
          <groupId>test</groupId>
          <artifactId>app</artifactId>
          <version>1.0</version>
          <properties>
            <maven.compiler.release>{release}</maven.compiler.release>
          </properties>
        </project>
        """;

    /// <summary>A Gradle build file with a Java toolchain.</summary>
    public static string GradleBuild(int toolchain) => $$"""
        plugins { id 'java' }
        java { toolchain { languageVersion = JavaLanguageVersion.of({{toolchain}}) } }
        """;
}
