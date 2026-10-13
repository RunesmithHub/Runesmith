using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Threading;
using HammerUI;
using HammerUI.Controls;
using Runesmith.Sdk.Commands;
using Runesmith.Sdk.Localization;
using Runesmith.Sdk.Settings;
using Runesmith.Shell.Appearance;
using Runesmith.Shell.Commands;
using Runesmith.Shell.Services;
using Runesmith.Shell.Views;
using Runesmith.Workspace.Files;

namespace Runesmith.Shell.Pages;

/// <summary>The screen Runesmith shows while no folder is open: the recent projects with a search and ways to start, the appearance, and the
/// plugin manager.</summary>
internal sealed class WelcomePage : Grid
{
    private readonly CommandService commands;
    private readonly RecentFolders recent;
    private readonly Workbench workbench;
    private readonly ISettingsService settings;
    private readonly ThemeService themes;
    private readonly Func<Control> createPlugins;
    private readonly ListBox navigation = new() { Classes = { "sidebar" }, Margin = new Thickness(12, 0) };
    private readonly ContentControl view = new();
    private readonly StackPanel projectList = new() { Spacing = 2 };
    private readonly StackPanel actions = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly SearchBox search = new SearchBox().Localize(SearchBox.PlaceholderTextProperty, () => Strings.SettingsWelcomeSearch);
    private readonly Button close;
    private Control? projects;
    private Control? customize;
    private Control? plugins;

    /// <param name="createPlugins">Creates the plugin manager the Plugins page shows.</param>
    /// <param name="closeWelcome">Returns to the workbench, while something is open behind the welcome screen.</param>
    public WelcomePage(CommandService commands, RecentFolders recent, Workbench workbench, ISettingsService settings, ThemeService themes, Func<Control> createPlugins, Action closeWelcome)
    {
        this.commands = commands;
        this.recent = recent;
        this.workbench = workbench;
        this.settings = settings;
        this.themes = themes;
        this.createPlugins = createPlugins;
        ColumnDefinitions = new ColumnDefinitions("232,*");
        this[!BackgroundProperty] = new DynamicResourceExtension("Surface1Brush");

        navigation.Items.Add(NavigationItem(() => Strings.SettingsWelcomeProjects, Icons.Folder));
        navigation.Items.Add(NavigationItem(() => Strings.SettingsWelcomeCustomize, Icons.Palette));
        navigation.Items.Add(NavigationItem(() => Strings.SettingsWelcomePlugins, Icons.Puzzle));
        navigation.SelectionChanged += (_, e) =>
        {
            switch (navigation.SelectedIndex)
            {
                case 0:
                    view.Content = projects ??= CreateProjects();
                    break;
                case 1:
                    view.Content = customize ??= CustomizeHost();
                    break;
                case 2:
                    view.Content = plugins ??= this.createPlugins();
                    break;
            }
        };

        var version = typeof(WelcomePage).Assembly.GetName().Version;
        var brand = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Margin = new Thickness(24, 28, 24, 24),
            Children =
            {
                new Logo(32),
                new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock { Text = Strings.ShellProduct, FontWeight = FontWeight.SemiBold, FontSize = 15 },
                        new TextBlock { Text = version is null ? "" : $"{version.Major}.{version.Minor}.{version.Build}", Classes = { "caption" } },
                    },
                },
            },
        };
        var side = new DockPanel { Children = { brand, navigation } };
        DockPanel.SetDock(brand, Dock.Top);
        var sideBorder = new Border { Child = side };
        sideBorder[!Border.BackgroundProperty] = new DynamicResourceExtension("Surface0Brush");

        close = new Button { Classes = { "icon" }, Content = new SymbolIcon { Data = Icons.X, Size = 16 }, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 16, 16, 0), IsVisible = false };
        close.Localize(ToolTip.TipProperty, () => Strings.SettingsWelcomeBack);
        close.Click += (_, _) => closeWelcome();

        var content = new Panel { Children = { new ScrollViewer { Content = view }, close } };
        SetColumn(content, 1);
        Children.AddRange([sideBorder, content]);
        navigation.SelectedIndex = 0;

        recent.Changed += (_, _) => UiThread.Run(FillProjects);
        search.PropertyChanged += (_, e) =>
        {
            if (e.Property == SearchBox.TextProperty)
                FillProjects();
        };
    }

    /// <summary>Shows the plugin manager.</summary>
    public void ShowPlugins() => navigation.SelectedIndex = 2;

    /// <summary>Gets whether the plugin manager is the page shown.</summary>
    public bool IsShowingPlugins => navigation.SelectedIndex == 2;

    /// <summary>Gets or sets whether the screen offers a way back to the workbench.</summary>
    public bool CanClose
    {
        get => close.IsVisible;
        set => close.IsVisible = value;
    }

    /// <summary>Says how long ago something happened, such as "3 hours ago", or the date when it is long ago.</summary>
    public static string Ago(DateTimeOffset when, DateTimeOffset now)
    {
        var elapsed = now - when;
        return elapsed.TotalMinutes < 1 ? Strings.SettingsAgoNow
            : elapsed.TotalHours < 1 ? Strings.SettingsAgoMinutes((int)elapsed.TotalMinutes)
            : elapsed.TotalDays < 1 ? Strings.SettingsAgoHours((int)elapsed.TotalHours)
            : elapsed.TotalDays < 2 ? Strings.SettingsAgoYesterday
            : elapsed.TotalDays < 30 ? Strings.SettingsAgoDays((int)elapsed.TotalDays)
            : Strings.SettingsAgoDate(when);
    }

    /// <summary>Writes a path under the user's home folder from <c>~</c>, as shells do.</summary>
    public static string HomeRelative(string path)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd(Path.DirectorySeparatorChar);
        return home.Length > 0 && path.StartsWith(home + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "~" + path[home.Length..] : path;
    }

    private static ListBoxItem NavigationItem(Func<string> text, Geometry icon) => new()
    {
        Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children = { new SymbolIcon { Data = icon, Size = 16 }, new TextBlock { VerticalAlignment = VerticalAlignment.Center }.Localize(TextBlock.TextProperty, text) },
        },
    };

    private StackPanel CreateProjects()
    {
        FillActions();
        commands.Changed += (_, _) => UiThread.Run(FillActions);
        Localized.Changed += (_, _) => FillProjects();

        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        DockPanel.SetDock(actions, Dock.Right);
        search.Margin = new Thickness(0, 0, 12, 0);
        bar.Children.AddRange([actions, search]);

        FillProjects();
        return new StackPanel
        {
            MaxWidth = 880,
            Margin = new Thickness(40, 32, 40, 32),
            Children =
            {
                new TextBlock { Classes = { "title" }, Margin = new Thickness(0, 0, 0, 20) }.Localize(TextBlock.TextProperty, () => Strings.SettingsWelcomeProjects),
                bar,
                projectList,
            },
        };
    }

    // After New Project and Open come the ways to get a project that plugins add, such as cloning a repository.
    private void FillActions()
    {
        actions.Children.Clear();
        if (commands.Find(CommandIds.NewProject) is not null)
            actions.Children.Add(ActionButton(Strings.SettingsWelcomeNewProject, CommandIds.NewProject, Icons.Plus, primary: true));
        actions.Children.Add(ActionButton(Strings.SettingsWelcomeOpen, CommandIds.OpenFolder, Icons.FolderOpen, primary: false));
        foreach (var command in MenuBuilder.ContributedCommands(commands, ContextMenus.Project))
            actions.Children.Add(ActionButton(command.Title, command.Id, Icons.Find(command.Icon) ?? Icons.FolderPlus, primary: false));
    }

    private Button ActionButton(string text, string commandId, Geometry icon, bool primary)
    {
        var button = new Button
        {
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new SymbolIcon { Data = icon, Size = 16 }, new TextBlock { Text = text } } },
        };
        if (primary)
            button.Classes.Add("accent");
        if (commands.GetKeyBinding(commandId) is { } gesture)
            ToolTip.SetTip(button, Strings.ToolsWithKeys(text, gesture.Split(" | ")[0]));
        button.Click += (_, _) => _ = commands.ExecuteAsync(commandId);
        return button;
    }

    private void FillProjects()
    {
        projectList.Children.Clear();
        var query = search.Text?.Trim() ?? "";
        var entries = recent.Entries
            .Where(e => query.Length == 0 || e.Path.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (entries.Count == 0)
        {
            projectList.Children.Add(new EmptyState
            {
                Icon = Icons.FolderOpen,
                Title = query.Length == 0 ? Strings.SettingsWelcomeNoRecent : Strings.SettingsWelcomeNoMatch,
                Hint = query.Length == 0 ? Strings.SettingsWelcomeNoRecentHint : Strings.SettingsWelcomeNoMatchHint,
                Margin = new Thickness(0, 32),
            });
            return;
        }

        var now = DateTimeOffset.Now;
        foreach (var entry in entries)
            projectList.Children.Add(ProjectRow(entry, now));
    }

    private Button ProjectRow(RecentFolder entry, DateTimeOffset now)
    {
        var name = Path.GetFileName(entry.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var tile = new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(8),
            Child = new TextBlock { Text = name.Length > 0 ? name[..1].ToUpperInvariant() : "?", FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        tile[!Border.BackgroundProperty] = new DynamicResourceExtension("SurfacePressedBrush");

        var text = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0),
            Children =
            {
                new TextBlock { Text = name, FontWeight = FontWeight.Medium, TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock { Text = HomeRelative(entry.Path), Classes = { "caption" }, TextTrimming = TextTrimming.PathSegmentEllipsis },
            },
        };
        var opened = new TextBlock { Text = entry.Opened is { } when ? Ago(when, now) : "", Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center };

        var row = new DockPanel();
        DockPanel.SetDock(tile, Dock.Left);
        DockPanel.SetDock(opened, Dock.Right);
        row.Children.AddRange([tile, opened, text]);
        var button = new Button
        {
            Classes = { "subtle" },
            Height = 52,
            Padding = new Thickness(10, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = row,
        };
        ToolTip.SetTip(button, Strings.SettingsWelcomeOpenPath(entry.Path));
        button.Click += (_, _) => _ = workbench.OpenFolderAsync(entry.Path);
        button.ContextMenu = new ContextMenu { Items = { RemoveItem(entry.Path) } };
        return button;
    }

    private MenuItem RemoveItem(string path)
    {
        var item = new MenuItem { Header = Strings.SettingsWelcomeRemoveRecent, Icon = new SymbolIcon { Data = Icons.Trash, Size = 16 } };
        item.Click += (_, _) => recent.Remove(path);
        return item;
    }

    // The page is built again when the appearance changes elsewhere, such as with Ctrl+Shift+T, so its pickers show what applies.
    private ContentControl CustomizeHost()
    {
        var host = new ContentControl { Content = CreateCustomize() };
        var pending = false;
        void Rebuild()
        {
            if (pending)
                return;
            pending = true;
            Dispatcher.UIThread.Post(() =>
            {
                pending = false;
                host.Content = CreateCustomize();
            });
        }

        themes.Changed += (_, _) => Rebuild();
        Localized.Changed += (_, _) => Rebuild();
        settings.Changed += (_, e) =>
        {
            if (e.Key is SettingKeys.Theme or SettingKeys.Accent or SettingKeys.ColorScheme or SettingKeys.FileIconTheme)
                Rebuild();
        };
        return host;
    }

    private StackPanel CreateCustomize()
    {
        var catalog = themes.Catalog;
        void Set(string key, string value) => settings.Set(key, value);

        var keymap = new ComboBox { ItemsSource = new[] { Strings.SettingsKeymapDefault }, SelectedIndex = 0, IsEnabled = false };
        ToolTip.SetTip(keymap, Strings.SettingsKeymapTip);

        return new StackPanel
        {
            MaxWidth = 760,
            Margin = new Thickness(40, 32, 40, 32),
            HorizontalAlignment = HorizontalAlignment.Left,
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = Strings.SettingsWelcomeCustomize, Classes = { "title" }, Margin = new Thickness(0, 0, 0, 20) },
                Row(Strings.SettingsCustomizeTheme, Strings.SettingsCustomizeThemeHint,
                    AppearancePickers.Picker(AppearancePickers.Themes(catalog), settings.Get<string>(SettingKeys.Theme), v => Set(SettingKeys.Theme, v)), 320),
                Row(Strings.SettingsCustomizeAccent, Strings.SettingsCustomizeAccentHint,
                    AppearancePickers.Accent(settings.Get<string>(SettingKeys.Accent), themes.Current.Palette.Accent, v => Set(SettingKeys.Accent, v)), double.NaN),
                Row(Strings.SettingsCustomizeSyntax, Strings.SettingsCustomizeSyntaxHint,
                    AppearancePickers.Picker(AppearancePickers.Schemes(catalog), settings.Get<string>(SettingKeys.ColorScheme), v => Set(SettingKeys.ColorScheme, v)), 320),
                Row(Strings.SettingsCustomizeIcons, Strings.SettingsCustomizeIconsHint,
                    AppearancePickers.Picker(AppearancePickers.IconThemes(catalog), settings.Get<string>(SettingKeys.FileIconTheme), v => Set(SettingKeys.FileIconTheme, v)), 320),
                Row(Strings.SettingsCustomizeUiFont, Strings.SettingsCustomizeUiFontHint, SizeBox(ShellSettings.UiFontSize, 11, 18)),
                Row(Strings.SettingsCustomizeEditorFont, Strings.SettingsCustomizeEditorFontHint, SizeBox(SettingKeys.FontSize, 10, 28)),
                Row(Strings.SettingsCustomizeKeymap, Strings.SettingsCustomizeKeymapHint, keymap),
            },
        };
    }

    private ComboBox SizeBox(string key, int from, int to)
    {
        var sizes = Enumerable.Range(from, to - from + 1).ToList();
        var box = new ComboBox { ItemsSource = sizes, SelectedItem = (int)Math.Round(settings.Get<double>(key)) };
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is int size)
                settings.Set(key, size);
        };
        return box;
    }

    private static Grid Row(string title, string hint, Control field, double width = 160)
    {
        var text = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children = { new TextBlock { Text = title, FontWeight = FontWeight.Medium }, new TextBlock { Text = hint, Classes = { "caption" }, TextWrapping = TextWrapping.Wrap } },
        };
        field.VerticalAlignment = VerticalAlignment.Center;
        field.Width = width;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,24,Auto"), MinHeight = 56 };
        SetColumn(field, 2);
        row.Children.AddRange([text, field]);
        return row;
    }
}
