using System.Composition;
using HammerUI;
using HammerUI.Docking;
using Runesmith.Sdk.Settings;
using Runesmith.Sdk.ToolWindows;
using Runesmith.Sdk.Workspace;
using Runesmith.Shell.Commands;
using Runesmith.Shell.Docking;
using Runesmith.Shell.Services;
using Runesmith.Shell.ToolWindows;

namespace Runesmith.Shell.Pages;

/// <summary>Opens Runesmith's own pages: the settings and the keyboard shortcuts in the editor area, and the welcome screen.</summary>
[Export]
[Shared]
public sealed class PageService
{
    public const string ShortcutsPanel = "shortcuts";

    private readonly ShellLayout layout;
    private readonly Lazy<IToolWindowManager> toolWindows;
    private readonly SearchToolWindow search;
    private readonly NotificationService notifications;
    private readonly RuntimeInfo runtime;
    private SettingsPage? settingsPage;
    private string? settingsCategory;

    [ImportingConstructor]
    public PageService(
        ShellLayout layout,
        Lazy<IToolWindowManager> toolWindows,
        SearchToolWindow search,
        NotificationService notifications,
        RuntimeInfo runtime,
        Lazy<CommandService> commands,
        Lazy<ISettingsService> settings,
        Lazy<IWorkspace> workspace,
        Lazy<Editors.EditorService> editors,
        Lazy<SdksSection> sdks,
        Lazy<Hub.HubService> hub,
        Lazy<ThemeService> themes,
        [ImportMany] IEnumerable<Lazy<ISettingsSectionProvider>> sections)
    {
        this.layout = layout;
        this.toolWindows = toolWindows;
        this.search = search;
        this.notifications = notifications;
        this.runtime = runtime;
        layout.AddFactory(id => id switch
        {
            ShellLayout.SettingsPanel => new DockablePanel(id, "Settings", () => Opened(new SettingsPage(settings.Value, workspace.Value, runtime, notifications, path => editors.Value.OpenAsync(path), () => sdks.Value.Create(), [.. sections], () => hub.Value.Show(Hub.PluginManagerTab.Installed), themes.Value))) { Icon = Icons.Settings },
            ShortcutsPanel => new DockablePanel(id, "Keyboard Shortcuts", () => new ShortcutsPage(commands.Value, notifications)) { Icon = Icons.Keyboard },
            _ => null,
        });
    }

    /// <summary>Raised when the welcome screen is asked for.</summary>
    public event EventHandler? WelcomeRequested;

    public void ShowWelcome() => WelcomeRequested?.Invoke(this, EventArgs.Empty);

    public void ShowSettings() => layout.AddToEditors(ShellLayout.SettingsPanel);

    /// <summary>Shows the settings at a category, such as "SDKs".</summary>
    public void ShowSettings(string category)
    {
        if (settingsPage is null)
            settingsCategory = category;
        else
            settingsPage.ShowCategory(category);
        layout.AddToEditors(ShellLayout.SettingsPanel);
    }

    private SettingsPage Opened(SettingsPage page)
    {
        settingsPage = page;
        if (settingsCategory is { } category)
            page.ShowCategory(category);
        settingsCategory = null;
        return page;
    }

    public void ShowKeyboardShortcuts() => layout.AddToEditors(ShortcutsPanel);

    /// <summary>Shows the Search panel and puts text in its search box, when given.</summary>
    public void ShowSearch(string? query)
    {
        toolWindows.Value.Show(SearchToolWindow.Id);
        search.Focus(query);
    }

    public Task ShowAboutAsync() => notifications.Dialogs.ShowAsync(AboutDialog.Create(runtime));
}
