using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HammerUI;
using HammerUI.Controls;
using Runesmith.Sdk;
using Runesmith.Sdk.Settings;
using Runesmith.Sdk.Shell;
using Runesmith.Sdk.Workspace;
using Runesmith.Shell.Appearance;
using Runesmith.Shell.Services;
using Runesmith.Workspace.Settings;

namespace Runesmith.Shell.Pages;

/// <summary>The settings, by category, for the user or for the open folder, and the plugins.</summary>
internal sealed class SettingsPage : DockPanel
{
    private const string PluginsCategory = "Plugins";
    private const double SectionsWidth = 916;

    private readonly ISettingsService settings;
    private readonly IWorkspace workspace;
    private readonly RuntimeInfo runtime;
    private readonly NotificationService notifications;
    private readonly Func<string, Task> openFile;
    private readonly Func<Control>? sdksSection;
    private readonly Action? openPlugins;
    private readonly ThemeService? themes;
    private readonly List<ISettingsSectionProvider> pluginSections;
    private readonly ListBox categories = new() { Classes = { "sidebar" }, Width = 200, Margin = new Thickness(12, 8, 0, 12) };
    private readonly StackPanel sections = new() { Spacing = 24, Margin = new Thickness(28, 16, 28, 40) };
    private readonly SearchBox search = new() { PlaceholderText = "Search settings", Width = 280 };
    private readonly SegmentedControl scope = new();
    private readonly DispatcherTimer refreshTimer;

    /// <param name="openPlugins">Shows the plugin manager.</param>
    public SettingsPage(ISettingsService settings, IWorkspace workspace, RuntimeInfo runtime, NotificationService notifications, Func<string, Task> openFile, Func<Control>? sdksSection = null,
        IEnumerable<Lazy<ISettingsSectionProvider>>? pluginSections = null, Action? openPlugins = null, ThemeService? themes = null)
    {
        this.themes = themes;
        this.sdksSection = sdksSection;
        this.openPlugins = openPlugins;
        this.pluginSections = Resolve(pluginSections ?? [], notifications);
        this.settings = settings;
        this.workspace = workspace;
        this.runtime = runtime;
        this.notifications = notifications;
        this.openFile = openFile;
        this[!BackgroundProperty] = new DynamicResourceExtension("SurfaceBrush");

        scope.Items.Add(new SegmentedControlItem { Content = "User" });
        scope.Items.Add(new SegmentedControlItem { Content = "Folder" });
        scope.SelectedIndex = 0;
        ToolTip.SetTip(scope, "User settings apply everywhere; folder settings apply to the open folder only and override the user's.");

        var openJson = new Button { Classes = { "subtle", "small" }, Content = "Open settings file" };
        openJson.Click += (_, _) => _ = OpenJsonAsync();
        var header = new DockPanel { Margin = new Thickness(28, 18, 28, 6) };
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { search, scope, openJson } };
        DockPanel.SetDock(tools, Dock.Right);
        header.Children.Add(tools);
        header.Children.Add(new TextBlock { Text = "Settings", FontSize = 22, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        DockPanel.SetDock(header, Dock.Top);
        Children.Add(header);

        DockPanel.SetDock(categories, Dock.Left);
        Children.Add(categories);
        // Every category fills the same width, up to a readable one, whatever its rows need.
        var column = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star) { MaxWidth = SectionsWidth } }, Children = { sections } };
        Children.Add(new ScrollViewer { Content = column });

        refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        refreshTimer.Tick += (_, _) =>
        {
            // Rebuilding the rows would take the focus from a field the user is still typing in, so wait until it loses the focus.
            if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox { IsKeyboardFocusWithin: true } box && sections.IsVisualAncestorOf(box))
                return;

            refreshTimer.Stop();
            Fill();
        };
        categories.SelectionChanged += (_, _) => Fill();
        search.PropertyChanged += (_, e) =>
        {
            if (e.Property == SearchBox.TextProperty)
                Fill();
        };
        scope.SelectionChanged += (_, _) => Fill();
        settings.Changed += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            refreshTimer.Stop();
            refreshTimer.Start();
        });
        workspace.Changed += (_, _) => UpdateScope();

        categories.ItemsSource = Categories();
        categories.SelectedIndex = 0;
        UpdateScope();
    }

    /// <summary>Shows a category, clearing the search.</summary>
    public void ShowCategory(string category)
    {
        search.Text = "";
        categories.SelectedItem = category;
    }

    private SettingScope Scope => scope.SelectedIndex == 1 && workspace.RootPath is not null ? SettingScope.Workspace : SettingScope.User;

    private List<string> Categories()
    {
        var names = settings.Definitions.Where(d => d.Category != PluginsCategory && d.Category != SdksSection.Category).Select(d => d.Category)
            .Concat(pluginSections.Select(p => p.Category)).Distinct().ToList();
        string[] order = ["Appearance", "Editor", "Files", "Window", "Languages"];
        names.Sort((a, b) =>
        {
            var ia = Array.IndexOf(order, a);
            var ib = Array.IndexOf(order, b);
            return (ia < 0 ? 100 : ia) != (ib < 0 ? 100 : ib) ? (ia < 0 ? 100 : ia).CompareTo(ib < 0 ? 100 : ib) : string.CompareOrdinal(a, b);
        });
        if (sdksSection is not null)
            names.Add(SdksSection.Category);
        names.Add(PluginsCategory);
        return names;
    }

    private void UpdateScope()
    {
        if (scope.Items[1] is SegmentedControlItem folder)
            folder.IsEnabled = workspace.RootPath is not null;
        if (workspace.RootPath is null)
            scope.SelectedIndex = 0;
        Fill();
    }

    private void Fill()
    {
        sections.Children.Clear();
        var query = search.Text?.Trim() ?? "";
        var shown = query.Length > 0 ? Categories() : [categories.SelectedItem as string ?? "Appearance"];
        foreach (var category in shown)
        {
            if (category == SdksSection.Category)
            {
                if (query.Length == 0 || category.Contains(query, StringComparison.OrdinalIgnoreCase))
                    sections.Children.Add(sdksSection!());
                continue;
            }

            if (category == PluginsCategory)
            {
                if (PluginsSection(query) is { } plugins)
                    sections.Children.Add(plugins);
                continue;
            }

            var rows = settings.Definitions
                .Where(d => d.Category == category && d.Key != ShellSettings.DisabledPlugins)
                .Where(d => query.Length == 0 || Matches(d, query))
                .Select(Row)
                .ToList();
            var own = query.Length == 0 || category.Contains(query, StringComparison.OrdinalIgnoreCase) ? PluginSections(category) : [];
            if (rows.Count == 0 && own.Count == 0)
                continue;

            var section = rows.Count > 0 ? Section(category, rows) : Section(category, null);
            section.Children.InsertRange(1, own);
            sections.Children.Add(section);
        }

        if (sections.Children.Count == 0)
            sections.Children.Add(new EmptyState { Icon = Icons.Search, Title = "No settings match", Hint = "Try other words, or clear the search." });
    }

    private static bool Matches(SettingDefinition definition, string query) =>
        definition.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
        || definition.Key.Contains(query, StringComparison.OrdinalIgnoreCase)
        || definition.Description?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;

    private static StackPanel Section(string title, IEnumerable<Control>? rows)
    {
        var section = new StackPanel { Spacing = 10, Children = { new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeight.SemiBold } } };
        if (rows is not null)
        {
            var card = new Border { Classes = { "card" }, Child = new StackPanel { Spacing = 4, Children = { } } };
            ((StackPanel)card.Child).Children.AddRange(rows);
            section.Children.Add(card);
        }

        return section;
    }

    private static List<ISettingsSectionProvider> Resolve(IEnumerable<Lazy<ISettingsSectionProvider>> providers, NotificationService notifications)
    {
        var resolved = new List<ISettingsSectionProvider>();
        foreach (var provider in providers)
        {
            try
            {
                _ = provider.Value.Category;
                resolved.Add(provider.Value);
            }
            catch (Exception exception)
            {
                notifications.Notify(NotificationKind.Error, "A plugin's settings section could not be added", exception.Message);
            }
        }

        return resolved;
    }

    // A plugin's section that fails to build is replaced by its error, and the category's settings still show.
    private List<Control> PluginSections(string category)
    {
        var controls = new List<Control>();
        foreach (var provider in pluginSections.Where(p => p.Category == category))
        {
            try
            {
                controls.Add(provider.CreateSection());
            }
            catch (Exception exception)
            {
                controls.Add(new TextBlock { Text = $"This section could not be shown: {exception.Message}", Classes = { "muted" }, TextWrapping = TextWrapping.Wrap });
            }
        }

        return controls;
    }

    private SettingRow Row(SettingDefinition definition)
    {
        var target = Scope;
        var effective = settings.GetEffectiveScope(definition.Key);
        var current = settings.GetValue(definition.Key, target) ?? (effective == SettingScope.Workspace && target == SettingScope.User
            ? settings.GetValue(definition.Key, SettingScope.User) ?? definition.DefaultValue
            : GetEffective(definition));

        var description = definition.Description ?? "";
        if (target == SettingScope.User && effective == SettingScope.Workspace)
            description += " The open folder sets its own value, which wins.";
        var userOnly = target == SettingScope.Workspace && CoreSettings.UserOnly.Contains(definition.Key);
        if (userOnly)
            description += " Switch to User to change it.";
        description += $"  ({definition.Key})";

        var editor = Editor(definition, current, value => Set(definition, value, target));
        editor.IsEnabled = !userOnly;
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { editor } };
        if (settings.GetValue(definition.Key, target) is not null)
        {
            var reset = new Button { Classes = { "icon", "small" }, Content = new SymbolIcon { Data = Icons.RotateCcw, Size = 14 } };
            ToolTip.SetTip(reset, target == SettingScope.Workspace ? "Remove the folder's value" : "Reset to the default");
            reset.Click += (_, _) => settings.Reset(definition.Key, target);
            content.Children.Insert(0, reset);
        }

        return new SettingRow { Title = definition.Title, Description = description, Content = content };
    }

    private object GetEffective(SettingDefinition definition) =>
        settings.GetValue(definition.Key, SettingScope.Workspace) ?? settings.GetValue(definition.Key, SettingScope.User) ?? definition.DefaultValue;

    private Control Editor(SettingDefinition definition, object value, Action<object> set)
    {
        switch (definition.DefaultValue)
        {
            case bool:
            {
                var toggle = new ToggleSwitch { IsChecked = value is true, OnContent = null, OffContent = null };
                toggle.IsCheckedChanged += (_, _) => set(toggle.IsChecked == true);
                return toggle;
            }

            case int or double:
            {
                var isInteger = definition.DefaultValue is int;
                var field = new NumberField
                {
                    Value = Convert.ToDouble(value, CultureInfo.InvariantCulture),
                    Minimum = definition.Minimum ?? double.MinValue,
                    Maximum = definition.Maximum ?? double.MaxValue,
                    IsInteger = isInteger,
                    Width = 110,
                };
                var debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
                debounce.Tick += (_, _) =>
                {
                    debounce.Stop();
                    set(isInteger ? (int)Math.Round(field.Value) : field.Value);
                };
                field.PropertyChanged += (_, e) =>
                {
                    if (e.Property == NumberField.ValueProperty)
                    {
                        debounce.Stop();
                        debounce.Start();
                    }
                };
                return field;
            }

            case string when definition.Key == SettingKeys.Accent:
                return AppearancePickers.Accent((string)value, themes?.Current.Palette.Accent ?? Color.Parse("#7073F6"), v => set(v));

            case string when themes is not null && AppearanceChoices(definition.Key) is { } appearance:
                return AppearancePickers.Picker(appearance, (string)value, v => set(v));

            case string when definition.Choices is { Count: > 0 } choices:
            {
                var box = new ComboBox
                {
                    ItemsSource = choices,
                    SelectedItem = value as string,
                    MinWidth = 160,
                    ItemTemplate = new FuncDataTemplate<string?>((choice, _) => new TextBlock { Text = Display(choice) }),
                };
                box.SelectionChanged += (_, _) =>
                {
                    if (box.SelectedItem is string choice && choice != (string)value)
                        set(choice);
                };
                return box;
            }

            default:
            {
                var box = new TextBox { Text = value?.ToString(), MinWidth = 280 };
                void Commit()
                {
                    if (box.Text != value?.ToString())
                        set(box.Text ?? "");
                }

                box.LostFocus += (_, _) => Commit();
                box.KeyDown += (_, e) =>
                {
                    if (e.Key == Avalonia.Input.Key.Enter)
                        Commit();
                };
                return box;
            }
        }
    }

    // Choices are stored as written in the settings file, such as afterDelay or official-only, and shown as words, such as "After delay".
    internal static string Display(string? choice)
    {
        if (string.IsNullOrEmpty(choice))
            return "";

        var words = new System.Text.StringBuilder();
        foreach (var character in choice)
        {
            if (character == '-')
                words.Append(' ');
            else if (char.IsUpper(character) && words.Length > 0)
                words.Append(' ').Append(char.ToLowerInvariant(character));
            else
                words.Append(words.Length == 0 ? char.ToUpperInvariant(character) : character);
        }

        return choice is "lf" or "crlf" ? choice.ToUpperInvariant() : words.ToString();
    }

    private List<AppearanceChoice>? AppearanceChoices(string key) => key switch
    {
        SettingKeys.Theme => AppearancePickers.Themes(themes!.Catalog),
        SettingKeys.ColorScheme => AppearancePickers.Schemes(themes!.Catalog),
        SettingKeys.FileIconTheme => AppearancePickers.IconThemes(themes!.Catalog),
        _ => null,
    };

    private void Set(SettingDefinition definition, object value, SettingScope target)
    {
        try
        {
            settings.Set(definition.Key, value, target);
        }
        catch (InvalidOperationException exception)
        {
            notifications.Notify(NotificationKind.Error, $"{definition.Title} could not be changed", exception.Message);
        }
    }

    // The plugins themselves are in the plugin manager; this section holds the plugin hub's settings and the ways there.
    private StackPanel? PluginsSection(string query)
    {
        var rows = settings.Definitions
            .Where(d => d.Category == PluginsCategory && d.Key != ShellSettings.DisabledPlugins)
            .Where(d => query.Length == 0 || PluginsCategory.Contains(query, StringComparison.OrdinalIgnoreCase) || Matches(d, query))
            .Select(Row)
            .ToList<Control>();
        if (rows.Count == 0 && query.Length > 0)
            return null;

        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (openPlugins is not null)
        {
            var manage = new Button { Classes = { "small" }, Content = "Manage plugins" };
            manage.Click += (_, _) => openPlugins();
            tools.Children.Add(manage);
        }

        var folder = new Button { Classes = { "subtle", "small" }, Content = "Open the plugins folder" };
        folder.Click += (_, _) =>
        {
            Directory.CreateDirectory(RunesmithPaths.UserPlugins);
            Launcher.Reveal(RunesmithPaths.UserPlugins);
        };
        tools.Children.Add(folder);
        var section = Section(PluginsCategory, rows);
        section.Children.Add(tools);
        return section;
    }

    private async Task OpenJsonAsync()
    {
        var path = Scope == SettingScope.Workspace && workspace.RootPath is { } root ? RunesmithPaths.WorkspaceSettings(root) : RunesmithPaths.UserSettings;
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, "{\n}\n");
        }

        await openFile(path);
    }
}
