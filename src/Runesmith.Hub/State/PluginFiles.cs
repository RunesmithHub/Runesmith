using RunesmithHub.Protocol;

namespace Runesmith.Hub.State;

/// <summary>Records an installed plugin's files and checks later that none changed, was added or went missing.</summary>
/// <remarks>A file whose size and last write time are as recorded counts as unchanged without hashing it; any other file is hashed.</remarks>
public static class PluginFiles
{
    /// <summary>Records every file under a plugin's folder.</summary>
    public static IReadOnlyList<InstalledFile> Record(string folder) =>
        [.. Enumerate(folder).Select(file => Describe(folder, file, Hash(file)))];

    /// <summary>Checks a plugin's folder against its recorded files.</summary>
    public static IntegrityResult Check(string folder, IReadOnlyList<InstalledFile> recorded)
    {
        ArgumentNullException.ThrowIfNull(recorded);
        if (!Directory.Exists(folder))
            return new IntegrityResult(["The plugin's folder is missing."], recorded);

        var problems = new List<string>();
        var updated = new List<InstalledFile>();
        var expected = recorded.ToDictionary(file => file.Path, StringComparer.Ordinal);
        foreach (var file in Enumerate(folder))
        {
            var relative = Relative(folder, file);
            if (!expected.Remove(relative, out var record))
            {
                problems.Add($"{relative} was added.");
                continue;
            }

            var info = new FileInfo(file);
            if (info.Length == record.Length && info.LastWriteTimeUtc == record.Modified)
            {
                updated.Add(record);
                continue;
            }

            var sha256 = Hash(file);
            if (info.Length != record.Length || !string.Equals(sha256, record.Sha256, StringComparison.Ordinal))
                problems.Add($"{relative} changed.");
            else
                updated.Add(record with { Modified = info.LastWriteTimeUtc });
        }

        problems.AddRange(expected.Keys.Order(StringComparer.Ordinal).Select(path => $"{path} is missing."));
        return new IntegrityResult(problems, updated);
    }

    private static IEnumerable<string> Enumerate(string folder) =>
        Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal);

    private static InstalledFile Describe(string folder, string file, string sha256)
    {
        var info = new FileInfo(file);
        return new InstalledFile(Relative(folder, file), info.Length, sha256, info.LastWriteTimeUtc);
    }

    private static string Relative(string folder, string file) => Path.GetRelativePath(folder, file).Replace(Path.DirectorySeparatorChar, '/');

    private static string Hash(string file)
    {
        using var stream = File.OpenRead(file);
        return Hashes.Sha256(stream);
    }
}

/// <summary>What a check of a plugin's files found.</summary>
/// <param name="Problems">Each file that changed, was added or is missing; empty when the files are as installed.</param>
/// <param name="Files">The recorded files with refreshed write times, for files that were hashed and found unchanged.</param>
public sealed record IntegrityResult(IReadOnlyList<string> Problems, IReadOnlyList<InstalledFile> Files)
{
    public bool IsIntact => Problems.Count == 0;
}
