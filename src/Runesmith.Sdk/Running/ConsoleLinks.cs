namespace Runesmith.Sdk.Running;

/// <summary>A place in a source file that a line of a program's output names, which the Run console and the terminal turn into a link.</summary>
/// <param name="Start">Where the link starts in the line.</param>
/// <param name="Length">The link's length.</param>
/// <param name="Path">The file's path as the line writes it: a full path, a path relative to the folder the program runs in or to the open
/// folder, or, with <paramref name="IsSourceRelative"/>, the end of a path, such as <c>com/example/Main.java</c>.</param>
/// <param name="Line">The line, counted from 1.</param>
/// <param name="Column">The column, counted from 1, or 0 when the line names none.</param>
/// <param name="IsSourceRelative">Whether <paramref name="Path"/> is the end of the path of a file somewhere in the open folder, as a stack
/// frame gives it that names a package and a file but not the folder its sources are in.</param>
/// <remarks>Added in plugin API 0.2.0.</remarks>
public sealed record ConsoleLink(int Start, int Length, string Path, int Line, int Column, bool IsSourceRelative = false);

/// <summary>Finds the places in source files that the output of a language's programs names, such as the frames of its stack traces. Export
/// it with <c>[Export(typeof(IConsoleLinkProvider))]</c>.</summary>
/// <remarks>Runesmith itself finds <c>path:line</c>, <c>path:line:column</c> and <c>path(line,column)</c>. It asks every provider for each
/// line the Run console and the terminal show, on the UI thread, so a provider must answer at once; a link shows only when its file exists.
/// Where two links overlap, the first provider's is kept, and providers come before Runesmith's own. Added in plugin API 0.2.0.</remarks>
public interface IConsoleLinkProvider
{
    /// <summary>Gets the places a line names; empty when it names none.</summary>
    IReadOnlyList<ConsoleLink> FindLinks(string line);
}
