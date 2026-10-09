using System.Security.Cryptography;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using HammerUI;
using HammerUI.Controls;
using Runesmith.Sdk;

namespace Runesmith.Plugins.Gitea;

/// <summary>The icons of the Forgejo and Gitea plugins, on HammerUI's 24 by 24 stroke grid.</summary>
internal static class GiteaIcons
{
    /// <summary>A flame over a forge's hearth, for Forgejo.</summary>
    public const string Forgejo = "forgejo";

    /// <summary>A cup of tea, for Gitea.</summary>
    public const string Gitea = "gitea";

    public const string LogOut = "log-out";
    public const string KeyRound = "key-round";
    public const string Server = "server";

    /// <summary>Registers the icons; call it at plugin start, before any UI or definition names them.</summary>
    public static void Register()
    {
        Icons.Register(Forgejo,
            "M12 3c.5 2.5 3.5 4 3.5 7a3.5 3.5 0 0 1-7 0c0-1.2.5-2 1-2.5.2 1.2.9 1.8 1.6 1.8C10.6 7.6 11 5 12 3z",
            "M4 16h16", "M6 16v2a2 2 0 0 0 2 2h8a2 2 0 0 0 2-2v-2");
        Icons.Register(Gitea, "M17 8h1a4 4 0 1 1 0 8h-1", "M3 8h14v9a4 4 0 0 1-4 4H7a4 4 0 0 1-4-4Z", "M7 2v2", "M10 2v2", "M13 2v2");
        Icons.Register(LogOut, "M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4", "m16 17 5-5-5-5", "M21 12H9");
        Icons.Register(KeyRound,
            "M2.586 17.414A2 2 0 0 0 2 18.828V21a1 1 0 0 0 1 1h3a1 1 0 0 0 1-1v-1a1 1 0 0 1 1-1h1a1 1 0 0 0 1-1v-1a1 1 0 0 1 1-1h.172a2 2 0 0 0 1.414-.586l.814-.814a6.5 6.5 0 1 0-4-4z",
            "M16.5 7.5h.01");
        if (!Icons.Exists(Server))
            Icons.Register(Server, "M4 2h16a2 2 0 0 1 2 2v4a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2z",
                "M4 14h16a2 2 0 0 1 2 2v4a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2v-4a2 2 0 0 1 2-2z", "M6 6h.01", "M6 18h.01");
    }

    /// <summary>Gets an icon by name, or the info icon when it is not registered.</summary>
    public static Geometry Get(string name) => Icons.Find(name) ?? Icons.Info;
}

/// <summary>Small builders the views share, so they look like the rest of Runesmith.</summary>
internal static class ViewHelpers
{
    /// <summary>Gets a text block whose color follows a theme brush, such as <c>TextMutedBrush</c>.</summary>
    public static TextBlock Label(string? text, string brush = "TextPrimaryBrush", double fontSize = 13, FontWeight weight = FontWeight.Normal, bool wrap = false)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            FontWeight = weight,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Brush(block, TextBlock.ForegroundProperty, brush);
        return block;
    }

    /// <summary>Binds a property to a theme brush, so it follows the theme.</summary>
    public static T Brush<T>(T control, AvaloniaProperty property, string brush)
        where T : Control
    {
        control.Bind(property, control.GetResourceObservable(brush));
        return control;
    }

    /// <summary>Gets an icon in a theme brush.</summary>
    public static SymbolIcon Icon(Geometry icon, double size = 16, string brush = "TextSecondaryBrush") =>
        Brush(new SymbolIcon { Data = icon, Size = size, VerticalAlignment = VerticalAlignment.Center }, SymbolIcon.ForegroundProperty, brush);

    /// <summary>Gets a button showing an icon and a label.</summary>
    public static Button IconButton(Geometry icon, string text, params string[] classes)
    {
        var button = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children = { new SymbolIcon { Data = icon, Size = 14, VerticalAlignment = VerticalAlignment.Center }, new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center } },
            },
        };
        button.Classes.AddRange(classes);
        return button;
    }

    /// <summary>Gets a button that looks like a link.</summary>
    public static Button Link(string text, Action click)
    {
        var button = new Button
        {
            Content = Label(text, "AccentBrush", 12),
            Classes = { "subtle", "small" },
            Padding = new Thickness(4, 2),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
        };
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>Gets a numbered step of instructions, with parts of it in bold.</summary>
    public static Grid Step(int number, params (string Text, bool Bold)[] parts)
    {
        var badge = new Border
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(10),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = number.ToString(System.Globalization.CultureInfo.InvariantCulture),
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Brush(badge, Border.BackgroundProperty, "AccentSubtleBrush");
        Brush((TextBlock)badge.Child, TextBlock.ForegroundProperty, "AccentBrush");
        var text = Label(null, "TextSecondaryBrush", wrap: true);
        text.VerticalAlignment = VerticalAlignment.Top;
        text.Margin = new Thickness(0, 1, 0, 0);
        text.Inlines = [];
        foreach (var (part, bold) in parts)
            text.Inlines.Add(new Run(part) { FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal });
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,10,*"), Children = { badge, text } };
        Grid.SetColumn(text, 2);
        return grid;
    }

    /// <summary>Gets a card with a sunken background, for a block of instructions.</summary>
    public static Border Card(Control child)
    {
        var card = new Border { Padding = new Thickness(14, 12), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = child };
        Brush(card, Border.BackgroundProperty, "SurfaceSunkenBrush");
        Brush(card, Border.BorderBrushProperty, "BorderSubtleBrush");
        return card;
    }

    /// <summary>Gets a spinning icon in the accent color.</summary>
    public static SymbolIcon Spinner(double size = 14) =>
        Brush(new SymbolIcon { Data = Icons.RotateCw, Size = size, Classes = { "spin" }, VerticalAlignment = VerticalAlignment.Center }, SymbolIcon.ForegroundProperty, "AccentBrush");

    /// <summary>Gets a thin line that separates sections.</summary>
    public static Border Divider(Thickness margin) =>
        Brush(new Border { Height = 1, Margin = margin }, Border.BackgroundProperty, "BorderSubtleBrush");

    /// <summary>Gets a row of a leading control, text that takes the rest of the width, and trailing controls.</summary>
    public static Grid Row(Control leading, Control text, Control trailing)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,12,*,16,Auto"), Children = { leading, text, trailing } };
        Grid.SetColumn(text, 2);
        Grid.SetColumn(trailing, 4);
        return grid;
    }

    /// <summary>Gets the note that says where tokens are kept.</summary>
    public static DockPanel StoreNote(GiteaContext context)
    {
        var text = context.HasSystemSecretStore ? "Tokens are kept in the system's secret store."
            : context.Secrets is not null ? "No system secret store was found, so tokens are kept in a file only you can read."
            : "Tokens are kept only until Runesmith closes.";
        var lockIcon = Icon(Icons.Lock, 12, "TextMutedBrush");
        lockIcon.VerticalAlignment = VerticalAlignment.Top;
        lockIcon.Margin = new Thickness(0, 2, 6, 0);
        var note = new DockPanel { Margin = new Thickness(2, 2, 0, 0), Children = { lockIcon, Label(text, "TextMutedBrush", 12, wrap: true) } };
        DockPanel.SetDock(lockIcon, Dock.Left);
        return note;
    }
}

/// <summary>An account's avatar: round, showing the first letter of the login until the image arrives or when there is none.</summary>
internal sealed class Avatar : Border
{
    private static readonly Color[] Tones =
    [
        Color.Parse("#5B8DEF"), Color.Parse("#4FAE7C"), Color.Parse("#C77DDB"), Color.Parse("#E0884F"),
        Color.Parse("#3FA7B5"), Color.Parse("#D16A8C"), Color.Parse("#8A8FE8"), Color.Parse("#B39B3D"),
    ];

    public Avatar(AvatarCache? cache, string login, Uri? avatarUrl, double size)
    {
        Width = size;
        Height = size;
        CornerRadius = new CornerRadius(size / 2);
        ClipToBounds = true;
        VerticalAlignment = VerticalAlignment.Center;
        Background = new SolidColorBrush(Tones[(int)((uint)StableHash(login) % (uint)Tones.Length)]);
        Child = new TextBlock
        {
            Text = login.Length > 0 ? char.ToUpperInvariant(login[0]).ToString() : "?",
            FontSize = Math.Max(9, size * 0.45),
            FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        if (cache is not null && avatarUrl is not null)
            _ = LoadAsync(cache, avatarUrl);
    }

    private async Task LoadAsync(AvatarCache cache, Uri url)
    {
        if (await cache.GetAsync(url) is { } bitmap)
        {
            Background = Brushes.Transparent;
            Child = new Image { Source = bitmap, Stretch = Stretch.UniformToFill };
        }
    }

    // string.GetHashCode changes between runs, and a person's color should not.
    private static int StableHash(string text)
    {
        var hash = 17;
        foreach (var character in text.ToUpperInvariant())
            hash = unchecked((hash * 31) + character);
        return hash;
    }
}

/// <summary>Downloads avatars once and keeps them on disk and in memory; failures are quiet.</summary>
/// <param name="http">The client to download with.</param>
/// <param name="folder">The folder the images are kept in.</param>
internal sealed class AvatarCache(HttpClient http, string folder)
{
    private static readonly TimeSpan Freshness = TimeSpan.FromDays(7);

    private readonly Dictionary<string, Task<Bitmap?>> loaded = new(StringComparer.Ordinal);
    private readonly Lock gate = new();

    /// <summary>Gets the folder a plugin keeps its avatars in.</summary>
    public static string FolderFor(string pluginId) => Path.Combine(RunesmithPaths.Cache, pluginId, "avatars");

    /// <summary>Gets an avatar, or null when it cannot be had; only HTTPS addresses are fetched.</summary>
    public Task<Bitmap?> GetAsync(Uri url)
    {
        if (url.Scheme != Uri.UriSchemeHttps)
            return Task.FromResult<Bitmap?>(null);

        lock (gate)
        {
            if (!loaded.TryGetValue(url.AbsoluteUri, out var task))
            {
                task = Task.Run(() => LoadAsync(url));
                loaded[url.AbsoluteUri] = task;
            }

            return task;
        }
    }

    private async Task<Bitmap?> LoadAsync(Uri uri)
    {
        var file = Path.Combine(folder, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri)))[..32] + ".img");
        try
        {
            if (File.Exists(file) && DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < Freshness)
                return new Bitmap(file);

            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("Runesmith", null));
            using var response = await http.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return File.Exists(file) ? new Bitmap(file) : null;

            var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            using var stream = new MemoryStream(bytes);
            var bitmap = new Bitmap(stream);
            Directory.CreateDirectory(folder);
            await File.WriteAllBytesAsync(file, bytes).ConfigureAwait(false);
            return bitmap;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException
            or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
