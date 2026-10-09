using Runesmith.Sdk.Build;
using Runesmith.Text;

namespace Runesmith.Editor;

/// <summary>A problem the editor underlines, at a span of the current text.</summary>
internal sealed record DiagnosticMarker(TextSpan Span, DiagnosticSeverity Severity, string Message, string? Code, string Source);
