namespace Runesmith.Sdk.Analyzers.Tests;

/// <summary>The Runesmith repository the tests run in.</summary>
internal static class Repository
{
    public static string Root { get; } = Find();

    private static string Find()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Runesmith.slnx")))
                return directory.FullName;
        }

        throw new InvalidOperationException("The tests do not run inside the Runesmith repository.");
    }
}

/// <summary>Compares text with a file kept in the repository; with RUNESMITH_UPDATE_SNAPSHOTS=1, writes the file instead.</summary>
internal static class Snapshot
{
    public static void Match(string path, string actual)
    {
        actual = actual.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (Environment.GetEnvironmentVariable("RUNESMITH_UPDATE_SNAPSHOTS") == "1" || !File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, actual);
            Assert.Fail($"Wrote the snapshot {path}; check it and run the test again.");
        }

        Assert.Equal(File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal), actual);
    }
}
