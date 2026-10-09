using System.Composition;
using HammerUI;
using Runesmith.Shell.Commands;
using Runesmith.Workspace.Search;

namespace Runesmith.Shell.Palette;

/// <summary>Finds commands by name for the command palette.</summary>
[Export(typeof(IQuickPickSource))]
[method: ImportingConstructor]
public sealed class CommandSource(CommandService commands) : IQuickPickSource
{
    public string Prefix => ">";

    public string Name => "Commands";

    public string Placeholder => "Search commands";

    public Task<IReadOnlyList<QuickPickItem>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var rows = new List<(QuickPickItem Item, double Score)>();
        foreach (var command in commands.Commands.Where(c => c.ShowInPalette))
        {
            var label = $"{command.Category}: {command.Title}";
            int[] matches = [];
            var score = query.Length == 0 ? 0 : FuzzyMatcher.Score(command.Title, query, out matches);
            if (score <= 0 && query.Length > 0)
            {
                if (FuzzyMatcher.Score(label, query, out _) <= 0)
                    continue;
                score = 1;
                matches = [];
            }

            rows.Add((new QuickPickItem(command.Title, command.Id)
            {
                Detail = command.Category,
                Subtitle = command.Description,
                Gesture = commands.GetKeyBinding(command.Id),
                Icon = Icons.Find(command.Icon) ?? Icons.ChevronRight,
                Matches = matches,
                IsEnabled = commands.CanExecute(command.Id),
            }, score));
        }

        IReadOnlyList<QuickPickItem> sorted = query.Length == 0
            ? [.. rows.Select(r => r.Item).OrderBy(i => i.Detail, StringComparer.Ordinal).ThenBy(i => i.Title, StringComparer.Ordinal)]
            : [.. rows.OrderByDescending(r => r.Score).ThenBy(r => r.Item.Title, StringComparer.Ordinal).Select(r => r.Item)];
        return Task.FromResult(sorted);
    }

    public Task AcceptAsync(QuickPickItem item) => commands.ExecuteAsync((string)item.Value!);
}
