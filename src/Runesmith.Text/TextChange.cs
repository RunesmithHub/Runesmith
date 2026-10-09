namespace Runesmith.Text;

/// <summary>Replaces the text in <see cref="Span"/> with <see cref="NewText"/>; an empty span inserts and empty text deletes.</summary>
/// <param name="Span">The replaced range, in the snapshot the change applies to.</param>
/// <param name="NewText">The text that takes its place.</param>
public readonly record struct TextChange(TextSpan Span, string NewText);
