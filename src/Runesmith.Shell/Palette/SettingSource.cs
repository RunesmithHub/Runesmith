using System.Composition;
using HammerUI;
using Runesmith.Sdk.Settings;
using Runesmith.Shell.Pages;
using Runesmith.Workspace.Search;

namespace Runesmith.Shell.Palette;

/// <summary>Finds settings by name for search everywhere; picking one opens its category of the settings.</summary>
[Export]
[method: ImportingConstructor]
public sealed class SettingSource(ISettingsService settings, Lazy<PageService> pages) : IQuickPickSource
{
    public string Prefix => "";

    public string Name => "Settings";

    public string Placeholder => "Search settings";

    public Task<IReadOnlyList<QuickPickItem>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        if (query.Length == 0)
            return Task.FromResult<IReadOnlyList<QuickPickItem>>([]);

        var rows = new List<(QuickPickItem Item, double Score)>();
        foreach (var definition in settings.Definitions.Where(d => d.Category != "Plugins"))
        {
            var score = FuzzyMatcher.Score(definition.Title, query, out var matches);
            if (score <= 0)
                continue;

            rows.Add((new QuickPickItem(definition.Title, definition.Category)
            {
                Subtitle = definition.Description,
                Detail = $"Setting: {definition.Category}",
                Icon = Icons.Settings,
                Matches = matches,
            }, score));
        }

        IReadOnlyList<QuickPickItem> sorted = [.. rows.OrderByDescending(r => r.Score).ThenBy(r => r.Item.Title, StringComparer.Ordinal).Select(r => r.Item)];
        return Task.FromResult(sorted);
    }

    public Task AcceptAsync(QuickPickItem item)
    {
        pages.Value.ShowSettings((string)item.Value!);
        return Task.CompletedTask;
    }
}

