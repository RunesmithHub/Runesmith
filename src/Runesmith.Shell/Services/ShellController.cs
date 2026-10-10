using System.Composition;
using Avalonia.Controls;
using HammerUI.Services;
using Runesmith.Shell.Palette;

namespace Runesmith.Shell.Services;

/// <summary>What the main window can do for commands: the quick pick, file pickers and closing.</summary>
[Export]
[Shared]
[method: ImportingConstructor]
public sealed class ShellController(NotificationService notifications, [ImportMany] IEnumerable<Lazy<IQuickPickSource>> sources, Lazy<SettingSource> settings)
{
    private readonly FileDialogService files = new(notifications.Host);
    // The sources import the editors, which import this controller, so they are created when the quick pick first opens.
    private readonly Lazy<IReadOnlyList<IQuickPickSource>> sources = new(() => [.. sources.Select(s => s.Value).OrderBy(s => s.Prefix.Length)]);

    /// <summary>Gets or sets the main window, once it exists.</summary>
    public Window? Window { get; set; }

    /// <summary>Opens the quick pick with a query, such as <c>&gt;</c> for the command palette.</summary>
    public Task ShowQuickPickAsync(string initialQuery = "") => ShowAsync(sources.Value, initialQuery);

    /// <summary>Opens the quick pick on files, symbols, commands and settings at once; the usual prefixes still pick one kind.</summary>
    public Task ShowSearchEverywhereAsync()
    {
        var all = sources.Value;
        var everywhere = new EverywhereSource(
        [
            (all.First(s => s.Prefix.Length == 0), 6),
            (all.First(s => s.Prefix == "#"), 5),
            (all.First(s => s.Prefix == ">"), 5),
            (settings.Value, 4),
        ]);
        return ShowAsync([everywhere, .. all.Where(s => s.Prefix.Length > 0)], "");
    }

    private async Task ShowAsync(IReadOnlyList<IQuickPickSource> shown, string initialQuery)
    {
        if (notifications.Host.Dialogs?.IsOpen == true)
            return;

        QuickPick? pick = null;
        pick = new QuickPick(shown, initialQuery, (item, source) => notifications.Dialogs.Close(pick!, item is null ? null : (item, source)));
        if (await notifications.Dialogs.ShowAsync(pick) is ValueTuple<QuickPickItem, IQuickPickSource> picked)
            await picked.Item2.AcceptAsync(picked.Item1);
    }

    /// <summary>Lets the user pick one of some rows, such as one of several definitions.</summary>
    public async Task<QuickPickItem?> PickAsync(string placeholder, IReadOnlyList<QuickPickItem> items)
    {
        var source = new FixedSource(placeholder, items);
        QuickPick? pick = null;
        pick = new QuickPick([source], "", (item, _) => notifications.Dialogs.Close(pick!, item));
        return await notifications.Dialogs.ShowAsync(pick) as QuickPickItem;
    }

    public Task<string?> PickFolderAsync(string title, string? startFolder = null) => files.PickFolderAsync(title, startFolder);

    public Task<string?> PickFileToOpenAsync(string title, string? startFolder = null) => files.PickFileToOpenAsync(title, [], startFolder);

    public Task<string?> PickFileToSaveAsync(string title, string suggestedName) => files.PickFileToSaveAsync(title, suggestedName, []);

    /// <summary>Gets the text box with the keyboard focus, such as the find bar's, or null.</summary>
    public TextBox? FocusedTextBox => Window?.FocusManager?.GetFocusedElement() as TextBox;

    public void Close() => Window?.Close();

    private sealed class FixedSource(string placeholder, IReadOnlyList<QuickPickItem> items) : IQuickPickSource
    {
        public string Prefix => "";

        public string Name => "";

        public string Placeholder => placeholder;

        public Task<IReadOnlyList<QuickPickItem>> SearchAsync(string query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<QuickPickItem>>(query.Length == 0
                ? items
                : [.. items.Where(i => i.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || i.Subtitle?.Contains(query, StringComparison.OrdinalIgnoreCase) == true)]);

        public Task AcceptAsync(QuickPickItem item) => Task.CompletedTask;
    }
}
