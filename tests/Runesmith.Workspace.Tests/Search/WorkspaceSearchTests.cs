using Runesmith.Sdk.Workspace;
using Runesmith.Text;
using Runesmith.Workspace.Search;

namespace Runesmith.Workspace.Tests.Search;

public sealed class WorkspaceSearchTests : IDisposable
{
    private readonly TestWorkspace workspace = new();
    private readonly TestDocuments documents = new();
    private readonly FindInFiles engine;
    private readonly WorkspaceSearch search;

    public WorkspaceSearchTests()
    {
        engine = new FindInFiles(workspace.Workspace, documents);
        search = new WorkspaceSearch(engine, workspace.Workspace);
        workspace.Write(Path.Combine("src", "Order.cs"), "class Order\n{\n    Order Next; // order of orders\n    int OrderId;\n}\n");
        workspace.Write(Path.Combine("src", "Customer.cs"), "class Customer { Order Last; }\n");
        workspace.Write(Path.Combine("docs", "orders.md"), "# Orders\nAn order has lines. ORDER BY date.\n");
        workspace.Write(Path.Combine("bin", "Debug", "Order.cs"), "class Order {}\n");
        workspace.Write("notes.txt", new string('x', 300) + " order " + new string('y', 300) + "\n");
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => workspace.Dispose();

    [Theory]
    [InlineData("order", TextSearchOptions.None, null, null)]
    [InlineData("Order", TextSearchOptions.MatchCase, null, null)]
    [InlineData("order", TextSearchOptions.WholeWord, null, null)]
    [InlineData(@"Order\w*", TextSearchOptions.Regex | TextSearchOptions.MatchCase, null, null)]
    [InlineData("order", TextSearchOptions.None, "*.cs", null)]
    [InlineData("order", TextSearchOptions.None, null, "docs")]
    [InlineData("order", TextSearchOptions.None, "src/**;*.txt", "Customer.cs")]
    public async Task FindsWhatTheSearchPanelFinds(string text, TextSearchOptions options, string? include, string? exclude)
    {
        await workspace.Workspace.OpenAsync(workspace.Root);
        var panel = new List<FileSearchResult>();
        await foreach (var result in engine.SearchAsync(text, options, include, exclude, Token))
            panel.Add(result);

        var query = new WorkspaceSearchQuery(text)
        {
            Options = options,
            Include = include?.Split(';') ?? [],
            Exclude = exclude?.Split(';') ?? [],
        };
        var plugin = new List<WorkspaceSearchResult>();
        await foreach (var result in search.SearchAsync(query, Token))
            plugin.Add(result);

        Assert.NotEmpty(panel);
        Assert.Equal(panel.Select(r => r.FilePath).Order(), plugin.Select(r => r.FilePath).Order());
        foreach (var result in plugin)
        {
            var expected = panel.Single(r => r.FilePath == result.FilePath).Matches;
            Assert.Equal(expected.Select(m => (m.Line, m.Column, m.Length)), result.Matches.Select(m => (m.Line, m.Column, m.Length)));
            var lines = File.ReadAllLines(result.FilePath);
            Assert.All(result.Matches, m => Assert.Equal(lines[m.Line], m.LineText));
        }

        Assert.DoesNotContain(plugin, r => r.FilePath.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SearchesOpenDocumentsAsTheyAreInTheEditor()
    {
        await workspace.Workspace.OpenAsync(workspace.Root);
        documents.Add(Path.Combine(workspace.Root, "src", "Customer.cs"), "class Customer\n{\n    Invoice Last;\n}\n");

        var results = await Collect(new WorkspaceSearchQuery("Invoice"));

        var match = Assert.Single(Assert.Single(results).Matches);
        Assert.Equal((2, 4, 7, "    Invoice Last;"), (match.Line, match.Column, match.Length, match.LineText));
    }

    [Fact]
    public async Task StopsAtMaxResults()
    {
        await workspace.Workspace.OpenAsync(workspace.Root);

        var results = await Collect(new WorkspaceSearchQuery("order") { MaxResults = 3 });

        Assert.Equal(3, results.Sum(r => r.Matches.Count));
        Assert.Single(results, r => r.IsLimitReached);
    }

    [Fact]
    public async Task RefusesAnInvalidRegularExpressionAndStopsWhenCancelled()
    {
        await workspace.Workspace.OpenAsync(workspace.Root);

        Assert.Throws<ArgumentException>(() => search.SearchAsync(new WorkspaceSearchQuery("(") { Options = TextSearchOptions.Regex }, Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Collect(new WorkspaceSearchQuery("order"), new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task FindsFilesByGlobFromTheFileList()
    {
        await workspace.Workspace.OpenAsync(workspace.Root);

        Assert.Equal(
            [Path.Combine(workspace.Root, "src", "Customer.cs"), Path.Combine(workspace.Root, "src", "Order.cs")],
            await search.FindFilesAsync("**/*.cs", cancellationToken: Token));
        Assert.Equal(2, (await search.FindFilesAsync("*.md;*.txt", cancellationToken: Token)).Count);
        Assert.Single(await search.FindFilesAsync("src/**", maxResults: 1, cancellationToken: Token));
        Assert.Empty(await search.FindFilesAsync("bin/**", cancellationToken: Token));
    }

    [Fact]
    public async Task FindsNothingWithoutAnOpenFolder()
    {
        Assert.Empty(await Collect(new WorkspaceSearchQuery("order")));
        Assert.Empty(await search.FindFilesAsync("*", cancellationToken: Token));
    }

    private async Task<List<WorkspaceSearchResult>> Collect(WorkspaceSearchQuery query, CancellationToken? cancellationToken = null)
    {
        var results = new List<WorkspaceSearchResult>();
        await foreach (var result in search.SearchAsync(query, cancellationToken ?? Token))
            results.Add(result);
        return results;
    }
}
