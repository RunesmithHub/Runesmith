using System.Composition;
using System.Globalization;
using HammerUI;
using Runesmith.Shell.Editors;
using Runesmith.Text;

namespace Runesmith.Shell.Palette;

/// <summary>Goes to a line, and optionally a column, of the active editor: <c>:42</c> or <c>:42:7</c>.</summary>
[Export(typeof(IQuickPickSource))]
[method: ImportingConstructor]
public sealed class LineSource(EditorService editors) : IQuickPickSource
{
    public string Prefix => ":";

    public string Name => "Go to line";

    public string Placeholder => editors.Active is { } editor
        ? $"Type a line number between 1 and {editor.Area.Snapshot.LineCount}, and optionally :column"
        : "Open a file first to go to a line in it";

    public Task<IReadOnlyList<QuickPickItem>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        if (editors.Active is not { } editor)
            return Task.FromResult<IReadOnlyList<QuickPickItem>>([]);

        var parts = query.Split(':', ',');
        var current = editor.Area.Snapshot.GetPosition(editor.CaretOffset);
        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.CurrentCulture, out var line))
        {
            return Task.FromResult<IReadOnlyList<QuickPickItem>>(
            [
                new QuickPickItem($"Current line {current.Line + 1}, column {current.Column + 1}", null) { Icon = Icons.Info, IsEnabled = false },
            ]);
        }

        var column = parts.Length > 1 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.CurrentCulture, out var c) ? c : 1;
        line = Math.Clamp(line, 1, editor.Area.Snapshot.LineCount);
        var title = column > 1 ? $"Go to line {line}, column {column}" : $"Go to line {line}";
        return Task.FromResult<IReadOnlyList<QuickPickItem>>([new QuickPickItem(title, new TextPosition(line - 1, Math.Max(0, column - 1))) { Icon = Icons.ArrowRight }]);
    }

    public Task AcceptAsync(QuickPickItem item)
    {
        if (editors.Active is { } editor && item.Value is TextPosition position)
            editor.GoTo(position);
        return Task.CompletedTask;
    }
}
