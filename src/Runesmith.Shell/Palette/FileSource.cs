using System.Composition;
using Runesmith.Sdk.Workspace;
using Runesmith.Shell.Appearance;
using Runesmith.Shell.Editors;
using Runesmith.Workspace.Search;

namespace Runesmith.Shell.Palette;

/// <summary>Finds the files of the open folder by name, for quick open.</summary>
[Export(typeof(IQuickPickSource))]
[method: ImportingConstructor]
public sealed class FileSource(QuickOpenSearch search, IWorkspace workspace, EditorService editors, FileIcons icons) : IQuickPickSource
{
    public string Prefix => "";

    public string Name => "Files";

    public string Placeholder => workspace.RootPath is null ? "Open a folder to search its files, or type > for commands" : "Search files by name";

    public async Task<IReadOnlyList<QuickPickItem>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        if (workspace.RootPath is null)
            return [];

        var matches = await search.SearchAsync(query, 60, cancellationToken);
        return
        [
            .. matches.Select(m =>
            {
                var name = m.RelativePath[m.NameStart..];
                var folder = m.NameStart > 0 ? m.RelativePath[..(m.NameStart - 1)] : "";
                var icon = icons.For(m.FullPath);
                return new QuickPickItem(name, m.FullPath)
                {
                    Subtitle = folder,
                    Icon = icon.Data,
                    IconBrush = icon.Brush,
                    IsIconFilled = icon.IsFilled,
                    Matches = [.. m.Matches.Where(i => i >= m.NameStart).Select(i => i - m.NameStart)],
                };
            }),
        ];
    }

    public Task AcceptAsync(QuickPickItem item) => editors.JumpToAsync((string)item.Value!);
}
