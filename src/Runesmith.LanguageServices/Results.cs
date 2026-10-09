using Runesmith.Text;

namespace Runesmith.LanguageServices;

/// <summary>What the pointer rests on, as Markdown, and the span it describes.</summary>
public sealed record HoverResult(string Markdown, TextSpan? Span = null);

/// <summary>One signature of a call.</summary>
/// <param name="Label">The whole signature, such as <c>void println(String x)</c>.</param>
/// <param name="Parameters">The span of each parameter within the label.</param>
public sealed record SignatureEntry(string Label, IReadOnlyList<TextSpan> Parameters, string? Documentation = null);

/// <summary>The signatures that fit the call around a position, and which one and which parameter are current.</summary>
public sealed record SignatureHelpResult(IReadOnlyList<SignatureEntry> Signatures, int ActiveSignature, int ActiveParameter);
