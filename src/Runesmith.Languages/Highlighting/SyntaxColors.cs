using System.Composition;
using Runesmith.Sdk.Appearance;

namespace Runesmith.Languages.Highlighting;

/// <summary>The syntax color scheme every highlighter colors tokens with; open editors restyle when it changes.</summary>
[Export]
[Shared]
[method: ImportingConstructor]
public sealed class SyntaxColors(TextMateGrammars grammars)
{
    private volatile ColorScheme current = BuiltInColorSchemes.Dark;

    public ColorScheme Current => current;

    /// <summary>Raised after <see cref="Current"/> changes, on the thread that changed it.</summary>
    public event EventHandler? Changed;

    /// <summary>Colors tokens with a scheme, or keeps the current one when the scheme's file cannot be read.</summary>
    /// <returns>Null, or why the scheme cannot be used.</returns>
    public string? Use(ColorScheme scheme)
    {
        ArgumentNullException.ThrowIfNull(scheme);
        if (scheme == current)
            return null;

        if (Check(scheme) is { } problem)
            return problem;

        current = scheme;
        Changed?.Invoke(this, EventArgs.Empty);
        return null;
    }

    /// <summary>Reads a scheme's file, and tells why it cannot be used, or null when it can.</summary>
    public string? Check(ColorScheme scheme)
    {
        ArgumentNullException.ThrowIfNull(scheme);
        try
        {
            grammars.GetRegistry(scheme.FilePath);
            return null;
        }
        catch (InvalidDataException exception)
        {
            return exception.Message;
        }
    }
}
