using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Media;
using HammerUI;
using HammerUI.Docking;
using Runesmith.Sdk.Documents;

namespace Runesmith.Shell.Diffs;

/// <summary>The editor tab of a diff, with the dot of unsaved changes while its working copy side has them.</summary>
internal sealed class DiffPanel : IDockPanel, INotifyPropertyChanged
{
    private const string Prefix = "diff:";

    public DiffPanel(string title, DiffView view, IDocument? workingCopy, string? toolTip)
    {
        Id = IdOf(title);
        Title = title;
        View = view;
        WorkingCopy = workingCopy;
        ToolTip = toolTip;
        workingCopy?.PropertyChanged += OnWorkingCopyChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; }

    public string Title { get; }

    public Geometry? Icon => Icons.GitCompare;

    public bool CanClose => true;

    public bool IsModified => WorkingCopy?.IsModified ?? false;

    public string? ToolTip { get; }

    public Control Content => View;

    public DiffView View { get; }

    /// <summary>Gets the document the right side edits, or null when both sides are read-only.</summary>
    public IDocument? WorkingCopy { get; }

    public static string IdOf(string title) => Prefix + title;

    public static bool IsDiffPanel(string panelId) => panelId.StartsWith(Prefix, StringComparison.Ordinal);

    public void Dispose()
    {
        WorkingCopy?.PropertyChanged -= OnWorkingCopyChanged;
        View.Dispose();
    }

    private void OnWorkingCopyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IDocument.IsModified))
            UiThread.Run(() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsModified))));
    }
}
