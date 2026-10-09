using System.Collections.Concurrent;
using Runesmith.Languages.Java.Semantics;
using Runesmith.Languages.Java.Syntax;
using Runesmith.LanguageServices;
using Runesmith.Text;

namespace Runesmith.Languages.Java.Analysis;

/// <summary>The open Java documents: each one's syntax tree, updated from the edits that made each version, and the project it is part of.</summary>
/// <remarks>A change only records its edits; the tree is brought up to date by the next request, so typing never waits for parsing.</remarks>
internal sealed class JavaDocuments
{
    private readonly ConcurrentDictionary<string, DocumentState> documents = new(SourceLibrary.PathComparer);

    public void Open(SourceDocument document) => documents[document.Path] = new DocumentState(document);

    public void Change(SourceDocument document, IReadOnlyList<TextChange> changes)
    {
        if (documents.TryGetValue(document.Path, out var state))
            state.Change(document, changes);
        else
            Open(document);
    }

    /// <summary>Forgets a document and returns the project it was part of, if any.</summary>
    public JavaProjectModel? Close(string path) => documents.TryRemove(path, out var state) ? state.Project : null;

    /// <summary>Gets the semantic model of a document version in its project, adding the newest version of the document to the project's
    /// sources first.</summary>
    public SemanticModel? ModelFor(SourceDocument document, JavaProjectModel project)
    {
        if (!documents.TryGetValue(document.Path, out var state))
            return null;

        var (file, isLatest) = state.Sync(document, project);
        var snapshot = project.Snapshot;
        return isLatest ? snapshot.ModelFor(file) : new SemanticModel(snapshot, file);
    }

    /// <summary>One open document: its newest tree, the edits not parsed yet, and recent versions' trees for requests that lag behind.</summary>
    private sealed class DocumentState(SourceDocument opened)
    {
        private const int RecentVersions = 4;

        private readonly Lock gate = new();
        private readonly List<(SourceDocument Document, IReadOnlyList<TextChange> Changes)> pending = [];
        private readonly Dictionary<int, SourceFile> recent = [];
        private SourceDocument latest = opened;
        private SourceFile? file;
        private JavaParseOptions? options;

        public JavaProjectModel? Project { get; private set; }

        public void Change(SourceDocument document, IReadOnlyList<TextChange> changes)
        {
            lock (gate)
            {
                pending.Add((document, changes));
                latest = document;
            }
        }

        /// <summary>Parses what changed since the last request, puts the newest version in the project, and returns the file for a version.</summary>
        public (SourceFile File, bool IsLatest) Sync(SourceDocument document, JavaProjectModel project)
        {
            lock (gate)
            {
                if (file is null || options != project.ParseOptions)
                {
                    pending.Clear();
                    recent.Clear();
                    options = project.ParseOptions;
                    file = Remember(latest.Version, SourceFile.Create(latest.Path, JavaSyntaxTree.Parse(latest.Snapshot.GetText(), options)));
                }

                foreach (var (version, changes) in pending)
                    file = Remember(version.Version, SourceFile.Create(version.Path, Reparse(file.Tree, version.Snapshot.GetText(), changes)));
                pending.Clear();

                if (Project != project || project.Snapshot.Sources.FindFile(file.Path) != file)
                {
                    if (Project is not null && Project != project)
                        Project.RemoveFile(file.Path);
                    Project = project;
                    project.SetFile(file);
                }

                if (document.Version == latest.Version)
                    return (file, true);
                if (recent.TryGetValue(document.Version, out var older))
                    return (older, false);
                return (SourceFile.Create(document.Path, JavaSyntaxTree.Parse(document.Snapshot.GetText(), options)), false);
            }
        }

        private SourceFile Remember(int version, SourceFile parsed)
        {
            recent[version] = parsed;
            if (recent.Count > RecentVersions)
                recent.Remove(recent.Keys.Min());
            return parsed;
        }

        // Edits arrive in the editor's coordinates and line endings; the snapshot stores \n, so inserted text is normalized the same way.
        private static JavaSyntaxTree Reparse(JavaSyntaxTree tree, string newText, IReadOnlyList<TextChange> changes)
        {
            var edits = changes
                .Select(c => new TextEdit(c.Span.Start, c.Span.Length, LineEndings.Normalize(c.NewText ?? "")))
                .Where(e => e.Length > 0 || e.NewText.Length > 0)
                .OrderBy(e => e.Start)
                .ToList();
            var length = tree.Text.Length + edits.Sum(e => e.NewText.Length - e.Length);
            var ordered = edits.Zip(edits.Skip(1)).All(pair => pair.First.End <= pair.Second.Start);
            return length == newText.Length && ordered && edits.All(e => e.End <= tree.Text.Length)
                ? tree.WithChanges(newText, edits)
                : JavaSyntaxTree.Parse(newText, tree.Options);
        }
    }
}
