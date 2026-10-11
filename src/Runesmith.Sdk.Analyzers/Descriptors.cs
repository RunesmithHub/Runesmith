using Microsoft.CodeAnalysis;

namespace Runesmith.Sdk.Analyzers;

/// <summary>The diagnostics of the SDK's compiler extensions.</summary>
internal static class Descriptors
{
    private const string Category = "Localization";
    private const string HelpLink = "https://docs.runesmith.dev/plugins/localization";

    /// <summary>A Fluent file of the English source does not parse.</summary>
    public static readonly DiagnosticDescriptor FluentSyntax = new(
        "RSL0001", "The Fluent file has a syntax error", "{0}", Category, DiagnosticSeverity.Error, isEnabledByDefault: true, helpLinkUri: HelpLink);

    /// <summary>Two English messages have the same id.</summary>
    public static readonly DiagnosticDescriptor DuplicateMessage = new(
        "RSL0002", "A message is defined twice", "The message {0} is defined again; each message id is used once in the English files",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true, helpLinkUri: HelpLink);

    /// <summary>Two messages, or a message and an attribute, become the same C# name.</summary>
    public static readonly DiagnosticDescriptor NameClash = new(
        "RSL0003", "Two messages get the same name in the Strings class",
        "The message {0} would be Strings.{1}, which {2} is already; rename one of the messages", Category, DiagnosticSeverity.Warning,
        isEnabledByDefault: true, helpLinkUri: HelpLink);

    /// <summary>Text users see is written in the code instead of the English Fluent file.</summary>
    public static readonly DiagnosticDescriptor NotLocalized = new(
        "RSL0100", "Text users see is not localized",
        "\"{0}\" is shown to users and can't be translated; move it into the English .ftl file and use the generated Strings member",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, helpLinkUri: HelpLink);
}
