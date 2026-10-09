using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using HammerUI;
using HammerUI.Controls;

namespace Runesmith.Shell.Views;

/// <summary>A file's path as a row of folders, from the open folder down to the file; clicking a part reveals it.</summary>
internal sealed class PathBreadcrumbs : StackPanel
{
    private const int MaxOutsideParts = 4;

    private readonly Action<string> reveal;

    /// <param name="reveal">Reveals a part, given its full path, such as in the Explorer.</param>
    public PathBreadcrumbs(Action<string> reveal)
    {
        this.reveal = reveal;
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;
        ClipToBounds = true;
    }

    /// <summary>Splits a path into the parts to show with their full paths: from the folder's own name when the file is inside
    /// <paramref name="root"/>, else the last few folders.</summary>
    public static IReadOnlyList<(string Name, string Path)> Parts(string filePath, string? root)
    {
        char[] separators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];
        var parts = new List<(string Name, string Path)>();
        var relative = root is null ? null : Path.GetRelativePath(root, filePath);
        string current;
        string[] names;
        if (relative is not null && relative != "." && !relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative))
        {
            current = root!.TrimEnd(separators);
            parts.Add((Path.GetFileName(current), current));
            names = relative.Split(separators, StringSplitOptions.RemoveEmptyEntries);
        }
        else
        {
            var all = filePath.Split(separators, StringSplitOptions.RemoveEmptyEntries);
            var start = Math.Max(0, all.Length - MaxOutsideParts);
            current = Path.Combine([Path.GetPathRoot(filePath) ?? "", .. all[..start]]);
            names = all[start..];
        }

        foreach (var name in names)
        {
            current = Path.Combine(current, name);
            parts.Add((name, current));
        }

        return parts;
    }

    /// <summary>Shows a file's path, or nothing for null.</summary>
    public void Show(string? filePath, string? root, Geometry? fileIcon = null)
    {
        Children.Clear();
        if (filePath is null)
            return;

        var parts = Parts(filePath, root);
        for (var i = 0; i < parts.Count; i++)
        {
            var (name, path) = parts[i];
            var isFile = i == parts.Count - 1;
            if (i > 0)
                Children.Add(new SymbolIcon { Data = Icons.ChevronRight, Size = 12, Classes = { "crumb-separator" }, VerticalAlignment = VerticalAlignment.Center });

            var label = new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center };
            object content = label;
            if (isFile && fileIcon is not null)
                content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { new SymbolIcon { Data = fileIcon, Size = 14 }, label } };

            var button = new Button { Classes = { "crumb" }, Content = content, Focusable = false };
            button.Classes.Set("current", isFile);
            ToolTip.SetTip(button, $"Reveal {path} in the Explorer");
            button.Click += (_, _) => reveal(path);
            Children.Add(button);
        }
    }
}
