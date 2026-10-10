using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Media;
using HammerUI.Docking;
using Runesmith.Editor;
using Runesmith.Sdk.Documents;
using Runesmith.Shell.Appearance;
using Runesmith.Shell.Views;

namespace Runesmith.Shell.Docking;

/// <summary>The tab of an open document: its name, a dot while it has unsaved changes, the path under the tabs, and its editor.</summary>
internal sealed class DocumentPanel : IDockPanel, INotifyPropertyChanged, IDisposable
{
    private readonly FileIcons icons;
    private readonly PathBreadcrumbs breadcrumbs;
    private readonly DockPanel layout;
    private Control? notice;
    private string? description;
    private bool wasModified;

    public DocumentPanel(string id, TextEditor editor, FileIcons icons, PathBreadcrumbs breadcrumbs)
    {
        Id = id;
        Editor = editor;
        this.icons = icons;
        this.breadcrumbs = breadcrumbs;
        var bar = new Border { Classes = { "breadcrumbs" }, Child = breadcrumbs };
        DockPanel.SetDock(bar, Dock.Top);
        layout = new DockPanel { Children = { bar, editor } };
        Content = layout;
        wasModified = editor.Document.IsModified;
        editor.Document.PropertyChanged += OnDocumentChanged;
        icons.Changed += OnIconsChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; private set; }

    public TextEditor Editor { get; }

    public string Title => Editor.Document.Name;

    public Geometry? Icon => FileIcon.Data;

    public IBrush? IconBrush => FileIcon.Brush;

    public bool IsIconFilled => FileIcon.IsFilled;

    public bool CanClose => true;

    public bool IsModified => Editor.Document.IsModified;

    public string? ToolTip => Editor.Document.FilePath;

    /// <summary>Gets or sets the folder shown after the name, when another open file has the same name.</summary>
    public string? Description
    {
        get => description;
        set
        {
            if (description == value)
                return;
            description = value;
            Changed(nameof(Description));
        }
    }

    public Control Content { get; }

    /// <summary>Gets or sets whether the file's path shows under the tabs.</summary>
    public bool ShowsBreadcrumbs
    {
        get => breadcrumbs.Parent is Control { IsVisible: true };
        set
        {
            if (breadcrumbs.Parent is Control bar)
                bar.IsVisible = value;
        }
    }

    /// <summary>Shows a line over the editor, under the path, in place of the one shown before.</summary>
    public void ShowNotice(Control line)
    {
        HideNotice();
        notice = line;
        DockPanel.SetDock(line, Dock.Top);
        layout.Children.Insert(1, line);
    }

    public void HideNotice()
    {
        if (notice is not null)
            layout.Children.Remove(notice);
        notice = null;
    }

    public void Dispose()
    {
        Editor.Document.PropertyChanged -= OnDocumentChanged;
        icons.Changed -= OnIconsChanged;
        Editor.Dispose();
    }

    private void OnDocumentChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(IDocument.IsModified) when Editor.Document.IsModified != wasModified:
                wasModified = Editor.Document.IsModified;
                Changed(nameof(IsModified));
                break;
            case nameof(IDocument.Name):
                Changed(nameof(Title));
                Changed(nameof(Icon));
                break;
            case nameof(IDocument.LanguageId):
                Changed(nameof(Icon));
                break;
        }
    }

    private ThemedIcon FileIcon => icons.For(Editor.Document.FilePath ?? Editor.Document.Name, languageId: Editor.Document.LanguageId);

    private void OnIconsChanged(object? sender, EventArgs e) => Changed(nameof(Icon));

    private void Changed(string name) => UiThread.Run(() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)));
}
