namespace Runesmith.Sdk.Workspace;

/// <summary>Names files and folders of a tool that the Explorer, Quick Open and search leave out, such as a build's output folders. Export
/// it with <c>[Export(typeof(IFileExcludeContributor))]</c>.</summary>
/// <remarks>Runesmith leaves out nothing by itself: what is left out is the user's <c>files.exclude</c> setting and what the installed plugins
/// contribute here. It reads the globs once, when it starts. Added in plugin API 0.2.0.</remarks>
public interface IFileExcludeContributor
{
    /// <summary>Gets the globs to leave out, relative to the open folder, such as <c>**/bin</c>.</summary>
    IEnumerable<string> Globs { get; }
}
