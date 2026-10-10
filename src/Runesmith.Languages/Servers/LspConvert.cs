using System.Text.RegularExpressions;
using Runesmith.Languages.Features;
using Runesmith.Lsp;
using Runesmith.Sdk.Build;
using Runesmith.Sdk.Languages;
using Runesmith.Text;
using Protocol = Runesmith.Lsp.Protocol;

namespace Runesmith.Languages.Servers;

/// <summary>Converts between the protocol's types and Runesmith's. Text in Runesmith uses only <c>\n</c>, and both count columns in UTF-16
/// code units, so positions carry over unchanged.</summary>
internal static partial class LspConvert
{
    public static Protocol.Position ToPosition(TextSnapshot snapshot, int offset)
    {
        var position = snapshot.GetPosition(Math.Clamp(offset, 0, snapshot.Length));
        return new Protocol.Position(position.Line, position.Column);
    }

    public static TextPosition ToTextPosition(Protocol.Position position) => new(Math.Max(0, position.Line), Math.Max(0, position.Character));

    public static int ToOffset(TextSnapshot snapshot, Protocol.Position position) => snapshot.GetOffset(ToTextPosition(position));

    public static TextSpan ToSpan(TextSnapshot snapshot, Protocol.Range range)
    {
        var start = ToOffset(snapshot, range.Start);
        return TextSpan.FromBounds(start, Math.Max(start, ToOffset(snapshot, range.End)));
    }

    public static Protocol.Range ToRange(TextSnapshot snapshot, TextSpan span) => new(ToPosition(snapshot, span.Start), ToPosition(snapshot, span.End));

    /// <summary>Turns a server's text edits of a snapshot into changes, sorted by where they start.</summary>
    public static IReadOnlyList<TextChange> ToChanges(TextSnapshot snapshot, IEnumerable<Protocol.TextEdit> edits) =>
        WorkspaceEdits.ToChanges(snapshot, edits.Select(edit => (ToTextPosition(edit.Range.Start), ToTextPosition(edit.Range.End), edit.NewText)));

    /// <summary>Turns a server's workspace edit into Runesmith's, reading the files that are not open. Resource operations become file
    /// operations, and each text edit names its file as it is once they are made.</summary>
    /// <exception cref="LanguageFeatureException">A file is not a local file or cannot be read.</exception>
    public static Sdk.Languages.WorkspaceEdit ToWorkspaceEdit(Protocol.WorkspaceEdit edit, Sdk.Documents.IDocumentService documents)
    {
        if (edit.DocumentChanges is not { } changes)
        {
            return new Sdk.Languages.WorkspaceEdit([.. edit.Documents.Select(document => ToDocumentEdit(document.Uri, document.Edits, documents, null))]);
        }

        var operations = new List<FileOperation>();
        var texts = new List<(int After, Sdk.Languages.DocumentEdit Edit)>();
        foreach (var change in changes)
        {
            switch (change)
            {
                case Protocol.TextDocumentEdit text:
                    texts.Add((operations.Count, ToDocumentEdit(text.TextDocument.Uri, text.Edits, documents, Source(ToLocalPath(text.TextDocument.Uri), operations))));
                    break;
                case Protocol.CreateFile create:
                    operations.Add(new CreateFileOperation(ToLocalPath(create.Uri))
                    {
                        Overwrite = create.Options?.Overwrite ?? false,
                        IgnoreIfExists = create.Options?.IgnoreIfExists ?? false,
                    });
                    break;
                case Protocol.RenameFile rename:
                    operations.Add(new RenameFileOperation(ToLocalPath(rename.OldUri), ToLocalPath(rename.NewUri))
                    {
                        Overwrite = rename.Options?.Overwrite ?? false,
                        IgnoreIfExists = rename.Options?.IgnoreIfExists ?? false,
                    });
                    break;
                case Protocol.DeleteFile delete:
                    operations.Add(new DeleteFileOperation(ToLocalPath(delete.Uri))
                    {
                        Recursive = delete.Options?.Recursive ?? false,
                        IgnoreIfMissing = delete.Options?.IgnoreIfNotExists ?? false,
                    });
                    break;
            }
        }

        return new Sdk.Languages.WorkspaceEdit([.. texts.Select(t => t.Edit with { FilePath = FinalPath(t.Edit.FilePath, operations, t.After) })])
        {
            FileOperations = operations,
        };
    }

    private static Sdk.Languages.DocumentEdit ToDocumentEdit(string uri, IReadOnlyList<Protocol.TextEdit> edits, Sdk.Documents.IDocumentService documents, string? source)
    {
        var path = ToLocalPath(uri);
        var basis = source is null ? TextSnapshot.Create("") : WorkspaceEdits.ReadBasis(documents, source)
            ?? throw new LanguageFeatureException($"{Path.GetFileName(path)} could not be read.");
        return new Sdk.Languages.DocumentEdit(path, ToChanges(basis, edits)) { Snapshot = source is not null && documents.Find(source) is not null ? basis : null };
    }

    private static string ToLocalPath(string uri) =>
        LspUri.ToPath(uri) ?? throw new LanguageFeatureException($"The language server changes {uri}, which is not a local file.");

    private static bool FileExists(string path) => File.Exists(path) || Directory.Exists(path);

    private static bool IsInside(string path, string folder) =>
        path.StartsWith(folder, StringComparison.Ordinal) && (path.Length == folder.Length || path[folder.Length] == Path.DirectorySeparatorChar);

    // Where a file the server names after some operations is on disk now, or null for a file one of them creates.
    private static string? Source(string path, List<FileOperation> operations)
    {
        for (var i = operations.Count - 1; i >= 0; i--)
        {
            switch (operations[i])
            {
                case CreateFileOperation create when string.Equals(create.FilePath, path, StringComparison.Ordinal) && (create.Overwrite || !FileExists(path)):
                    return null;
                case RenameFileOperation rename when IsInside(path, rename.NewPath):
                    path = rename.OldPath + path[rename.NewPath.Length..];
                    break;
            }
        }

        return path;
    }

    // Follows a path through the renames that come after the text edit that named it.
    private static string FinalPath(string path, List<FileOperation> operations, int after)
    {
        foreach (var operation in operations.Skip(after))
        {
            if (operation is RenameFileOperation rename && IsInside(path, rename.OldPath))
                path = rename.NewPath + path[rename.OldPath.Length..];
        }

        return path;
    }

    /// <summary>Turns a change set into the protocol's incremental changes, last change first, so positions in the old text stay right while
    /// the server applies them one after another.</summary>
    public static IReadOnlyList<Protocol.TextDocumentContentChangeEvent> ToContentChanges(TextChangeSet changeSet)
    {
        var before = changeSet.Before;
        var changes = new List<Protocol.TextDocumentContentChangeEvent>(changeSet.Changes.Count);
        for (var i = changeSet.Changes.Count - 1; i >= 0; i--)
        {
            var change = changeSet.Changes[i];
            var range = new Protocol.Range(ToPosition(before, change.Span.Start), ToPosition(before, change.Span.End));
            changes.Add(new Protocol.TextDocumentContentChangeEvent(range, change.NewText));
        }

        return changes;
    }

    public static Diagnostic ToDiagnostic(string filePath, Protocol.Diagnostic diagnostic, string source) =>
        new(filePath, ToTextPosition(diagnostic.Range.Start), ToTextPosition(diagnostic.Range.End), ToSeverity(diagnostic.Severity), diagnostic.Message,
            diagnostic.Source ?? source)
        {
            Code = diagnostic.Code,
        };

    public static DocumentLocation? ToLocation(Protocol.Location location) =>
        LspUri.ToPath(location.Uri) is { } path ? new DocumentLocation(path, ToTextPosition(location.Range.Start), ToTextPosition(location.Range.End)) : null;

    public static CompletionItemKind ToKind(Protocol.CompletionItemKind? kind) =>
        kind is { } value && (int)value is >= 1 and <= 25 ? (CompletionItemKind)((int)value - 1) : CompletionItemKind.Text;

    public static string? ToMarkdown(Protocol.MarkupContent? content) =>
        content is null || string.IsNullOrWhiteSpace(content.Value) ? null
        : content.Kind == Protocol.MarkupKind.Markdown ? content.Value
        : EscapeMarkdown(content.Value);

    /// <summary>Removes snippet syntax, such as <c>${1:name}</c> and <c>$0</c>, keeping the placeholders' text.</summary>
    public static string StripSnippet(string snippet) =>
        SnippetPlaceholder().Replace(SnippetTabStop().Replace(snippet, ""), match => match.Groups[1].Value).Replace("\\$", "$", StringComparison.Ordinal);

    private static DiagnosticSeverity ToSeverity(Protocol.DiagnosticSeverity? severity) => severity switch
    {
        Protocol.DiagnosticSeverity.Warning => DiagnosticSeverity.Warning,
        Protocol.DiagnosticSeverity.Information => DiagnosticSeverity.Information,
        Protocol.DiagnosticSeverity.Hint => DiagnosticSeverity.Hint,
        _ => DiagnosticSeverity.Error,
    };

    private static string EscapeMarkdown(string text) => MarkdownSpecial().Replace(text, @"\$0");

    [GeneratedRegex(@"\$\{\d+:([^}]*)\}")]
    private static partial Regex SnippetPlaceholder();

    [GeneratedRegex(@"\$(\d+|\{\d+\})")]
    private static partial Regex SnippetTabStop();

    [GeneratedRegex(@"[\\`*_{}\[\]<>#|]")]
    private static partial Regex MarkdownSpecial();
}
