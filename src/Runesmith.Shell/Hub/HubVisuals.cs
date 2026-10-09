using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using HammerUI;
using HammerUI.Controls;
using Runesmith.Hub;
using Runesmith.Hub.Enforcement;
using Runesmith.Shell.Views;
using RunesmithHub.Protocol.Index;

namespace Runesmith.Shell.Hub;

/// <summary>The small pieces the plugin manager is built from: tier and state badges, notices, capability lines and plugin icons.</summary>
internal static class HubVisuals
{
    private static readonly Color[] TileColors =
    [
        Color.Parse("#0E7490"), Color.Parse("#7C3AED"), Color.Parse("#B45309"), Color.Parse("#15803D"), Color.Parse("#BE185D"), Color.Parse("#4338CA"),
        Color.Parse("#0369A1"), Color.Parse("#A21CAF"),
    ];

    private static bool registered;

    /// <summary>Gets a line icon by name, with the few the hub needs beyond HammerUI's own.</summary>
    public static Geometry Icon(string name)
    {
        if (!registered)
        {
            registered = true;
            Icons.Register("hub-shield-check", "M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z", "m9 12 2 2 4-4");
            Icons.Register("hub-ban", "M2 12a10 10 0 1 0 20 0a10 10 0 1 0 -20 0", "m4.9 4.9 14.2 14.2");
            Icons.Register("hub-download", "M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4", "m7 10 5 5 5-5", "M12 15V3");
            Icons.Register("hub-pause", "M7 4h3v16H7z", "M14 4h3v16h-3z");
            Icons.Register("hub-archive", "M3 3h18a1 1 0 0 1 1 1v3a1 1 0 0 1-1 1H3a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1z", "M4 8v11a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8", "M10 12h4");
            Icons.Register("hub-clock", "M2 12a10 10 0 1 0 20 0a10 10 0 1 0 -20 0", "M12 6v6l4 2");
            Icons.Register("hub-globe", "M2 12a10 10 0 1 0 20 0a10 10 0 1 0 -20 0", "M12 2a14.5 14.5 0 0 0 0 20a14.5 14.5 0 0 0 0-20", "M2 12h20");
            Icons.Register("hub-key", "m15.5 7.5 2.3 2.3a1 1 0 0 0 1.4 0l2.1-2.1a1 1 0 0 0 0-1.4L19 4", "m21 2-9.6 9.6", "M2 15.5a5.5 5.5 0 1 0 11 0a5.5 5.5 0 1 0 -11 0");
            Icons.Register("hub-commit", "M9 12a3 3 0 1 0 6 0a3 3 0 1 0 -6 0", "M3 12h6", "M15 12h6");
            Icons.Register("hub-undo", "M9 14 4 9l5-5", "M4 9h10.5a5.5 5.5 0 0 1 0 11H11");
        }

        return Icons.Find("hub-" + name) ?? Icons.Find(name) ?? Icons.Puzzle;
    }

    /// <summary>Gets a theme brush that follows the theme.</summary>
    public static T Themed<T>(T control, AvaloniaProperty property, string key)
        where T : AvaloniaObject
    {
        control[!property] = new DynamicResourceExtension(key);
        return control;
    }

    /// <summary>A panel tinted with a theme color: a faint fill and a stronger outline, as the hub's badges and notices are.</summary>
    public static Panel Tinted(string brush, Control content, CornerRadius corner, double fill = 0.14, double outline = 0.45)
    {
        var background = Themed(new Border { CornerRadius = corner, Opacity = fill }, Border.BackgroundProperty, brush);
        var border = Themed(new Border { CornerRadius = corner, BorderThickness = new Thickness(1), Opacity = outline }, Border.BorderBrushProperty, brush);
        return new Panel { Children = { background, border, content } };
    }

    /// <summary>The tier badge: official on the accent color with Runesmith's mark, verified with a shield in the success color, unverified
    /// outlined in the warning color, local neutral.</summary>
    public static Control TierBadge(PluginTier? tier, bool local = false)
    {
        if (tier is null && !local)
            return new Panel();

        var (text, tip) = tier switch
        {
            PluginTier.Official => ("Official", "Published by Runesmith. Every version is reviewed."),
            PluginTier.Verified => ("Verified", "A verified publisher. Every version is reviewed by Runesmith before you get it."),
            PluginTier.Unverified => ("Unverified", "Checked automatically. No person at Runesmith has read this code."),
            _ => ("Local", "From your plugins folder, not from the hub."),
        };
        var label = new TextBlock { Text = text, FontSize = 11.5, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(7, 2, 8, 2), VerticalAlignment = VerticalAlignment.Center };
        Control badge;
        switch (tier)
        {
            case PluginTier.Official:
                row.Children.AddRange([new Logo(13), label]);
                Themed(label, TextBlock.ForegroundProperty, "AccentForegroundBrush");
                badge = Themed(new Border { CornerRadius = new CornerRadius(999), Child = row }, Border.BackgroundProperty, "AccentBrush");
                break;
            case PluginTier.Verified:
                row.Children.AddRange([Symbol("shield-check", 13, "SuccessBrush"), Themed(label, TextBlock.ForegroundProperty, "SuccessBrush")]);
                badge = Tinted("SuccessBrush", row, new CornerRadius(999), outline: 0.35);
                break;
            case PluginTier.Unverified:
                row.Children.AddRange([Symbol("AlertTriangle", 13, "WarningBrush"), Themed(label, TextBlock.ForegroundProperty, "WarningBrush")]);
                badge = Tinted("WarningBrush", row, new CornerRadius(999), fill: 0, outline: 1);
                break;
            default:
                row.Children.AddRange([Symbol("Folder", 13, "TextSecondaryBrush"), Themed(label, TextBlock.ForegroundProperty, "TextSecondaryBrush")]);
                badge = Tinted("TextSecondaryBrush", row, new CornerRadius(999), fill: 0.08, outline: 0.3);
                break;
        }

        badge.VerticalAlignment = VerticalAlignment.Center;
        badge.HorizontalAlignment = HorizontalAlignment.Left;
        ToolTip.SetTip(badge, tip);
        return badge;
    }

    /// <summary>A badge for a state an installed plugin or a version is in.</summary>
    public static Control StateBadge(string text, string icon, string brush, bool struck = false)
    {
        var label = Themed(new TextBlock { Text = text, FontSize = 11.5, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center }, TextBlock.ForegroundProperty, brush);
        if (struck)
            label.TextDecorations = TextDecorations.Strikethrough;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(7, 2, 8, 2), Children = { Symbol(icon, 12, brush), label } };
        var badge = Tinted(brush, row, new CornerRadius(999), outline: 0);
        badge.VerticalAlignment = VerticalAlignment.Center;
        badge.HorizontalAlignment = HorizontalAlignment.Left;
        return badge;
    }

    /// <summary>The badge for a notice kind, or null when the kind shows none.</summary>
    public static Control? StateBadge(NoticeKind? kind) => kind switch
    {
        NoticeKind.Blocked or NoticeKind.Quarantined => StateBadge("Blocked", "ban", "DangerBrush"),
        NoticeKind.FilesChanged => StateBadge("Files changed", "AlertTriangle", "DangerBrush"),
        NoticeKind.Frozen => StateBadge("Installs paused", "pause", "InfoBrush"),
        NoticeKind.Yanked => StateBadge("Withdrawn", "undo", "TextMutedBrush"),
        NoticeKind.Deprecated => StateBadge("Deprecated", "archive", "TextSecondaryBrush"),
        NoticeKind.Deleted => StateBadge("Removed from the hub", "Trash", "TextSecondaryBrush"),
        _ => null,
    };

    /// <summary>The badge for a version's state in the versions list, or null for a published version.</summary>
    public static Control? VersionBadge(VersionState state) => state switch
    {
        VersionState.Blocked => StateBadge("Blocked", "ban", "DangerBrush"),
        VersionState.Frozen => StateBadge("Installs paused", "pause", "InfoBrush"),
        VersionState.Yanked => StateBadge("Yanked", "undo", "TextMutedBrush", struck: true),
        VersionState.Deleted => StateBadge("Deleted", "Trash", "TextSecondaryBrush"),
        _ => null,
    };

    /// <summary>A notice: danger for plugins Runesmith turned off, warning and info for what to know, muted for quiet notes.</summary>
    /// <param name="actions">Buttons under the text.</param>
    public static Control Notice(PageNoticeKind kind, string title, string? message = null, IEnumerable<Control>? actions = null, string? icon = null, Control? extra = null)
    {
        var (brush, defaultIcon) = kind switch
        {
            PageNoticeKind.Danger => ("DangerBrush", "ban"),
            PageNoticeKind.Warning => ("WarningBrush", "AlertTriangle"),
            PageNoticeKind.Info => ("InfoBrush", "Info"),
            _ => ("TextMutedBrush", "Info"),
        };
        var text = new StackPanel { Spacing = 3 };
        text.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrWhiteSpace(message))
            text.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Classes = { "secondary" } });
        if (extra is not null)
            text.Children.Add(extra);
        var buttons = actions?.ToList() ?? [];
        if (buttons.Count > 0)
        {
            var row = new WrapPanel { Margin = new Thickness(0, 6, 0, 0), ItemSpacing = 8, LineSpacing = 6 };
            row.Children.AddRange(buttons);
            text.Children.Add(row);
        }

        var glyph = Symbol(icon ?? defaultIcon, 16, brush);
        glyph.VerticalAlignment = VerticalAlignment.Top;
        glyph.Margin = new Thickness(0, 1, 0, 0);
        var line = new DockPanel { Margin = new Thickness(12, 10) };
        DockPanel.SetDock(glyph, Dock.Left);
        text.Margin = new Thickness(10, 0, 0, 0);
        line.Children.AddRange([glyph, text]);
        return kind == PageNoticeKind.Quiet
            ? Themed(Themed(new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = line }, Border.BackgroundProperty, "SurfaceSunkenBrush"),
                Border.BorderBrushProperty, "BorderSubtleBrush")
            : Tinted(brush, line, new CornerRadius(8));
    }

    /// <summary>A line for one capability: its icon, label and reason, and where it comes from; high-risk ones in the danger color.</summary>
    public static Control Capability(CapabilityRow capability, bool showFrom = true)
    {
        var brush = capability.IsHighRisk ? "DangerBrush" : "TextSecondaryBrush";
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        title.Children.Add(Themed(new TextBlock { Text = capability.Label, FontWeight = FontWeight.SemiBold }, TextBlock.ForegroundProperty, capability.IsHighRisk ? "DangerBrush" : "TextPrimaryBrush"));
        if (capability.IsNew)
            title.Children.Add(StateBadge("New for you", "sparkles", "AccentBrush"));

        var reason = new TextBlock { TextWrapping = TextWrapping.Wrap, Classes = { "secondary" } };
        reason.Inlines!.Add(new Avalonia.Controls.Documents.Run(capability.Reason));
        if (showFrom)
            reason.Inlines.Add(Themed(new Avalonia.Controls.Documents.Run($"  From {capability.From}"), Avalonia.Controls.Documents.TextElement.ForegroundProperty, "TextMutedBrush"));
        if (capability.IsHighRisk)
            Themed(reason, TextBlock.ForegroundProperty, "DangerBrush");

        var glyph = Symbol(CapabilityIcon(capability.Id), 15, brush);
        glyph.VerticalAlignment = VerticalAlignment.Top;
        glyph.Margin = new Thickness(0, 2, 0, 0);
        var line = new DockPanel { Margin = new Thickness(0, 4) };
        DockPanel.SetDock(glyph, Dock.Left);
        var text = new StackPanel { Spacing = 1, Margin = new Thickness(10, 0, 0, 0), Children = { title, reason } };
        line.Children.AddRange([glyph, text]);
        return line;
    }

    /// <summary>The icon of a capability.</summary>
    public static string CapabilityIcon(string id) => id switch
    {
        "network" => "globe",
        "process" => "Terminal",
        "filesystem" => "Folder",
        "environment" => "Sliders",
        "credentials" => "key",
        "native" => "Cpu",
        "dynamic-code" => "Braces",
        _ => "Puzzle",
    };

    public static SymbolIcon Symbol(string name, double size, string brush) =>
        Themed(new SymbolIcon { Data = Icon(name), Size = size }, SymbolIcon.ForegroundProperty, brush);

    /// <summary>A plugin's icon: the hub's image when it is cached or can be fetched, Runesmith's mark for official plugins, the icon in the
    /// plugin's folder, or a tile with the name's first letter.</summary>
    public static Control PluginIcon(HubClient client, string id, string name, PluginTier? tier, HostedImage? image, string? folder, double size)
    {
        var tile = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size >= 48 ? 12 : 8), ClipToBounds = true };
        tile.Background = new SolidColorBrush(TileColors[(uint)StringComparer.Ordinal.GetHashCode(id) % TileColors.Length]);
        tile.Child = new TextBlock
        {
            Text = name.Length > 0 ? name[..1].ToUpperInvariant() : "?",
            FontSize = size * 0.42,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        if (tier == PluginTier.Official && id.StartsWith("runesmith.", StringComparison.Ordinal))
        {
            tile.Background = new SolidColorBrush(Color.Parse("#0F1013"));
            tile.Child = new Logo(size * 0.72) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            return tile;
        }

        if (folder is not null && LocalIcon(folder) is { } local && Load(local) is { } bitmap)
        {
            Show(tile, bitmap);
            return tile;
        }

        if (image is not null)
            _ = FetchAsync(client, image, tile);
        return tile;
    }

    private static string? LocalIcon(string folder)
    {
        foreach (var candidate in (ReadOnlySpan<string>)[Path.Combine(folder, "icon", "128.png"), Path.Combine(folder, "icon", "64.png"), Path.Combine(folder, "icon.png")])
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static async Task FetchAsync(HubClient client, HostedImage image, Border tile)
    {
        if (await client.GetIconAsync(image) is { } path && Load(path) is { } bitmap)
            Avalonia.Threading.Dispatcher.UIThread.Post(() => Show(tile, bitmap));
    }

    private static void Show(Border tile, Bitmap bitmap)
    {
        tile.Background = Brushes.Transparent;
        var picture = new Image { Source = bitmap, Stretch = Stretch.UniformToFill };
        RenderOptions.SetBitmapInterpolationMode(picture, BitmapInterpolationMode.HighQuality);
        tile.Child = picture;
    }

    private static Bitmap? Load(string path)
    {
        try
        {
            return new Bitmap(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
