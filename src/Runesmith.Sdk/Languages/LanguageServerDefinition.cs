namespace Runesmith.Sdk.Languages;

/// <summary>Describes a language server Runesmith starts for some languages, and what to tell the user when it is missing.</summary>
/// <remarks>Export one with <c>[Export(typeof(LanguageServerDefinition))]</c>. Runesmith starts the server the first time a document of one of its
/// languages opens in a folder, and gives its features to the editor: completion, hover, go to definition, signature help and diagnostics.</remarks>
/// <param name="Id">A unique id, such as <c>csharp-ls</c>; settings refer to it.</param>
/// <param name="Name">The name shown in the status bar and the Output panel, such as "C# (csharp-ls)".</param>
/// <param name="LanguageIds">The languages the server handles.</param>
/// <param name="Command">The executable, found on the PATH unless it is a full path; the user can change it in the settings.</param>
public sealed record LanguageServerDefinition(string Id, string Name, IReadOnlyList<string> LanguageIds, string Command)
{
    /// <summary>Gets the command-line arguments.</summary>
    public IReadOnlyList<string> Arguments { get; init; } = [];

    /// <summary>Gets what the user is told to do when the command cannot be started, such as an install command.</summary>
    public string? InstallHint { get; init; }

    /// <summary>Gets file name patterns that mark a project's root, such as <c>*.sln</c>; the server starts in the nearest folder above a document
    /// that has one, or in the open folder.</summary>
    public IReadOnlyList<string> RootMarkers { get; init; } = [];

    /// <summary>Gets the JSON the server receives as <c>initializationOptions</c>, or null.</summary>
    public string? InitializationOptions { get; init; }
}
