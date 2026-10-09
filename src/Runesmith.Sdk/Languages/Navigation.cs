using Runesmith.Sdk.Documents;
using Runesmith.Text;

namespace Runesmith.Sdk.Languages;

/// <summary>A place in a file.</summary>
/// <param name="End">The end of the place, or null for a single position.</param>
public sealed record DocumentLocation(string FilePath, TextPosition Start, TextPosition? End = null);

/// <summary>What the editor shows while the pointer rests on a symbol.</summary>
/// <param name="Markdown">The text, as Markdown.</param>
/// <param name="Span">The span of the snapshot the information is about, highlighted while shown.</param>
public sealed record HoverInfo(string Markdown, TextSpan? Span = null);

/// <summary>Describes the symbol under the pointer. Export it with <c>[Export(typeof(IHoverProvider))]</c> and <see cref="LanguagesAttribute"/>.</summary>
public interface IHoverProvider
{
    Task<HoverInfo?> GetHoverAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken);
}

/// <summary>Finds where a symbol is defined. Export it with <c>[Export(typeof(IDefinitionProvider))]</c> and <see cref="LanguagesAttribute"/>.</summary>
public interface IDefinitionProvider
{
    Task<IReadOnlyList<DocumentLocation>> GetDefinitionsAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken);
}

/// <summary>One signature of a method or function call.</summary>
/// <param name="Label">The whole signature, such as <c>void WriteLine(string value)</c>.</param>
/// <param name="Parameters">The span of each parameter within the label.</param>
public sealed record SignatureInfo(string Label, IReadOnlyList<TextSpan> Parameters, string? Documentation = null);

/// <summary>The signatures that fit the call around the caret, and which one and which parameter are current.</summary>
public sealed record SignatureHelpInfo(IReadOnlyList<SignatureInfo> Signatures, int ActiveSignature, int ActiveParameter);

/// <summary>Shows the signatures of the call being typed. Export it with <c>[Export(typeof(ISignatureHelpProvider))]</c> and
/// <see cref="LanguagesAttribute"/>.</summary>
public interface ISignatureHelpProvider
{
    /// <summary>Whether typing the character opens signature help, such as <c>(</c>.</summary>
    bool IsTriggerCharacter(IDocument document, char character);

    /// <summary>Whether typing the character while signature help shows updates it, such as <c>,</c>.</summary>
    bool IsRetriggerCharacter(IDocument document, char character);

    Task<SignatureHelpInfo?> GetSignatureHelpAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken);
}

/// <summary>The language features of every provider, merged for one document; the editor uses this rather than the providers.</summary>
public interface ILanguageFeatures
{
    bool IsCompletionTrigger(IDocument document, char character);

    Task<CompletionList> GetCompletionsAsync(CompletionRequest request, CancellationToken cancellationToken);

    Task<CompletionItem> ResolveCompletionAsync(CompletionItem item, CancellationToken cancellationToken);

    Task<HoverInfo?> GetHoverAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken);

    Task<IReadOnlyList<DocumentLocation>> GetDefinitionsAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken);

    bool IsSignatureHelpTrigger(IDocument document, char character);

    bool IsSignatureHelpRetrigger(IDocument document, char character);

    Task<SignatureHelpInfo?> GetSignatureHelpAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken);
}
