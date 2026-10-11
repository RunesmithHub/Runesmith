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
using Runesmith.Sdk.Localization;
using Runesmith.Sdk.Settings;
using Runesmith.Sdk.Shell;
using Runesmith.Sdk.Workspace;
using Runesmith.Shell.Appearance;
using Runesmith.Shell.Localization;
using Runesmith.Shell.Services;
using Runesmith.Shell.Views;
using Runesmith.Workspace.Settings;

namespace Runesmith.Shell.Pages;

/// <summary>The settings, by category, for the user or for the open folder, and the plugins.</summary>
internal sealed class SettingsPage : DockPanel
{
    private const string PluginsCategory = "Plugins";
    private const double SectionsWidth = 916;
    private const double NarrowWidth = 760;

    private readonly ISettingsService settings;
    private readonly IWorkspace workspace;
    private readonly RuntimeInfo runtime;
    private readonly NotificationService notifications;
    private readonly Func<string, Task> openFile;
    private readonly Func<Control>? sdksSection;
    private readonly Action? openPlugins;
    private readonly ThemeService? themes;
    private readonly ShellLocalization? localization;
    private readonly List<ISettingsSectionProvider> pluginSections;
    private readonly ListBox categories = new() { Classes = { "sidebar" }, Width = 200, Margin = new Thickness(12, 8, 0, 12), ItemTemplate = CategoryTemplate };
    private readonly ComboBox categoryPicker = new()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch,
        Margin = new Thickness(28, 8, 28, 0),
        IsVisible = false,
        ItemTemplate = CategoryTemplate,
    };
    private readonly StackPanel sections = new() { Spacing = 24, Margin = new Thickness(28, 16, 28, 40) };
    private readonly SearchBox search = new SearchBox { Width = 280 }.Localize(SearchBox.PlaceholderTextProperty, () => Strings.SettingsSearch);
    private readonly SegmentedControl scope = new();
    private readonly DispatcherTimer refreshTimer;

    /// <param name="openPlugins">Shows the plugin manager.</param>
    public SettingsPage(ISettingsService settings, IWorkspace workspace, RuntimeInfo runtime, NotificationService notifications, Func<string, Task> openFile, Func<Control>? sdksSection = null,
        IEnumerable<Lazy<ISettingsSectionProvider>>? pluginSections = null, Action? openPlugins = null, ThemeService? themes = null, ShellLocalization? localization = null)
    {
        this.themes = themes;
        this.localization = localization;
        this.sdksSection = sdksSection;
        this.openPlugins = openPlugins;
        this.pluginSections = Resolve(pluginSections ?? [], notifications);
        this.settings = settings;
        this.workspace = workspace;
        this.runtime = runtime;
        this.notifications = notifications;
        this.openFile = openFile;
        this[!BackgroundProperty] = new DynamicResourceExtension("SurfaceBrush");

        scope.Items.Add(new SegmentedControlItem().Localize(ContentControl.ContentProperty, () => Strings.SettingsScopeUser));
        scope.Items.Add(new SegmentedControlItem().Localize(ContentControl.ContentProperty, () => Strings.SettingsScopeFolder));
        scope.SelectedIndex = 0;
        scope.Localize(ToolTip.TipProperty, () => Strings.SettingsScopeTip);

        var openJson = new Button { Classes = { "subtle", "small" } }.Localize(ContentControl.ContentProperty, () => Strings.SettingsOpenFile);
        openJson.Click += (_, _) => _ = OpenJsonAsync();
        var title = new TextBlock { FontSize = 22, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center }.Localize(TextBlock.TextProperty, () => Strings.SettingsTitle);
        var tools = new WrapPanel { ItemSpacing = 10, LineSpacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { search, scope, openJson } };
        var header = new SideBySidePanel { MinMainWidth = 140, Spacing = 16, Margin = new Thickness(28, 18, 28, 6), Children = { title, tools } };
        DockPanel.SetDock(header, Dock.Top);
        Children.Add(header);

        DockPanel.SetDock(categories, Dock.Left);
        Children.Add(categories);
        DockPanel.SetDock(categoryPicker, Dock.Top);
        Children.Add(categoryPicker);
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
        categories.SelectionChanged += (_, _) =>
        {
            categoryPicker.SelectedItem = categories.SelectedItem;
            Fill();
        };
        categoryPicker.SelectionChanged += (_, _) =>
        {
            if (categoryPicker.SelectedItem is { } chosen && !Equals(chosen, categories.SelectedItem))
                categories.SelectedItem = chosen;
        };
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
        localization?.Changed += (_, _) =>
        {
            refreshTimer.Stop();
            refreshTimer.Start();
        };

        var names = Categories();
        categories.ItemsSource = names;
        categoryPicker.ItemsSource = names;
        categories.SelectedIndex = 0;
        UpdateScope();
    }

    /// <summary>Shows a category, clearing the search.</summary>
    public void ShowCategory(string category)
    {
        search.Text = "";
        categories.SelectedItem = category;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != BoundsProperty)
            return;

        var narrow = Bounds.Width < NarrowWidth;
        categories.IsVisible = !narrow;
        categoryPicker.IsVisible = narrow;
    }

    private SettingScope Scope => scope.SelectedIndex == 1 && workspace.RootPath is not null ? SettingScope.Workspace : SettingScope.User;

    private List<string> Categories()
    {
        var names = settings.Definitions.Where(d => d.Category != PluginsCategory && d.Category != SdksSection.Category).Select(d => d.Category)
            .Concat(pluginSections.Select(p => p.Category)).Distinct().ToList();
        string[] order = ["Appearance", LocalizationSettings.Category, "Editor", "Files", "Window", "Languages"];
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
            var own = query.Length == 0 || CategoryTitle(category).Contains(query, StringComparison.OrdinalIgnoreCase) ? PluginSections(category) : [];
            if (rows.Count == 0 && own.Count == 0)
                continue;

            var section = rows.Count > 0 ? Section(category, rows) : Section(category, null);
            section.Children.InsertRange(1, own);
            sections.Children.Add(section);
        }

        if (sections.Children.Count == 0)
            sections.Children.Add(new EmptyState { Icon = Icons.Search, Title = Strings.SettingsNoMatch, Hint = Strings.SettingsNoMatchHint });
    }

    private static bool Matches(SettingDefinition definition, string query) =>
        definition.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
        || definition.Key.Contains(query, StringComparison.OrdinalIgnoreCase)
        || definition.Description?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;

    // A category is a key that stays the same in every language; this is its name in the language that shows.
    internal static string CategoryTitle(string? category) => category switch
    {
        "Appearance" => Strings.SettingsCategoryAppearance,
        LocalizationSettings.Category => LocalizationSettings.CategoryTitle,
        "Editor" => Strings.SettingsCategoryEditor,
        "Files" => Strings.SettingsCategoryFiles,
        "Window" => Strings.SettingsCategoryWindow,
        "Languages" => Strings.SettingsCategoryLanguages,
        "Plugins" => Strings.SettingsCategoryPlugins,
        "SDKs" => Strings.SettingsCategorySdks,
        "Terminal" => Strings.SettingsCategoryTerminal,
        "Run" => Strings.SettingsCategoryRun,
        "Testing" => Strings.SettingsCategoryTesting,
        _ => category ?? "",
    };

    private static FuncDataTemplate<string?> CategoryTemplate { get; } =
        new((category, _) => new TextBlock().Localize(TextBlock.TextProperty, () => CategoryTitle(category)), supportsRecycling: false);

    private static StackPanel Section(string category, IEnumerable<Control>? rows)
    {
        var title = new TextBlock { FontSize = 16, FontWeight = FontWeight.SemiBold }.Localize(TextBlock.TextProperty, () => CategoryTitle(category));
        var section = new StackPanel { Spacing = 10, Children = { title } };
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
                notifications.Notify(NotificationKind.Error, Strings.SettingsSectionFailed, exception.Message);
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
                controls.Add(new TextBlock { Text = Strings.SettingsSectionNotShown(exception.Message), Classes = { "muted" }, TextWrapping = TextWrapping.Wrap });
            }
        }

        return controls;
    }

    private Border Row(SettingDefinition definition)
    {
        var target = Scope;
        var effective = settings.GetEffectiveScope(definition.Key);
        var current = settings.GetValue(definition.Key, target) ?? (effective == SettingScope.Workspace && target == SettingScope.User
            ? settings.GetValue(definition.Key, SettingScope.User) ?? definition.DefaultValue
            : GetEffective(definition));

        var description = definition.Description ?? "";
        if (target == SettingScope.User && effective == SettingScope.Workspace)
            description += " " + Strings.SettingsFolderWins;
        var userOnly = target == SettingScope.Workspace && CoreSettings.UserOnly.Contains(definition.Key);
        if (userOnly)
            description += " " + Strings.SettingsUserOnly;
        description += $"  ({definition.Key})";

        var editor = Editor(definition, current, value => Set(definition, value, target));
        editor.IsEnabled = !userOnly;
        var content = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
        if (settings.GetValue(definition.Key, target) is not null)
        {
            var reset = new Button { Classes = { "icon", "small" }, Content = new SymbolIcon { Data = Icons.RotateCcw, Size = 14 } };
            ToolTip.SetTip(reset, target == SettingScope.Workspace ? Strings.SettingsRemoveFolderValue : Strings.SettingsReset);
            reset.Click += (_, _) => settings.Reset(definition.Key, target);
            reset.Margin = new Thickness(0, 0, 8, 0);
            DockPanel.SetDock(reset, Dock.Left);
            content.Children.Add(reset);
        }

        content.Children.Add(editor);

        return SettingCard(definition.Title, description, content);
    }

    /// <summary>A setting's row, with the control beside the title and description while they keep room to read, and under them when not.</summary>
    private static Border SettingCard(string title, string description, Control control)
    {
        var text = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Hub.HubVisuals.Themed(new TextBlock { Text = title, FontWeight = FontWeight.Medium, TextWrapping = TextWrapping.Wrap }, TextBlock.ForegroundProperty, "TextPrimaryBrush"));
        if (!string.IsNullOrEmpty(description))
            text.Children.Add(Hub.HubVisuals.Themed(new TextBlock { Text = description, FontSize = 12, TextWrapping = TextWrapping.Wrap }, TextBlock.ForegroundProperty, "TextMutedBrush"));

        control.HorizontalAlignment = HorizontalAlignment.Left;
        var card = new Border
        {
            Padding = new Thickness(14, 10),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            MinHeight = 56,
            Child = new SideBySidePanel { MinMainWidth = 260, Spacing = 16, Children = { text, control } },
        };
        return Hub.HubVisuals.Themed(Hub.HubVisuals.Themed(card, Border.BackgroundProperty, "SurfaceBrush"), Border.BorderBrushProperty, "BorderSubtleBrush");
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

            case string when definition.Key == SettingKeys.Language && localization is not null:
                return LanguagePicker.Create(localization, (string)value, v => set(v));

            case string when definition.Choices is { Count: > 0 } choices:
            {
                var box = new ComboBox
                {
                    ItemsSource = choices,
                    SelectedItem = value as string,
                    MinWidth = 160,
                    ItemTemplate = new FuncDataTemplate<string?>((choice, _) => new TextBlock { Text = ChoiceTitle(definition, choice) }),
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

    /// <summary>Gets the name a choice shows by: the one the setting gives it, or else its value as words.</summary>
    internal static string ChoiceTitle(SettingDefinition definition, string? choice)
    {
        if (definition.Choices is { } choices && definition.ChoiceTitles is { } titles)
        {
            for (var index = 0; index < Math.Min(choices.Count, titles.Count); index++)
            {
                if (choices[index] == choice)
                    return titles[index];
            }
        }

        return Display(choice);
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
            notifications.Notify(NotificationKind.Error, Strings.SettingsChangeFailed(definition.Title), exception.Message);
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
            var manage = new Button { Classes = { "small" }, Content = Strings.SettingsManagePlugins };
            manage.Click += (_, _) => openPlugins();
            tools.Children.Add(manage);
        }

        var folder = new Button { Classes = { "subtle", "small" }, Content = Strings.SettingsOpenPluginsFolder };
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
