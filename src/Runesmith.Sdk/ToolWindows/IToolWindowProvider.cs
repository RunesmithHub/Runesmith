using Avalonia.Controls;

namespace Runesmith.Sdk.ToolWindows;

/// <summary>Supplies a tool window. Export it with <c>[Export(typeof(IToolWindowProvider))]</c>.</summary>
/// <remarks>Runesmith adds a View menu item and a command for each tool window, and creates the content only when the window first shows.</remarks>
public interface IToolWindowProvider
{
    ToolWindowDefinition Definition { get; }

    /// <summary>Creates the window's content; called once, on the UI thread, the first time the window shows. The control is kept while the
    /// window is moved, floated or hidden.</summary>
    Control CreateContent();
}

/// <summary>Shows, hides and focuses tool windows.</summary>
public interface IToolWindowManager
{
    /// <summary>Gets the tool windows that exist.</summary>
    IReadOnlyList<ToolWindowDefinition> ToolWindows { get; }

    /// <summary>Shows a tool window where it was last docked, or on its default side, and brings it to the front.</summary>
    void Show(string toolWindowId);

    void Hide(string toolWindowId);

    bool IsVisible(string toolWindowId);

    /// <summary>Shows a short text, such as a count, on the window's stripe button, or removes it with null; any thread may call it.</summary>
    void SetBadge(string toolWindowId, string? badge);

    /// <summary>Takes a window and its stripe button away while it does not apply, such as a version control window outside a repository, or
    /// brings them back; windows are available until this says otherwise. Any thread may call it.</summary>
    /// <remarks>A window that showed when it was taken away shows again when it comes back, unless another one took its place.</remarks>
    void SetAvailable(string toolWindowId, bool available);
}
