namespace Runesmith.Sdk.Settings;

/// <summary>Reads and changes settings. A value set in the workspace overrides the user's, which overrides the default.</summary>
public interface ISettingsService
{
    /// <summary>Gets every known setting.</summary>
    IReadOnlyList<SettingDefinition> Definitions { get; }

    /// <summary>Gets a setting's value from the highest scope that sets it.</summary>
    /// <exception cref="KeyNotFoundException">No setting has the key.</exception>
    T Get<T>(string key);

    /// <summary>Gets the value set in one scope, or null when that scope does not set it.</summary>
    object? GetValue(string key, SettingScope scope);

    /// <summary>Gets the scope the setting's value comes from.</summary>
    SettingScope GetEffectiveScope(string key);

    /// <summary>Sets a value in a scope and saves that scope's file; a value outside the setting's limits is brought within them.</summary>
    /// <exception cref="InvalidOperationException">The scope is <see cref="SettingScope.Default"/>, or it is <see cref="SettingScope.Workspace"/>
    /// while no folder is open.</exception>
    /// <exception cref="UnauthorizedAccessException">A plugin changes a setting that is not its own: one whose key does not start with its id
    /// and a dot, and that it does not contribute.</exception>
    void Set(string key, object value, SettingScope scope = SettingScope.User);

    /// <summary>Removes the value a scope sets, so the setting falls back to the scope below.</summary>
    /// <exception cref="UnauthorizedAccessException">A plugin resets a setting that is not its own.</exception>
    void Reset(string key, SettingScope scope = SettingScope.User);

    /// <summary>Raised, on the UI thread, when a setting's value changes, including when a settings file changes on disk.</summary>
    event EventHandler<SettingChangedEventArgs>? Changed;
}

/// <summary>The setting whose value changed.</summary>
public sealed class SettingChangedEventArgs(string key) : EventArgs
{
    public string Key { get; } = key;
}
