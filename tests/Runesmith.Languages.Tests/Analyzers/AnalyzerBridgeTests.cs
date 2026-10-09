using Runesmith.Languages.Analyzers;
using Runesmith.LanguageServices.Tests;
using Runesmith.Sdk.Build;
using Runesmith.Text;

namespace Runesmith.Languages.Tests.Analyzers;

public sealed class AnalyzerBridgeTests : IDisposable
{
    private const string FilePath = "/work/A.fake";

    private readonly FakeAnalyzer analyzer = new("WriteLine");
    private readonly TestDocuments documents = new();
    private readonly TestWorkspace workspace = new();
    private readonly TestDiagnostics diagnostics = new();
    private readonly AnalyzerBridge bridge;

    public AnalyzerBridgeTests() => bridge = new AnalyzerBridge([analyzer], documents, workspace, diagnostics, new TestOutput());

    public void Dispose() => bridge.Dispose();

    [Fact]
    public void OpensDocumentsByPathAndUntitledOnesByName()
    {
        documents.Add(new TestDocument(FilePath, "A.fake", "text"));
        documents.Add(new TestDocument(null, "Untitled-1", "text"));

        Assert.Contains($"open {FilePath} 1", analyzer.Calls);
        Assert.Contains("open untitled:Untitled-1 1", analyzer.Calls);
    }

    [Fact]
    public void EditsReachTheAnalyzerAsNewVersions()
    {
        var document = new TestDocument(FilePath, "A.fake", "text");
        documents.Add(document);

        document.Buffer.Insert(0, "a");
        document.Buffer.Insert(1, "b");

        Assert.Equal([$"open {FilePath} 1", $"change {FilePath} 2", $"change {FilePath} 3"], analyzer.Calls.Where(call => call.StartsWith("open", StringComparison.Ordinal) || call.StartsWith("change", StringComparison.Ordinal)));
        Assert.Equal("abtext", bridge.Host.GetDocument(FilePath)?.Snapshot.GetText());
    }

    [Fact]
    public async Task ProblemsReachTheDiagnosticsWithLinesAndColumns()
    {
        documents.Add(new TestDocument(FilePath, "A.fake", "fine\n  bad"));

        var problem = Assert.Single(await WaitForDiagnosticsAsync(FilePath));
        Assert.Equal(new TextPosition(1, 2), problem.Start);
        Assert.Equal(new TextPosition(1, 5), problem.End);
        Assert.Equal(DiagnosticSeverity.Error, problem.Severity);
        Assert.Equal("fake", problem.Source);
    }

    [Fact]
    public async Task ProblemsOfUntitledDocumentsAreNotPublished()
    {
        documents.Add(new TestDocument(null, "Untitled-1", "bad"));
        documents.Add(new TestDocument(FilePath, "A.fake", "bad"));

        await WaitForDiagnosticsAsync(FilePath);

        Assert.All(diagnostics.All, problem => Assert.Equal(FilePath, problem.FilePath));
    }

    [Fact]
    public async Task ClosingADocumentClosesItAndClearsItsProblems()
    {
        var document = new TestDocument(FilePath, "A.fake", "bad");
        documents.Add(document);
        await WaitForDiagnosticsAsync(FilePath);

        documents.Close(document);

        Assert.Contains($"close {FilePath}", analyzer.Calls);
        Assert.Empty(diagnostics.Get(FilePath));
        Assert.Null(bridge.PathOf(document));
    }

    [Fact]
    public void SavingAnUntitledDocumentReopensItUnderItsPath()
    {
        var document = new TestDocument(null, "Untitled-1", "text");
        documents.Add(document);

        documents.SaveAs(document, FilePath);

        Assert.Contains("close untitled:Untitled-1", analyzer.Calls);
        Assert.Contains($"open {FilePath} 1", analyzer.Calls);
        Assert.Equal(FilePath, bridge.PathOf(document));
    }

    [Fact]
    public async Task OpeningAFolderOpensItInTheAnalyzers()
    {
        await workspace.OpenAsync("/work");

        Assert.Contains("workspace /work", analyzer.Calls);
    }

    private async Task<IReadOnlyList<Diagnostic>> WaitForDiagnosticsAsync(string path)
    {
        for (var i = 0; i < 200 && diagnostics.Get(path).Count == 0; i++)
            await Task.Delay(10, TestContext.Current.CancellationToken);
        return diagnostics.Get(path);
    }
}
