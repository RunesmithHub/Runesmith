using System.Composition;
using HammerUI.Docking;

namespace Runesmith.Shell.Docking;

/// <summary>The editor area: where documents and Runesmith's pages dock, split and float, and the panels the dock host shows.</summary>
/// <remarks>Documents open in the group of the editor used last. The open documents are restored by the session, so the arrangement itself is
/// not saved.</remarks>
[Export]
[Shared]
public sealed class ShellLayout : IDockContentProvider
{
    public const string EditorsGroup = "editors";
    public const string SettingsPanel = "settings";
    private const string DocumentPrefix = "doc:";

    private readonly Dictionary<string, IDockPanel> panels = new(StringComparer.Ordinal);
    private readonly HashSet<string> served = new(StringComparer.Ordinal);
    private readonly List<Func<string, IDockPanel?>> factories = [];
    private DockHost? host;
    private string? lastEditorGroup;

    public ShellLayout()
    {
        Layout = new DockLayout(new DockGroup(EditorsGroup));
        Layout.Changed += OnChanged;
    }

    /// <summary>Gets the layout the editor area's dock host shows.</summary>
    public DockLayout Layout { get; }

    /// <summary>Gets the panel id of a document.</summary>
    public static string DocumentPanelId(string key) => DocumentPrefix + key;

    public static bool IsDocumentPanel(string panelId) => panelId.StartsWith(DocumentPrefix, StringComparison.Ordinal);

    /// <summary>Shows the layout and its panels in a dock host.</summary>
    public void Attach(DockHost dockHost)
    {
        host = dockHost;
        dockHost.ContentProvider = new Provider(this);
        dockHost.Layout = Layout;
    }

    /// <summary>Adds a way to create panels by id, such as Runesmith's pages.</summary>
    public void AddFactory(Func<string, IDockPanel?> factory) => factories.Add(factory);

    /// <summary>Makes a panel known by its id, such as a document's.</summary>
    public void Register(IDockPanel panel)
    {
        panels[panel.Id] = panel;
        // HammerUI 0.1.0 keeps the first panel it got for an id; a new provider makes it ask again.
        if (served.Remove(panel.Id) && host is not null)
            host.ContentProvider = new Provider(this);
    }

    public void Unregister(string panelId) => panels.Remove(panelId);

    public IDockPanel? GetPanel(string panelId)
    {
        if (!panels.TryGetValue(panelId, out var panel))
        {
            panel = factories.Select(factory => factory(panelId)).FirstOrDefault(created => created is not null);
            if (panel is null)
                return null;
            panels[panelId] = panel;
        }

        served.Add(panelId);
        return panel;
    }

    /// <summary>Shows a document or page in the editor area: in the group of the editor used last, or the first group of the window.</summary>
    public void AddToEditors(string panelId)
    {
        if (Layout.Contains(panelId))
        {
            Layout.EnsureVisible(panelId);
            return;
        }

        var target = lastEditorGroup is not null && Layout.FindNode(lastEditorGroup) is DockGroup last && Layout.FloatOf(last) is null ? last
            : Layout.FindNode(EditorsGroup) as DockGroup
            ?? DockedGroups().FirstOrDefault(g => g.Panels.Count > 0)
            ?? DockedGroups().First();
        Layout.MovePanel(panelId, target.Id);
        Layout.EnsureVisible(panelId);
    }

    private IEnumerable<DockGroup> DockedGroups() => Layout.Groups.Where(g => Layout.FloatOf(g) is null);

    private void OnChanged(object? sender, DockLayoutChangedEventArgs e)
    {
        if (Layout.FocusedGroup is { } group && group.Panels.Count > 0)
            lastEditorGroup = group.Id;
    }

    private sealed class Provider(ShellLayout layout) : IDockContentProvider
    {
        public IDockPanel? GetPanel(string panelId) => layout.GetPanel(panelId);
    }
}
