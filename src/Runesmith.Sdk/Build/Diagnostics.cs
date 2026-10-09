using Runesmith.Text;

namespace Runesmith.Sdk.Build;

/// <summary>How serious a problem is.</summary>
public enum DiagnosticSeverity
{
    Error,
    Warning,
    Information,
    Hint,
}

/// <summary>A problem in a file, such as a compiler error.</summary>
/// <param name="Source">Who reported it, such as <c>csharp-ls</c> or <c>build</c>.</param>
/// <param name="End">The end of the problem's text, or null when only its start is known.</param>
public sealed record Diagnostic(string FilePath, TextPosition Start, TextPosition? End, DiagnosticSeverity Severity, string Message, string Source)
{
    /// <summary>Gets the problem's code, such as <c>CS0103</c>.</summary>
    public string? Code { get; init; }
}

/// <summary>Collects the problems every source reports, for the Problems panel and the editor's squiggles.</summary>
/// <remarks>Each source owns its own set per file and replaces it whole. Its members can be called from any thread; <see cref="Changed"/> is
/// raised on the UI thread.</remarks>
public interface IDiagnosticService
{
    /// <summary>Replaces what a source reports for a file; an empty list clears it.</summary>
    void Set(string source, string filePath, IReadOnlyList<Diagnostic> diagnostics);

    /// <summary>Clears everything a source reported.</summary>
    void Clear(string source);

    /// <summary>Gets the problems of a file from every source.</summary>
    IReadOnlyList<Diagnostic> Get(string filePath);

    /// <summary>Gets every problem, grouped by nothing, in no particular order.</summary>
    IReadOnlyList<Diagnostic> All { get; }

    /// <summary>Raised when problems change; its argument lists the files.</summary>
    event EventHandler<IReadOnlyCollection<string>>? Changed;
}
