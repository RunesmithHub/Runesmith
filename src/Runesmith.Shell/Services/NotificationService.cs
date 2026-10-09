using System.Collections.ObjectModel;
using System.Composition;
using HammerUI.Controls;
using HammerUI.Services;
using Runesmith.Sdk.Shell;

namespace Runesmith.Shell.Services;

/// <summary>Shows toasts and dialogs on the main window, and keeps the notifications shown for the history behind the status bar's bell.</summary>
[Export(typeof(INotificationService))]
[Export]
[Shared]
public sealed class NotificationService : INotificationService
{
    private const int HistoryLimit = 100;

    private readonly WindowToastService toasts;

    public NotificationService()
    {
        Host = new WindowHost();
        Dialogs = new DialogService(Host);
        toasts = new WindowToastService(Host);
    }

    /// <summary>Gets the host the main window attaches its dialog and toast layers to.</summary>
    public WindowHost Host { get; }

    /// <summary>Gets the dialogs of the main window, which plugins import as HammerUI's <see cref="IDialogService"/>.</summary>
    [Export(typeof(IDialogService))]
    public DialogService Dialogs { get; }

    /// <summary>Gets the toasts of the main window, which plugins import as HammerUI's <see cref="IToastService"/>; unlike
    /// <see cref="Notify"/>, they stay out of the notification history.</summary>
    [Export(typeof(IToastService))]
    public IToastService Toasts => toasts;

    /// <summary>Gets the notifications shown since Runesmith started, newest first; changed on the UI thread.</summary>
    public ObservableCollection<NotificationEntry> History { get; } = [];

    /// <summary>Gets whether a notification came since the history was last looked at.</summary>
    public bool HasUnread { get; private set; }

    /// <summary>Raised, on the UI thread, when the history or <see cref="HasUnread"/> changes.</summary>
    public event EventHandler? HistoryChanged;

    /// <summary>Marks the history as seen.</summary>
    public void MarkRead()
    {
        HasUnread = false;
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearHistory()
    {
        History.Clear();
        MarkRead();
    }

    public void Notify(NotificationKind kind, string title, string? message = null, string? actionText = null, Action? action = null)
    {
        var toastKind = kind switch
        {
            NotificationKind.Success => ToastKind.Success,
            NotificationKind.Warning => ToastKind.Warning,
            NotificationKind.Error => ToastKind.Error,
            _ => ToastKind.Info,
        };
        var duration = kind is NotificationKind.Warning or NotificationKind.Error || action is not null ? TimeSpan.FromSeconds(10) : (TimeSpan?)null;
        var toastAction = actionText is not null && action is not null ? new ToastAction(actionText, action) : null;
        toasts.Show(title, message, toastKind, duration, toastAction);
        UiThread.Run(() =>
        {
            History.Insert(0, new NotificationEntry(kind, title, message, DateTime.Now));
            while (History.Count > HistoryLimit)
                History.RemoveAt(History.Count - 1);
            HasUnread = true;
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    public Task<bool> ConfirmAsync(string title, string message, string confirmText = "OK", bool isDestructive = false) =>
        RunOnUiThread(() => Dialogs.ConfirmAsync(title, message, confirmText, isDestructive));

    public Task<int?> ChooseAsync(string title, string message, IReadOnlyList<string> choices) =>
        RunOnUiThread(async () =>
        {
            var buttons = choices.Select((choice, index) => new MessageDialogButton(choice, index, IsDefault: index == choices.Count - 1)).ToList();
            buttons.Insert(0, new MessageDialogButton("Cancel", null, IsCancel: true));
            return await Dialogs.ShowMessageAsync(title, message, buttons) as int?;
        });

    private static Task<T> RunOnUiThread<T>(Func<Task<T>> show) =>
        Avalonia.Threading.Dispatcher.UIThread.CheckAccess() ? show() : Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(show);
}

/// <summary>A notification as the history shows it.</summary>
public sealed record NotificationEntry(NotificationKind Kind, string Title, string? Message, DateTime Time);
