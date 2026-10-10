using System.Composition;
using Runesmith.Editor.Input;
using Runesmith.Sdk.Build;
using Runesmith.Sdk.Commands;
using Runesmith.Sdk.Languages;

namespace Runesmith.Editor;

/// <summary>The services every editor uses, gathered once so creating an editor takes one argument.</summary>
[Export]
[Shared]
[method: ImportingConstructor]
public sealed class EditorServices(
    ILanguageFeatures features,
    ISyntaxHighlighterProvider highlighters,
    IDiagnosticService diagnostics,
    ILanguageRegistry languages,
    IEditorFeatures editorFeatures,
    INavigationFeatures navigation,
    ISyntaxFeatures syntax,
    EditorKeyHooks keyHooks,
    Lazy<IWorkspaceEditService> workspaceEdits,
    Lazy<ICommandService> commands)
{
    public ILanguageFeatures Features { get; } = features;

    public ISyntaxHighlighterProvider Highlighters { get; } = highlighters;

    public IDiagnosticService Diagnostics { get; } = diagnostics;

    public ILanguageRegistry Languages { get; } = languages;

    /// <summary>Gets the code actions, rename, formatting and decorations of the providers.</summary>
    public IEditorFeatures EditorFeatures { get; } = editorFeatures;

    /// <summary>Gets the references, implementations, occurrences, symbols and hierarchies of the providers.</summary>
    public INavigationFeatures Navigation { get; } = navigation;

    /// <summary>Gets the folding ranges, selection ranges and semantic tokens of the providers.</summary>
    public ISyntaxFeatures Syntax { get; } = syntax;

    public EditorKeyHooks KeyHooks { get; } = keyHooks;

    /// <summary>Gets what applies the edits of code actions and renames.</summary>
    public Lazy<IWorkspaceEditService> WorkspaceEdits { get; } = workspaceEdits;

    /// <summary>Gets what runs the commands of code actions, gutter icons and code lenses.</summary>
    public Lazy<ICommandService> Commands { get; } = commands;
}
