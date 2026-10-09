using RunesmithHub.Protocol.Catalog;
using RunesmithHub.Protocol.Index;

namespace Runesmith.Hub.Catalog;

/// <summary>What Browse asks for.</summary>
/// <param name="Tier">Only plugins of this tier, or null for every allowed tier.</param>
/// <param name="Category">Only plugins in this category, or null.</param>
/// <param name="CompatibleOnly">Only plugins with a version that runs on this Runesmith.</param>
public sealed record CatalogQuery(string Text, PluginTier? Tier = null, string? Category = null, bool CompatibleOnly = false);

/// <summary>The result of a search: the plugins in rank order, and how many the tier setting hid.</summary>
public sealed record CatalogResults(IReadOnlyList<PluginRecord> Plugins, int HiddenByTierSetting);

/// <summary>Searches the verified catalog and ranks the results the way the hub's website does: deprecated plugins last, then by how well the
/// name, id and listing match, then official before verified before unverified, then the most recently published.</summary>
public static class CatalogSearch
{
    public static CatalogResults Search(HubCatalog catalog, CatalogQuery query, AllowedTiers allowed, RunesmithHost host)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(query);
        var text = query.Text.Trim().ToLowerInvariant();
        var listed = catalog.Plugins.Where(p => p.State is PluginState.Listed or PluginState.Deprecated or PluginState.Frozen).ToList();
        var hidden = listed.Count(p => !allowed.Allows(p.Tier));
        var scored = new List<(PluginRecord Plugin, int Score)>();
        foreach (var plugin in listed.Where(p => allowed.Allows(p.Tier)))
        {
            if ((query.Tier is { } tier && plugin.Tier != tier)
                || (query.Category is { } category && !plugin.Categories.Contains(category, StringComparer.Ordinal))
                || (query.CompatibleOnly && !plugin.Versions.Any(host.CanRun)))
                continue;

            if (Score(plugin, text) is { } score)
                scored.Add((plugin, score));
        }

        var ranked = scored
            .OrderBy(entry => entry.Plugin.State == PluginState.Deprecated)
            .ThenByDescending(entry => entry.Score)
            .ThenBy(entry => entry.Plugin.Tier)
            .ThenByDescending(entry => entry.Plugin.Versions.Max(v => v.PublishedAt) ?? DateTimeOffset.MinValue)
            .Select(entry => entry.Plugin)
            .ToList();
        return new CatalogResults(ranked, hidden);
    }

    /// <summary>Scores how well a plugin matches; null when it does not match every word.</summary>
    internal static int? Score(PluginRecord plugin, string query)
    {
        if (query.Length == 0)
            return 0;
        if (plugin.Id == query)
            return 1000;

        var total = 0;
        var tokens = Tokenize(query);
        foreach (var token in tokens.Count > 0 ? tokens : [query])
        {
            var best = Math.Max(NameScore(plugin, query, token), Math.Max(TermScore(plugin, token), plugin.Id.Contains(token, StringComparison.Ordinal) ? 60 : 0));
            if (best == 0)
                return null;
            total += best;
        }

        return total;
    }

    private static int NameScore(PluginRecord plugin, string query, string token)
    {
        var name = plugin.Name.ToLowerInvariant();
        if (name == query)
            return 500;
        var words = Tokenize(name);
        if (words.Any(word => word.StartsWith(token, StringComparison.Ordinal)))
            return 120;
        if (name.Contains(token, StringComparison.Ordinal))
            return 80;
        if (token.Length >= 4 && words.Any(word => EditDistance(word[..Math.Min(word.Length, token.Length + 1)], token) <= 1 || EditDistance(word, token) <= 1))
            return 50;
        return 0;
    }

    private static int TermScore(PluginRecord plugin, string token)
    {
        var best = 0;
        void Consider(IEnumerable<string> terms, int weight)
        {
            foreach (var term in terms)
            {
                if (term == token)
                    best = Math.Max(best, weight * 10);
                else if (token.Length >= 3 && term.StartsWith(token, StringComparison.Ordinal))
                    best = Math.Max(best, weight * 7);
            }
        }

        Consider(plugin.Keywords.Select(k => k.ToLowerInvariant()), 4);
        Consider(plugin.Categories.SelectMany(c => Tokenize(c.ToLowerInvariant())), 3);
        Consider(Tokenize(plugin.Summary.ToLowerInvariant()), 2);
        Consider(Tokenize(plugin.Publisher), 2);
        return best;
    }

    private static List<string> Tokenize(string text)
    {
        var words = new List<string>();
        var start = 0;
        for (var i = 0; i <= text.Length; i++)
        {
            if (i < text.Length && char.IsLetterOrDigit(text[i]))
                continue;
            if (i > start)
                words.Add(text[start..i]);
            start = i + 1;
        }

        return words;
    }

    private static int EditDistance(string a, string b)
    {
        var previous = Enumerable.Range(0, b.Length + 1).ToArray();
        for (var i = 1; i <= a.Length; i++)
        {
            var current = new int[b.Length + 1];
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            previous = current;
        }

        return previous[b.Length];
    }
}
