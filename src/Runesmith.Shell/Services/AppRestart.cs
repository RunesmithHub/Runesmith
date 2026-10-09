using System.Composition;

namespace Runesmith.Shell.Services;

/// <summary>Restarts Runesmith, normally or in safe mode, once the application has said how.</summary>
[Export]
[Shared]
public sealed class AppRestart
{
    /// <summary>Gets or sets what restarts Runesmith; its argument says whether to start in safe mode.</summary>
    public Action<bool>? Handler { get; set; }

    public bool CanRestart => Handler is not null;

    /// <summary>Closes Runesmith, asking about unsaved changes, and starts it again.</summary>
    public void Restart(bool safeMode = false) => Handler?.Invoke(safeMode);
}
