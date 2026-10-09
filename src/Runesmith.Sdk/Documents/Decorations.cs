using Runesmith.Text;

namespace Runesmith.Sdk.Documents;

/// <summary>The theme color a decoration is drawn in.</summary>
public enum DecorationTone
{
    Neutral,
    Accent,
    Information,
    Success,
    Warning,
    Error,
}

/// <summary>Something an editor draws over or beside a document's text. Decorations follow edits: a span moves with the text around it, and
/// a decoration whose text is deleted is dropped.</summary>
/// <param name="Span">Where the decoration is, in the snapshot of the request that returned it.</param>
public abstract record Decoration(TextSpan Span)
{
    /// <summary>Gets what shows while the pointer rests on the decoration, as Markdown.</summary>
    public string? ToolTip { get; init; }
}

/// <summary>An icon in the gutter, beside the line where <see cref="Decoration.Span"/> starts, such as a bookmark or a test result.</summary>
/// <param name="Icon">The icon's name, such as <c>flag</c>; see HammerUI's <c>Icons.Find</c>.</param>
public sealed record GutterMarker(TextSpan Span, string Icon) : Decoration(Span)
{
    public DecorationTone Tone { get; init; } = DecorationTone.Accent;

    /// <summary>Gets the command a click on the icon runs, or null when the icon is not clickable.</summary>
    public string? CommandId { get; init; }

    /// <summary>Gets the argument the command gets.</summary>
    public object? CommandArgument { get; init; }

    /// <summary>Gets whether the line is also marked beside the vertical scroll bar.</summary>
    public bool ShowsInOverviewRuler { get; init; }
}

/// <summary>Which side of its offset an inlay hint sits on.</summary>
public enum InlayHintSide
{
    /// <summary>Before the text that starts at the offset, such as a parameter name before an argument, a space apart from it; the caret at
    /// the offset is drawn after the hint.</summary>
    Before,

    /// <summary>After the text that ends at the offset, such as a type after a variable's name; the caret at the offset is drawn before the
    /// hint.</summary>
    After,
}

/// <summary>What an inlay hint shows.</summary>
public enum InlayHintKind
{
    Other,
    Type,
    Parameter,
}

/// <summary>Faint text drawn inside a line that is not part of the document, such as parameter names and inferred types. It cannot be
/// selected or edited, and the caret moves over it.</summary>
/// <param name="Offset">Where the hint is, in the snapshot of the request that returned it.</param>
/// <param name="Text">The hint, such as <c>count:</c> or <c>: int</c>.</param>
public sealed record InlayHint(int Offset, string Text) : Decoration(new TextSpan(Offset, 0))
{
    public InlayHintSide Side { get; init; } = InlayHintSide.Before;

    public InlayHintKind Kind { get; init; }
}

/// <summary>A line of clickable text above the line where <see cref="Decoration.Span"/> starts, such as "3 references" or "Run test".</summary>
public sealed record CodeLens(TextSpan Span, string Text) : Decoration(Span)
{
    /// <summary>Gets the command a click runs, or null when the text is only information.</summary>
    public string? CommandId { get; init; }

    /// <summary>Gets the argument the command gets.</summary>
    public object? CommandArgument { get; init; }
}

/// <summary>How a highlighted span is underlined.</summary>
public enum UnderlineStyle
{
    None,
    Solid,
    Dotted,
    Dashed,
    Wavy,
}

/// <summary>A span drawn underlined, with a background, or both, such as a spelling mistake or an unused variable. Its
/// <see cref="Decoration.ToolTip"/> shows in the editor's hover.</summary>
public sealed record TextHighlight(TextSpan Span) : Decoration(Span)
{
    public UnderlineStyle Underline { get; init; } = UnderlineStyle.Wavy;

    /// <summary>Gets whether the span gets a faint background of its tone.</summary>
    public bool Background { get; init; }

    public DecorationTone Tone { get; init; } = DecorationTone.Information;

    /// <summary>Gets whether the span's line is also marked beside the vertical scroll bar.</summary>
    public bool ShowsInOverviewRuler { get; init; }
}

/// <summary>A request for the decorations of a document.</summary>
/// <param name="Snapshot">The text the decorations' spans refer to.</param>
public sealed record DecorationRequest(IDocument Document, TextSnapshot Snapshot);

/// <summary>Tells the editor that a provider's decorations changed.</summary>
/// <param name="FilePath">The file whose decorations changed, or null for every file.</param>
public sealed class DecorationsChangedEventArgs(string? FilePath) : EventArgs
{
    public string? FilePath { get; } = FilePath;
}

/// <summary>Decorates documents: gutter icons, inlay hints, code lenses and highlighted spans. Export it with
/// <c>[Export(typeof(IDecorationProvider))]</c> and <see cref="Languages.LanguagesAttribute"/>.</summary>
/// <remarks>Each editor keeps each provider's decorations apart and replaces them whole when the provider answers again. It asks when the
/// document opens, a moment after typing stops, and when the provider raises <see cref="Changed"/>; until then the decorations follow the
/// edits. Requests come from a background thread and are cancelled when a newer one starts.</remarks>
public interface IDecorationProvider
{
    Task<IReadOnlyList<Decoration>> GetDecorationsAsync(DecorationRequest request, CancellationToken cancellationToken);

    /// <summary>Raised, on any thread, when the provider has new decorations, so open editors ask again.</summary>
    event EventHandler<DecorationsChangedEventArgs>? Changed
    {
        add { }
        remove { }
    }
}
