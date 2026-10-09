using Runesmith.Text;

namespace Runesmith.LanguageServices;

/// <summary>How serious a problem is.</summary>
public enum ProblemSeverity
{
    Error,
    Warning,
    Information,
    Hint,
}

/// <summary>A problem an analyzer found in a document version.</summary>
/// <param name="Span">Where the problem is, in the version the analyzer was given.</param>
/// <param name="Code">The problem's code, such as <c>CS0103</c> or <c>JAVA-RELEASE</c>.</param>
public sealed record Problem(TextSpan Span, ProblemSeverity Severity, string Message, string? Code = null);
