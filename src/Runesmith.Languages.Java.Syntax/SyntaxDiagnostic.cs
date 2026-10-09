namespace Runesmith.Languages.Java.Syntax;

/// <summary>A problem found while reading Java source: a syntax error, or a feature the Java version does not have.</summary>
/// <param name="Code">The kind of problem: <see cref="SyntaxError"/>, <see cref="ReleaseError"/> or <see cref="PreviewError"/>.</param>
public sealed record SyntaxDiagnostic(SourceSpan Span, string Code, string Message)
{
    /// <summary>The code of syntax errors.</summary>
    public const string SyntaxError = "JAVA-SYNTAX";

    /// <summary>The code of features that need a newer Java release.</summary>
    public const string ReleaseError = "JAVA-RELEASE";

    /// <summary>The code of preview features used without enabling preview features.</summary>
    public const string PreviewError = "JAVA-PREVIEW";

    internal static int Compare(SyntaxDiagnostic a, SyntaxDiagnostic b)
    {
        var byStart = a.Span.Start.CompareTo(b.Span.Start);
        if (byStart != 0)
            return byStart;
        var byLength = a.Span.Length.CompareTo(b.Span.Length);
        if (byLength != 0)
            return byLength;
        var byCode = string.CompareOrdinal(a.Code, b.Code);
        return byCode != 0 ? byCode : string.CompareOrdinal(a.Message, b.Message);
    }
}
