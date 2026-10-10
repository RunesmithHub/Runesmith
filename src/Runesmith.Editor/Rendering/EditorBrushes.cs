using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using HammerUI.Theming;

namespace Runesmith.Editor.Rendering;

/// <summary>The brushes and pens the editor draws with, taken from the theme.</summary>
internal sealed class EditorBrushes
{
    public required IBrush Background { get; init; }

    public required IBrush Text { get; init; }

    public required IBrush LineNumber { get; init; }

    public required IBrush CurrentLineNumber { get; init; }

    public required IBrush CurrentLine { get; init; }

    public required IBrush Selection { get; init; }

    public required IBrush InactiveSelection { get; init; }

    public required IBrush Caret { get; init; }

    public required IPen IndentGuide { get; init; }

    public required IPen ActiveIndentGuide { get; init; }

    public required IBrush Whitespace { get; init; }

    public required IBrush SearchMatch { get; init; }

    public required IPen CurrentSearchMatch { get; init; }

    public required IBrush BracketMatch { get; init; }

    public required IPen BracketMatchBorder { get; init; }

    public required IPen Error { get; init; }

    public required IPen Warning { get; init; }

    public required IPen Information { get; init; }

    public required IPen Link { get; init; }

    public required IBrush ErrorMarker { get; init; }

    public required IBrush WarningMarker { get; init; }

    public required IBrush AddedMarker { get; init; }

    public required IBrush ModifiedMarker { get; init; }

    public required IBrush DeletedMarker { get; init; }

    public required IPen Filler { get; init; }

    public required IBrush FoldMarker { get; init; }

    public required IBrush FoldPlaceholder { get; init; }

    public required IBrush FoldPlaceholderText { get; init; }

    private IBrush[] DiffLines { get; init; } = [];

    private IBrush[] DiffSpans { get; init; } = [];

    /// <summary>Gets the tint of a changed line of a diff.</summary>
    public IBrush DiffLine(DiffKind kind) => DiffLines[(int)kind];

    /// <summary>Gets the stronger tint of changed characters within a changed line.</summary>
    public IBrush DiffSpan(DiffKind kind) => DiffSpans[(int)kind];

    public static EditorBrushes From(Control control)
    {
        Color Get(string key, Color fallback) =>
            control.TryFindResource(key, control.ActualThemeVariant, out var value) && value is Color color ? color : fallback;

        var accent = Get(ThemeKeys.AccentColor, Color.Parse("#7073F6"));
        var text = Get(ThemeKeys.TextPrimaryColor, Colors.White);
        var muted = Get(ThemeKeys.TextMutedColor, Colors.Gray);
        var border = Get(ThemeKeys.BorderStrongColor, Colors.DimGray);
        var warning = Get(ThemeKeys.WarningColor, Colors.Orange);
        var danger = Get(ThemeKeys.DangerColor, Colors.Red);
        var info = Get(ThemeKeys.InfoColor, Colors.DodgerBlue);
        var success = Get(ThemeKeys.SuccessColor, Colors.SeaGreen);

        return new EditorBrushes
        {
            Background = new ImmutableSolidColorBrush(Get(ThemeKeys.SurfaceColor, Colors.Black)),
            Text = new ImmutableSolidColorBrush(text),
            LineNumber = new ImmutableSolidColorBrush(muted),
            CurrentLineNumber = new ImmutableSolidColorBrush(text),
            CurrentLine = new ImmutableSolidColorBrush(Get(ThemeKeys.SurfaceHoverColor, Color.FromArgb(18, 255, 255, 255))),
            Selection = new ImmutableSolidColorBrush(accent, 0.32),
            InactiveSelection = new ImmutableSolidColorBrush(muted, 0.22),
            Caret = new ImmutableSolidColorBrush(accent),
            IndentGuide = new ImmutablePen(new ImmutableSolidColorBrush(border, 0.55), 1),
            ActiveIndentGuide = new ImmutablePen(new ImmutableSolidColorBrush(muted, 0.8), 1),
            Whitespace = new ImmutableSolidColorBrush(muted, 0.5),
            SearchMatch = new ImmutableSolidColorBrush(warning, 0.28),
            CurrentSearchMatch = new ImmutablePen(new ImmutableSolidColorBrush(warning), 1.5),
            BracketMatch = new ImmutableSolidColorBrush(accent, 0.14),
            BracketMatchBorder = new ImmutablePen(new ImmutableSolidColorBrush(accent, 0.6), 1),
            Error = new ImmutablePen(new ImmutableSolidColorBrush(danger), 1.2),
            Warning = new ImmutablePen(new ImmutableSolidColorBrush(warning), 1.2),
            Information = new ImmutablePen(new ImmutableSolidColorBrush(info), 1.2),
            Link = new ImmutablePen(new ImmutableSolidColorBrush(accent), 1),
            ErrorMarker = new ImmutableSolidColorBrush(danger),
            WarningMarker = new ImmutableSolidColorBrush(warning),
            AddedMarker = new ImmutableSolidColorBrush(success),
            ModifiedMarker = new ImmutableSolidColorBrush(info),
            DeletedMarker = new ImmutableSolidColorBrush(danger),
            Filler = new ImmutablePen(new ImmutableSolidColorBrush(border, 0.45), 1),
            FoldMarker = new ImmutableSolidColorBrush(muted),
            FoldPlaceholder = new ImmutableSolidColorBrush(accent, 0.16),
            FoldPlaceholderText = new ImmutableSolidColorBrush(Color.FromArgb(255, (byte)((accent.R + text.R) / 2), (byte)((accent.G + text.G) / 2), (byte)((accent.B + text.B) / 2))),
            DiffLines = [new ImmutableSolidColorBrush(success, 0.12), new ImmutableSolidColorBrush(danger, 0.12), new ImmutableSolidColorBrush(info, 0.12)],
            DiffSpans = [new ImmutableSolidColorBrush(success, 0.3), new ImmutableSolidColorBrush(danger, 0.3), new ImmutableSolidColorBrush(info, 0.3)],
        };
    }
}
