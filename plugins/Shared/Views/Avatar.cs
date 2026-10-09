using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Runesmith.Plugins.Views;

/// <summary>An avatar: round for people, rounded square for organizations, showing the first letter of the login until the image
/// arrives or when there is none.</summary>
internal sealed class Avatar : Border
{
    private static readonly Color[] Tones =
    [
        Color.Parse("#5B8DEF"), Color.Parse("#4FAE7C"), Color.Parse("#C77DDB"), Color.Parse("#E0884F"),
        Color.Parse("#3FA7B5"), Color.Parse("#D16A8C"), Color.Parse("#8A8FE8"), Color.Parse("#B39B3D"),
    ];

    public Avatar(AvatarCache? cache, string login, string? avatarUrl, double size, bool isOrganization = false)
    {
        Width = size;
        Height = size;
        CornerRadius = new CornerRadius(isOrganization ? size / 4 : size / 2);
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
            _ = LoadAsync(cache, avatarUrl, (int)Math.Ceiling(size));
    }

    private async Task LoadAsync(AvatarCache cache, string url, int size)
    {
        if (await cache.GetAsync(url, size) is { } bitmap)
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
