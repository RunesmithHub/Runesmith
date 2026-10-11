; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|--------------------------------------------------
RSL0001 | Localization | Error | StringsGenerator, a syntax error in an English Fluent file
RSL0002 | Localization | Error | StringsGenerator, a message id defined twice
RSL0003 | Localization | Warning | StringsGenerator, two messages with the same C# name
RSL0100 | Localization | Warning | UserVisibleTextAnalyzer, text users see that is not localized
