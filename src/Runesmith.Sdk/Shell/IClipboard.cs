namespace Runesmith.Sdk.Shell;

/// <summary>Reads and writes the system clipboard's text.</summary>
/// <remarks>Its members can be called from any thread. Added in plugin API 0.1.2.</remarks>
public interface IClipboard
{
    /// <summary>Gets the clipboard's text, or null when it holds no text or cannot be read.</summary>
    Task<string?> GetTextAsync(CancellationToken cancellationToken = default);

    /// <summary>Puts text on the clipboard, replacing what it held.</summary>
    Task SetTextAsync(string text, CancellationToken cancellationToken = default);
}
