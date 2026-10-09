using Runesmith.Languages.CSharp;
using Runesmith.Languages.Java.Analysis;
using Runesmith.LanguageServices;

namespace Runesmith.LanguageServer;

/// <summary>The analyzers the server offers.</summary>
internal static class AnalyzerCatalog
{
    public static IReadOnlyList<ILanguageAnalyzer> Create() => [new CSharpAnalyzer(), new JavaAnalyzer()];
}
