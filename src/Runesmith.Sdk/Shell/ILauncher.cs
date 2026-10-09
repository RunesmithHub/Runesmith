namespace Runesmith.Sdk.Shell;

/// <summary>Hands things to the system: links to the browser, files to the file manager.</summary>
/// <remarks>A plugin needs the <c>process</c> capability in its manifest to use the launcher.</remarks>
public interface ILauncher
{
    /// <summary>Opens a web address in the default browser.</summary>
    void OpenUrl(Uri url);

    /// <summary>Shows a file or folder in the system's file manager.</summary>
    void Reveal(string path);
}
