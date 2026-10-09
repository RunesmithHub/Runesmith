namespace Runesmith.Lsp;

/// <summary>A language server could not be started or stopped working.</summary>
public sealed class LanguageServerException : Exception
{
    public LanguageServerException()
    {
    }

    public LanguageServerException(string message)
        : base(message)
    {
    }

    public LanguageServerException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
