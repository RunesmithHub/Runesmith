namespace Runesmith.Sdk.Commands;

/// <summary>Finds and runs commands, and tells which key each one is bound to.</summary>
public interface ICommandService
{
    /// <summary>Gets every registered command.</summary>
    IReadOnlyList<CommandDefinition> Commands { get; }

    /// <summary>Finds a command by id, or returns null.</summary>
    CommandDefinition? Find(string commandId);

    /// <summary>Whether the command exists and can run now.</summary>
    bool CanExecute(string commandId, object? argument = null);

    /// <summary>Runs a command if it can run; returns whether it ran.</summary>
    Task<bool> ExecuteAsync(string commandId, object? argument = null);

    /// <summary>Gets the key gesture the command is bound to now, after the user's own bindings, such as <c>Ctrl+S</c>, or null.</summary>
    string? GetKeyBinding(string commandId);

    /// <summary>Raised when commands are added or bindings change, so menus and lists can refresh.</summary>
    event EventHandler? Changed;
}
