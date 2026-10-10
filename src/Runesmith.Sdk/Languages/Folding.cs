using Runesmith.Sdk.Documents;
using Runesmith.Text;

namespace Runesmith.Sdk.Languages;

/// <summary>What a folding range holds, so Runesmith can fold all of one kind.</summary>
/// <remarks>Added in plugin API 0.1.2.</remarks>
public enum FoldingRangeKind
{
    /// <summary>A block of code, such as a method's body.</summary>
    Block,

    /// <summary>A comment that spans several lines.</summary>
    Comment,

    /// <summary>A run of imports, such as <c>using</c> directives.</summary>
    Imports,

    /// <summary>A region the author marked, such as with <c>#region</c>.</summary>
    Region,
}

/// <summary>Lines that can be folded away: folding keeps <paramref name="StartLine"/> and hides the lines after it up to and including
/// <paramref name="EndLine"/>.</summary>
/// <param name="StartLine">The first line, counted from zero, which stays visible with the placeholder after it.</param>
/// <param name="EndLine">The last line hidden, after <paramref name="StartLine"/>.</param>
/// <remarks>Added in plugin API 0.1.2.</remarks>
public sealed record FoldingRange(int StartLine, int EndLine)
{
    public FoldingRangeKind Kind { get; init; }

    /// <summary>Gets the text shown in place of the hidden lines, such as <c>{...}</c>; null shows an ellipsis.</summary>
    public string? CollapsedText { get; init; }
}

/// <summary>Says which lines of a document can be folded. Export it with <c>[Export(typeof(IFoldingRangeProvider))]</c> and
/// <see cref="LanguagesAttribute"/>.</summary>
/// <remarks>The first provider that answers is used; without one, the editor folds by indentation. Runesmith asks when a document opens and a
/// moment after typing stops; folded ranges stay folded across edits. Added in plugin API 0.1.2.</remarks>
public interface IFoldingRangeProvider
{
    /// <summary>Gets the ranges, in any order; null when this provider does not know the document, so the next provider is asked.</summary>
    Task<IReadOnlyList<FoldingRange>?> GetFoldingRangesAsync(IDocument document, TextSnapshot snapshot, CancellationToken cancellationToken);

    /// <summary>Raised, on any thread, when the provider's answers change for a reason other than an edit, so open editors ask again.</summary>
    event EventHandler<LanguageFeatureChangedEventArgs>? Changed
    {
        add { }
        remove { }
    }
}

/// <summary>Gives the spans Expand Selection grows through, such as a word, its expression, its statement and its block. Export it with
/// <c>[Export(typeof(ISelectionRangeProvider))]</c> and <see cref="LanguagesAttribute"/>.</summary>
/// <remarks>The first provider that answers is used; without one, the editor grows through words, brackets and lines. Added in plugin API
/// 0.1.2.</remarks>
public interface ISelectionRangeProvider
{
    /// <summary>Gets the spans around an offset from the smallest to the largest, each containing the one before; null when this provider does
    /// not know the document, so the next provider is asked.</summary>
    Task<IReadOnlyList<TextSpan>?> GetSelectionRangesAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken);
}
