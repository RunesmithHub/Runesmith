using Avalonia.Controls;

namespace Runesmith.Sdk.Shell;

/// <summary>Where in the main toolbar a widget sits.</summary>
public enum ToolbarSlot
{
    /// <summary>On the left, after the project widget, such as a version control branch widget.</summary>
    Leading,

    /// <summary>On the right, before the run widget.</summary>
    Trailing,
}

/// <summary>Supplies a widget for the main toolbar. Export it with <c>[Export(typeof(IToolbarWidgetProvider))]</c>.</summary>
public interface IToolbarWidgetProvider
{
    /// <summary>Gets the slot the widget sits in.</summary>
    ToolbarSlot Slot { get; }

    /// <summary>Gets the widget's position among the widgets of its slot; lower comes first.</summary>
    int Order { get; }

    /// <summary>Creates the widget; called once, on the UI thread, when the main window opens. It should be a compact control 28 px high
    /// that hides itself while it has nothing to show.</summary>
    Control CreateWidget();
}
