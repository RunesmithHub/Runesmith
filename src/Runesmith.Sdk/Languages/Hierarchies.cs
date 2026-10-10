using Runesmith.Sdk.Documents;
using Runesmith.Text;

namespace Runesmith.Sdk.Languages;

/// <summary>A function or type in a call or type hierarchy.</summary>
/// <param name="Name">The name shown in the Hierarchy panel.</param>
/// <param name="Location">The span of the whole declaration.</param>
/// <remarks>Added in plugin API 0.1.2.</remarks>
public sealed record HierarchyItem(string Name, SymbolKind Kind, DocumentLocation Location)
{
    /// <summary>Gets a short description shown after the name, such as the containing type.</summary>
    public string? Detail { get; init; }

    /// <summary>Gets the span of the name, where Runesmith goes when the item is opened; null uses the start of <see cref="Location"/>.</summary>
    public DocumentLocation? SelectionLocation { get; init; }

    /// <summary>Gets data the provider needs to find the item's calls or types later.</summary>
    public object? Data { get; init; }

    /// <summary>Gets the provider that made the item, which is asked for the item's calls or types; set by Runesmith.</summary>
    public object? Provider { get; init; }
}

/// <summary>A call between two functions: the other function, and where the calls are.</summary>
/// <param name="Item">For incoming calls the caller, for outgoing calls the function called.</param>
/// <param name="CallSites">Where the calls are: in the caller's file for incoming calls, and in the original item's file for outgoing calls.</param>
/// <remarks>Added in plugin API 0.1.2.</remarks>
public sealed record HierarchyCall(HierarchyItem Item, IReadOnlyList<DocumentLocation> CallSites);

/// <summary>Finds who calls a function and what it calls, for the call hierarchy. Export it with
/// <c>[Export(typeof(ICallHierarchyProvider))]</c> and <see cref="LanguagesAttribute"/>.</summary>
/// <remarks>The first provider that prepares an item answers for it and for every item it returns. Each level of the tree is asked for when
/// the user expands it. Added in plugin API 0.1.2.</remarks>
public interface ICallHierarchyProvider
{
    /// <summary>Gets the function at an offset, usually one item; empty when there is none there, so the next provider is asked.</summary>
    Task<IReadOnlyList<HierarchyItem>> PrepareCallHierarchyAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken);

    Task<IReadOnlyList<HierarchyCall>> GetIncomingCallsAsync(HierarchyItem item, CancellationToken cancellationToken);

    Task<IReadOnlyList<HierarchyCall>> GetOutgoingCallsAsync(HierarchyItem item, CancellationToken cancellationToken);
}

/// <summary>Finds the base types and the derived types of a type, for the type hierarchy. Export it with
/// <c>[Export(typeof(ITypeHierarchyProvider))]</c> and <see cref="LanguagesAttribute"/>.</summary>
/// <remarks>The first provider that prepares an item answers for it and for every item it returns. Added in plugin API 0.1.2.</remarks>
public interface ITypeHierarchyProvider
{
    /// <summary>Gets the type at an offset, usually one item; empty when there is none there, so the next provider is asked.</summary>
    Task<IReadOnlyList<HierarchyItem>> PrepareTypeHierarchyAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken);

    /// <summary>Gets the types the item derives from or implements.</summary>
    Task<IReadOnlyList<HierarchyItem>> GetSupertypesAsync(HierarchyItem item, CancellationToken cancellationToken);

    /// <summary>Gets the types that derive from or implement the item.</summary>
    Task<IReadOnlyList<HierarchyItem>> GetSubtypesAsync(HierarchyItem item, CancellationToken cancellationToken);
}
