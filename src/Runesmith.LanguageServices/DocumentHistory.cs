using Runesmith.Text;

namespace Runesmith.LanguageServices;

/// <summary>The recent versions of one open document and the changes between them, so a result for an older version can be moved to the
/// newest one.</summary>
internal sealed class DocumentHistory
{
    private const int VersionsKept = 32;

    private readonly List<(SourceDocument Document, IReadOnlyList<TextChange> ChangesFromPrevious)> versions = [];

    public DocumentHistory(SourceDocument document) => versions.Add((document, []));

    public SourceDocument Latest
    {
        get
        {
            lock (versions)
                return versions[^1].Document;
        }
    }

    public SourceDocument Add(TextSnapshot snapshot, IReadOnlyList<TextChange> changes, string? languageId = null)
    {
        lock (versions)
        {
            var latest = versions[^1].Document;
            var document = new SourceDocument(latest.Path, languageId ?? latest.LanguageId, latest.Version + 1, snapshot);
            versions.Add((document, changes));
            if (versions.Count > VersionsKept)
                versions.RemoveAt(0);
            return document;
        }
    }

    /// <summary>Finds a version that is still kept, or returns null.</summary>
    public SourceDocument? Find(int version)
    {
        lock (versions)
            return versions.Find(entry => entry.Document.Version == version).Document;
    }

    /// <summary>Finds the version whose text is this snapshot, or returns null.</summary>
    public SourceDocument? Find(TextSnapshot snapshot)
    {
        lock (versions)
        {
            for (var i = versions.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(versions[i].Document.Snapshot, snapshot))
                    return versions[i].Document;
            }
        }

        return null;
    }

    /// <summary>Moves an offset from one version to a later one; returns null when the older version is no longer kept.</summary>
    public int? MapOffset(int fromVersion, int toVersion, int offset)
    {
        lock (versions)
        {
            if (!Contains(fromVersion) || !Contains(toVersion) || toVersion < fromVersion)
                return null;

            foreach (var (document, changes) in versions)
            {
                if (document.Version > fromVersion && document.Version <= toVersion)
                    offset = MapThrough(changes, offset);
            }

            return offset;
        }
    }

    /// <summary>Moves a span from one version to a later one; returns null when the older version is no longer kept.</summary>
    public TextSpan? MapSpan(int fromVersion, int toVersion, TextSpan span)
    {
        if (MapOffset(fromVersion, toVersion, span.Start) is not { } start || MapOffset(fromVersion, toVersion, span.End) is not { } end)
            return null;

        return TextSpan.FromBounds(start, Math.Max(start, end));
    }

    private bool Contains(int version) => versions[0].Document.Version <= version && version <= versions[^1].Document.Version;

    // An offset inside replaced text, or where text is inserted, moves to the end of the new text.
    private static int MapThrough(IReadOnlyList<TextChange> changes, int offset)
    {
        var delta = 0;
        foreach (var change in changes)
        {
            if (offset < change.Span.Start)
                break;

            if (offset < change.Span.End)
                return change.Span.Start + delta + change.NewText.Length;

            delta += change.NewText.Length - change.Span.Length;
        }

        return offset + delta;
    }
}
