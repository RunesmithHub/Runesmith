namespace Runesmith.Shell.Palette;

/// <summary>Searches files, commands and settings at once, a few of each, for search everywhere.</summary>
internal sealed class EverywhereSource(IReadOnlyList<(IQuickPickSource Source, int Count)> sources) : IQuickPickSource
{
    public string Prefix => "";

    public string Name => "Everywhere";

    public string Placeholder => "Search files, commands and settings";

    public async Task<IReadOnlyList<QuickPickItem>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var found = await Task.WhenAll(sources.Select(async s => (s.Source, Items: (await s.Source.SearchAsync(query, cancellationToken)).Take(s.Count))));
        return [.. found.SelectMany(f => f.Items.Select(item => item with { Value = new Picked(f.Source, item) }))];
    }

    public Task AcceptAsync(QuickPickItem item) => item.Value is Picked picked ? picked.Source.AcceptAsync(picked.Item) : Task.CompletedTask;

    private sealed record Picked(IQuickPickSource Source, QuickPickItem Item);
}
