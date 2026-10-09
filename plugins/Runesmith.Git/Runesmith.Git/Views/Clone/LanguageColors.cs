using Avalonia.Media;

namespace Runesmith.Git.Views.Clone;

/// <summary>The colors shown next to a repository's main language, as hosting services such as GitHub show them.</summary>
internal static class LanguageColors
{
    private static readonly Dictionary<string, Color> Colors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["C#"] = Color.Parse("#178600"),
        ["Java"] = Color.Parse("#B07219"),
        ["Kotlin"] = Color.Parse("#A97BFF"),
        ["TypeScript"] = Color.Parse("#3178C6"),
        ["JavaScript"] = Color.Parse("#F1E05A"),
        ["Python"] = Color.Parse("#3572A5"),
        ["Rust"] = Color.Parse("#DEA584"),
        ["Go"] = Color.Parse("#00ADD8"),
        ["C"] = Color.Parse("#555555"),
        ["C++"] = Color.Parse("#F34B7D"),
        ["Shell"] = Color.Parse("#89E051"),
        ["PowerShell"] = Color.Parse("#012456"),
        ["HTML"] = Color.Parse("#E34C26"),
        ["CSS"] = Color.Parse("#563D7C"),
        ["Ruby"] = Color.Parse("#701516"),
        ["Swift"] = Color.Parse("#F05138"),
        ["Dart"] = Color.Parse("#00B4AB"),
        ["PHP"] = Color.Parse("#4F5D95"),
        ["HCL"] = Color.Parse("#844FBA"),
        ["MDX"] = Color.Parse("#FCB32C"),
        ["Nix"] = Color.Parse("#7E7EFF"),
        ["Lua"] = Color.Parse("#000080"),
        ["Scala"] = Color.Parse("#C22D40"),
        ["F#"] = Color.Parse("#B845FC"),
        ["Vue"] = Color.Parse("#41B883"),
        ["Dockerfile"] = Color.Parse("#384D54"),
    };

    /// <summary>Gets a language's color, or a neutral gray for languages without one.</summary>
    public static Color For(string? language) => language is not null && Colors.TryGetValue(language, out var color) ? color : Color.Parse("#8B949E");
}
