using Runesmith.Sdk.Documents;
using Runesmith.Text;

namespace Runesmith.Sdk.Languages;

/// <summary>What kind of thing a symbol is; it picks the symbol's icon.</summary>
/// <remarks>The members are in the order of the language server protocol's symbol kinds. Added in plugin API 0.1.2.</remarks>
public enum SymbolKind
{
    File, Module, Namespace, Package, Class, Method, Property, Field, Constructor, Enum, Interface, Function, Variable, Constant, String, Number,
    Boolean, Array, Object, Key, Null, EnumMember, Struct, Event, Operator, TypeParameter,
}

/// <summary>A symbol of a document, such as a class or a method, with the symbols declared inside it.</summary>
/// <param name="Name">The name shown in the Outline and the breadcrumbs.</param>
/// <param name="Range">The span of the whole declaration, such as a method with its body, in the request's snapshot.</param>
/// <param name="SelectionRange">The span selected when the user goes to the symbol, usually its name; it lies inside <paramref name="Range"/>.</param>
/// <remarks>Added in plugin API 0.1.2.</remarks>
public sealed record DocumentSymbol(string Name, SymbolKind Kind, TextSpan Range, TextSpan SelectionRange)
{
    /// <summary>Gets a short description shown after the name, such as a method's signature.</summary>
    public string? Detail { get; init; }

    /// <summary>Gets the symbols declared inside this one, in the order they appear.</summary>
    public IReadOnlyList<DocumentSymbol> Children { get; init; } = [];

    /// <summary>Gets whether the symbol is deprecated, which draws it struck through.</summary>
    public bool IsDeprecated { get; init; }
}

/// <summary>Lists the symbols of a document as a tree, for the Outline, the breadcrumbs and Go to Symbol in File. Export it with
/// <c>[Export(typeof(IDocumentSymbolProvider))]</c> and <see cref="LanguagesAttribute"/>.</summary>
/// <remarks>The first provider that answers is used. Runesmith asks when a document opens and a moment after typing stops. Added in plugin API
/// 0.1.2.</remarks>
public interface IDocumentSymbolProvider
{
    /// <summary>Gets the document's top-level symbols, each with its children; null when this provider does not know the document, so the next
    /// provider is asked.</summary>
    Task<IReadOnlyList<DocumentSymbol>?> GetDocumentSymbolsAsync(IDocument document, TextSnapshot snapshot, CancellationToken cancellationToken);

    /// <summary>Raised, on any thread, when the provider's answers change for a reason other than an edit, such as its language server
    /// starting, so Runesmith asks again.</summary>
    event EventHandler<LanguageFeatureChangedEventArgs>? Changed
    {
        add { }
        remove { }
    }
}

/// <summary>A symbol found anywhere in the open folder.</summary>
/// <param name="Location">Where the symbol is declared; its start is where Runesmith goes.</param>
/// <remarks>Added in plugin API 0.1.2.</remarks>
public sealed record WorkspaceSymbol(string Name, SymbolKind Kind, DocumentLocation Location)
{
    /// <summary>Gets the name of the symbol that contains this one, such as its class, shown beside the name.</summary>
    public string? ContainerName { get; init; }

    /// <summary>Gets whether the symbol is deprecated.</summary>
    public bool IsDeprecated { get; init; }
}

/// <summary>Finds symbols in the whole open folder by name, for Go to Symbol in Workspace and Search Everywhere. Export it with
/// <c>[Export(typeof(IWorkspaceSymbolProvider))]</c>.</summary>
/// <remarks>Every provider is asked and the answers are merged, so a provider that knows nothing returns an empty list. Requests come from a
/// background thread and are cancelled as the user types. Added in plugin API 0.1.2.</remarks>
public interface IWorkspaceSymbolProvider
{
    /// <summary>Gets the symbols whose names match a query; the query may be fuzzy, such as <c>gUsr</c> for <c>GetUser</c>, and Runesmith
    /// ranks the answers itself.</summary>
    Task<IReadOnlyList<WorkspaceSymbol>> GetWorkspaceSymbolsAsync(string query, CancellationToken cancellationToken);
}

/// <summary>Tells Runesmith that a provider's answers changed.</summary>
/// <param name="FilePath">The file whose answers changed, or null for every file.</param>
/// <remarks>Added in plugin API 0.1.2.</remarks>
public sealed class LanguageFeatureChangedEventArgs(string? FilePath) : EventArgs
{
    public string? FilePath { get; } = FilePath;
}
