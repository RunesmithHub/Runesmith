using Runesmith.Git.Git;
using Runesmith.Sdk.VersionControl;

namespace Runesmith.Git.Tests.Git;

/// <summary>A Git repository in a temporary folder with a fixed identity, deleted when disposed.</summary>
internal sealed class TempRepository : IDisposable
{
    private TempRepository(string path, GitRepository repository)
    {
        Path = path;
        Repository = repository;
    }

    public string Path { get; }

    public GitRepository Repository { get; }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>Creates a repository on <c>main</c>, without commits.</summary>
    public static async Task<TempRepository> CreateAsync(IEnumerable<IGitCredentialSource>? credentialSources = null)
    {
        var path = Directory.CreateTempSubdirectory("runesmith-git-").FullName;
        await GitProcess.RunCheckedAsync(path, ["init", "--quiet", "--initial-branch=main"], Token);
        await ConfigureAsync(path);
        return await OpenAsync(path, credentialSources);
    }

    /// <summary>Creates a bare repository to serve as a remote.</summary>
    public static async Task<string> CreateBareAsync()
    {
        var path = Directory.CreateTempSubdirectory("runesmith-remote-").FullName;
        await GitProcess.RunCheckedAsync(path, ["init", "--quiet", "--bare", "--initial-branch=main"], Token);
        return path;
    }

    /// <summary>Clones a repository, such as a bare remote.</summary>
    public static async Task<TempRepository> CloneAsync(string source)
    {
        var path = Directory.CreateTempSubdirectory("runesmith-clone-").FullName;
        // The checkout happens during the clone, before ConfigureAsync, so line endings must be settled here too.
        await GitProcess.RunCheckedAsync(path, ["clone", "--quiet", "--config", "core.autocrlf=false", source, "."], Token);
        await ConfigureAsync(path);
        return await OpenAsync(path, null);
    }

    public Task<string> GitAsync(params string[] arguments) => GitProcess.RunCheckedAsync(Path, arguments, Token);

    public void Write(string relativePath, string content)
    {
        var full = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    public string Read(string relativePath) => File.ReadAllText(System.IO.Path.Combine(Path, relativePath));

    public bool Exists(string relativePath) => File.Exists(System.IO.Path.Combine(Path, relativePath));

    /// <summary>Writes a file, stages everything and commits; returns the commit's hash.</summary>
    public async Task<string> CommitFileAsync(string relativePath, string content, string message)
    {
        Write(relativePath, content);
        await GitAsync("add", "--all");
        await GitAsync("commit", "--quiet", "-m", message);
        return (await GitAsync("rev-parse", "HEAD")).Trim();
    }

    public void Dispose() => Delete(Path);

    /// <summary>Deletes a folder that Git wrote, whose object files are read-only on some systems.</summary>
    public static void Delete(string path)
    {
        if (!Directory.Exists(path))
            return;
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(path, recursive: true);
    }

    private static async Task<TempRepository> OpenAsync(string path, IEnumerable<IGitCredentialSource>? credentialSources)
    {
        var repository = await GitRepository.DiscoverAsync(path, credentialSources is null ? null : new GitCredentialResolver(credentialSources), Token) ?? throw new InvalidOperationException("Not a repository.");
        return new TempRepository(path, repository);
    }

    private static async Task ConfigureAsync(string path)
    {
        foreach (var (key, value) in new[]
        {
            ("user.name", "Ada Lovelace"), ("user.email", "ada@example.com"), ("commit.gpgsign", "false"), ("tag.gpgsign", "false"),
            ("core.autocrlf", "false"), ("core.hooksPath", ".git/hooks"), ("gc.auto", "0"),
        })
        {
            await GitProcess.RunCheckedAsync(path, ["config", key, value], Token);
        }
    }
}
