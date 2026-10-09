namespace Runesmith.Sdk.Shell;

/// <summary>Tells whether the color theme that shows is dark or light, and switches between dark and light.</summary>
public interface IThemeService
{
    bool IsDark { get; }

    /// <summary>Switches to Runesmith's own light theme while a dark one shows, or to its dark theme otherwise, and remembers the choice.</summary>
    void Toggle();

    /// <summary>Raised, on the UI thread, after the theme, its colors, the accent or the syntax color scheme change.</summary>
    event EventHandler? Changed;
}
