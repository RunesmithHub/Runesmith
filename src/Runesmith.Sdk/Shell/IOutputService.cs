namespace Runesmith.Sdk.Shell;

/// <summary>A named stream of text in the Output panel, such as a build's or a language server's log.</summary>
/// <remarks>Its members can be called from any thread.</remarks>
public interface IOutputChannel
{
    string Name { get; }

    void Append(string text);

    void AppendLine(string line);

    void Clear();

    /// <summary>Shows the Output panel with this channel selected.</summary>
    void Show();
}

/// <summary>Gives out channels of the Output panel.</summary>
public interface IOutputService
{
    /// <summary>Gets the channel with this name, creating it the first time.</summary>
    IOutputChannel GetChannel(string name);
}
