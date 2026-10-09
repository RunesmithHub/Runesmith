using Avalonia.Data.Converters;
using Runesmith.Sdk.Shell;

namespace Runesmith.Shell.Views;

/// <summary>Turns a status bar item's state into the classes that color its icon.</summary>
public static class StatusConverters
{
    public static FuncValueConverter<StatusBarState, bool> IsSuccess { get; } = new(state => state == StatusBarState.Success);

    public static FuncValueConverter<StatusBarState, bool> IsWarning { get; } = new(state => state == StatusBarState.Warning);

    public static FuncValueConverter<StatusBarState, bool> IsError { get; } = new(state => state == StatusBarState.Error);
}
