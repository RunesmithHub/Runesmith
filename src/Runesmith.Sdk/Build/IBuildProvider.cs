using Runesmith.Sdk.Shell;

namespace Runesmith.Sdk.Build;

/// <summary>The outcome of a build.</summary>
public sealed record BuildResult(bool Succeeded, int Errors, int Warnings, TimeSpan Duration);

/// <summary>What a build provider builds and where it reports.</summary>
/// <param name="RootPath">The open folder.</param>
/// <param name="Output">The channel the build writes its log to.</param>
/// <param name="ReportDiagnostic">Reports a problem the build found, as soon as it is found.</param>
public sealed record BuildContext(string RootPath, IOutputChannel Output, Action<Diagnostic> ReportDiagnostic);

/// <summary>Builds a kind of project, such as with <c>dotnet build</c>. Export it with <c>[Export(typeof(IBuildProvider))]</c>.</summary>
public interface IBuildProvider
{
    /// <summary>Gets the name shown while building, such as ".NET".</summary>
    string Name { get; }

    /// <summary>Whether the provider can build the folder, such as when it has a solution or project file.</summary>
    bool CanBuild(string rootPath);

    /// <summary>Builds; canceling the token stops the build.</summary>
    Task<BuildResult> BuildAsync(BuildContext context, CancellationToken cancellationToken);
}

/// <summary>Builds the open folder with the provider that can build it.</summary>
public interface IBuildService
{
    bool IsBuilding { get; }

    /// <summary>Whether a provider can build the open folder.</summary>
    bool CanBuild { get; }

    /// <summary>Builds the open folder, saving modified documents first; returns null when nothing can build it or a build runs already.</summary>
    Task<BuildResult?> BuildAsync();

    /// <summary>Stops the running build.</summary>
    void Cancel();

    /// <summary>Raised, on the UI thread, when a build starts or ends.</summary>
    event EventHandler? StateChanged;
}
