namespace Runesmith.Sdk.Shell;

/// <summary>How important a notification is.</summary>
public enum NotificationKind
{
    Info,
    Success,
    Warning,
    Error,
}

/// <summary>Tells the user something, with a toast that fades by itself, or asks them something with a dialog.</summary>
/// <remarks>Its members can be called from any thread.</remarks>
public interface INotificationService
{
    /// <summary>Shows a toast.</summary>
    /// <param name="actionText">The text of the toast's button, such as "Show output", or null for none.</param>
    /// <param name="action">Runs when the button is clicked.</param>
    void Notify(NotificationKind kind, string title, string? message = null, string? actionText = null, Action? action = null);

    /// <summary>Asks the user to confirm something; returns whether they did.</summary>
    Task<bool> ConfirmAsync(string title, string message, string confirmText = "OK", bool isDestructive = false);

    /// <summary>Asks the user to pick one of some choices; returns the index of the choice, or null when they cancel.</summary>
    Task<int?> ChooseAsync(string title, string message, IReadOnlyList<string> choices);
}
