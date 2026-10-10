using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Media;
using HammerUI;
using HammerUI.Docking;
using Runesmith.Sdk.Editors;
using Runesmith.Shell.Appearance;

namespace Runesmith.Shell.Editors.Custom;

/// <summary>The tab of a file open in a custom editor: its name, the dot of unsaved changes and the editor's control.</summary>
internal sealed class CustomEditorPanel : IDockPanel, INotifyPropertyChanged, IDisposable
{
    private const string Prefix = "custom:";

    private readonly FileIcons icons;

    public CustomEditorPanel(string id, string filePath, EditorEntry entry, CustomEditor editor, FileIcons icons)
    {
        Id = id;
        FilePath = filePath;
        Entry = entry;
        Editor = editor;
        this.icons = icons;
        editor.StateChanged += OnStateChanged;
        icons.Changed += OnIconsChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; }

    public string FilePath { get; }

    public EditorEntry Entry { get; }

    public CustomEditor Editor { get; }

    public string Title => Path.GetFileName(FilePath);

    public Geometry? Icon => Entry.Definition.Icon is { } name && Icons.Find(name) is { } icon ? icon : FileIcon.Data;

    public IBrush? IconBrush => Entry.Definition.Icon is null ? FileIcon.Brush : null;

    public bool IsIconFilled => Entry.Definition.Icon is null && FileIcon.IsFilled;

    public bool CanClose => true;

    public bool IsModified => Editor.IsModified;

    public string? ToolTip => $"{FilePath} ({Entry.Definition.Name})";

    public Control Content => Editor.Content;

    /// <summary>Gets the change-on-disk watch of the file, while there is one.</summary>
    public IDisposable? Watch { get; set; }

    private static int created;

    /// <summary>Gets a new panel id for a file; each editor gets its own, since the dock host keeps a panel's content by its id.</summary>
    public static string NewId(string filePath) => $"{Prefix}{Interlocked.Increment(ref created)}:{filePath}";

    public static bool IsCustomEditorPanel(string panelId) => panelId.StartsWith(Prefix, StringComparison.Ordinal);

    public void Dispose()
    {
        Editor.StateChanged -= OnStateChanged;
        icons.Changed -= OnIconsChanged;
        Watch?.Dispose();
        Editor.Dispose();
    }

    private ThemedIcon FileIcon => icons.For(FilePath);

    private void OnStateChanged(object? sender, EventArgs e) => Changed(nameof(IsModified));

    private void OnIconsChanged(object? sender, EventArgs e) => Changed(nameof(Icon));

    private void Changed(string name) => UiThread.Run(() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)));
}
