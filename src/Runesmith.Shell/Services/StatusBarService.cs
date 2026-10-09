using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Composition;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using HammerUI;
using Runesmith.Sdk.Commands;
using Runesmith.Sdk.Shell;

namespace Runesmith.Shell.Services;

/// <summary>Keeps the status bar's items, sorted by side and priority.</summary>
[Export(typeof(IStatusBar))]
[Export]
[Shared]
[method: ImportingConstructor]
public sealed class StatusBarService(ICommandService commands) : IStatusBar
{
    /// <summary>Gets the items at the left end, outermost first.</summary>
    public ObservableCollection<StatusBarItem> Left { get; } = [];

    /// <summary>Gets the items at the right end, innermost first.</summary>
    public ObservableCollection<StatusBarItem> Right { get; } = [];

    public IStatusBarItem AddItem(string id, StatusBarAlignment alignment, int priority = 0)
    {
        var item = new StatusBarItem(id, alignment, priority, commands, Remove);
        UiThread.Run(() =>
        {
            var list = alignment == StatusBarAlignment.Left ? Left : Right;
            var index = alignment == StatusBarAlignment.Left
                ? list.TakeWhile(i => i.Priority >= priority).Count()
                : list.TakeWhile(i => i.Priority <= priority).Count();
            list.Insert(index, item);
        });
        return item;
    }

    private void Remove(StatusBarItem item) => UiThread.Run(() => (item.Alignment == StatusBarAlignment.Left ? Left : Right).Remove(item));
}

/// <summary>An item of the status bar, as the status bar shows it.</summary>
public sealed class StatusBarItem : IStatusBarItem, INotifyPropertyChanged
{
    private readonly ICommandService commands;
    private readonly Action<StatusBarItem> remove;
    private string text = "";
    private string? icon;
    private string? toolTip;
    private StatusBarState state;
    private string? commandId;
    private bool isVisible = true;

    internal StatusBarItem(string id, StatusBarAlignment alignment, int priority, ICommandService commands, Action<StatusBarItem> remove)
    {
        Id = id;
        Alignment = alignment;
        Priority = priority;
        this.commands = commands;
        this.remove = remove;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; }

    public StatusBarAlignment Alignment { get; }

    public int Priority { get; }

    public string Text
    {
        get => text;
        set => Set(ref text, value ?? "");
    }

    public string? Icon
    {
        get => icon;
        set
        {
            Set(ref icon, value);
            Changed(nameof(IconData));
        }
    }

    public string? ToolTip
    {
        get => toolTip;
        set => Set(ref toolTip, value);
    }

    public StatusBarState State
    {
        get => state;
        set
        {
            Set(ref state, value);
            Changed(nameof(IsBusy));
            Changed(nameof(IconData));
        }
    }

    public string? CommandId
    {
        get => commandId;
        set
        {
            Set(ref commandId, value);
            Changed(nameof(IsClickable));
        }
    }

    public bool IsVisible
    {
        get => isVisible;
        set => Set(ref isVisible, value);
    }

    /// <summary>Gets the icon to draw: the item's own, or a spinner while busy.</summary>
    public Geometry? IconData => State == StatusBarState.Busy ? Icons.Refresh : Icons.Find(icon);

    public bool IsBusy => State == StatusBarState.Busy;

    public bool IsClickable => commandId is not null;

    /// <summary>Runs the item's command.</summary>
    public void Click()
    {
        if (commandId is not null)
            _ = commands.ExecuteAsync(commandId);
    }

    public void Dispose() => remove(this);

    private void Set<T>(ref T field, T value, [CallerMemberName] string name = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        Changed(name);
    }

    private void Changed(string name) => UiThread.Run(() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)));
}
