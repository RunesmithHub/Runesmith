using Runesmith.Sdk.Build;
using Runesmith.Sdk.Documents;
using Runesmith.Text;

namespace Runesmith.Sdk.Languages;

/// <summary>What a code action does, which groups it in the editor's menu.</summary>
public enum CodeActionKind
{
    /// <summary>Fixes a problem, such as adding a missing <c>using</c> directive.</summary>
    QuickFix,

    /// <summary>Changes the code's structure without changing what it does, such as extracting a method.</summary>
    Refactor,

    /// <summary>Changes a whole file, such as sorting its imports.</summary>
    Source,
}

/// <summary>Why code actions were asked for.</summary>
public enum CodeActionTrigger
{
    /// <summary>The caret moved or the text changed, to decide whether the light bulb shows; answer only with what is quick to find, such as
    /// the fixes for the problems in the span.</summary>
    Automatic,

    /// <summary>The user asked, with Alt+Enter or the light bulb; refactorings belong in the answer too.</summary>
    Invoked,
}

/// <summary>A request for the code actions of a span of a document.</summary>
/// <param name="Snapshot">The text the span refers to.</param>
/// <param name="Span">The selection, or the caret's line when nothing is selected.</param>
/// <param name="Diagnostics">The problems that touch the span.</param>
public sealed record CodeActionRequest(IDocument Document, TextSnapshot Snapshot, TextSpan Span, IReadOnlyList<Diagnostic> Diagnostics, CodeActionTrigger Trigger);

/// <summary>A quick fix or refactoring: a workspace edit, a command, or both; the edit is applied first.</summary>
/// <param name="Title">What the menu shows, such as "Add using System.Text".</param>
public sealed record CodeAction(string Title, CodeActionKind Kind)
{
    /// <summary>Gets the changes the action makes; null when the provider computes them in <see cref="ICodeActionProvider.ResolveAsync"/>,
    /// or when the action only runs a command.</summary>
    public WorkspaceEdit? Edit { get; init; }

    /// <summary>Gets the command run after the edit, such as one the plugin registered with <c>ICommandContributor</c>.</summary>
    public string? CommandId { get; init; }

    /// <summary>Gets the argument the command gets.</summary>
    public object? CommandArgument { get; init; }

    /// <summary>Gets whether the action is the most likely fix, listed first.</summary>
    public bool IsPreferred { get; init; }

    /// <summary>Gets the problems the action fixes.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics { get; init; } = [];

    /// <summary>Gets data the provider needs to resolve the action.</summary>
    public object? Data { get; init; }

    /// <summary>Gets the provider that made the action, which resolves it; set by Runesmith.</summary>
    public ICodeActionProvider? Provider { get; init; }

    /// <summary>Gets the name of the plugin whose provider made the action, or null for Runesmith; set by Runesmith.</summary>
    public string? Source { get; init; }
}

/// <summary>Offers quick fixes and refactorings for a span of a document. Export it with <c>[Export(typeof(ICodeActionProvider))]</c> and
/// <see cref="LanguagesAttribute"/>.</summary>
/// <remarks>Several providers can serve a language; their actions are listed together. Requests come from a background thread and are cancelled
/// when the caret moves on.</remarks>
public interface ICodeActionProvider
{
    Task<IReadOnlyList<CodeAction>> GetCodeActionsAsync(CodeActionRequest request, CancellationToken cancellationToken);

    /// <summary>Fills in what was left out for speed, usually the edit, when the user picks the action.</summary>
    Task<CodeAction> ResolveAsync(CodeAction action, CancellationToken cancellationToken) => Task.FromResult(action);
}
