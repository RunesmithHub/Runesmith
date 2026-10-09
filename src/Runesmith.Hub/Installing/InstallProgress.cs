namespace Runesmith.Hub.Installing;

/// <summary>The steps of an install, in order.</summary>
public enum InstallStep
{
    Downloading,
    Verifying,
    Unpacking,
    Checking,
    Committing,
    Done,
}

/// <summary>How far an install is.</summary>
/// <param name="Plugin">The name of the plugin the step works on, when it works on one.</param>
public sealed record InstallProgress(InstallStep Step, string? Plugin = null);

/// <summary>An install plan could not be applied; nothing changed.</summary>
public sealed class InstallException : Exception
{
    public InstallException()
    {
    }

    public InstallException(string message)
        : base(message)
    {
    }

    public InstallException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public InstallException(string message, string? detail)
        : base(message) => Detail = detail;

    /// <summary>Gets what exactly failed, for the log.</summary>
    public string? Detail { get; }
}
