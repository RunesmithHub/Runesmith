using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using HammerUI;
using HammerUI.Controls;
using Runesmith.Sdk.Commands;
using Runesmith.Sdk.Shell;
using Runesmith.Shell.Commands;
using Runesmith.Shell.Services;

namespace Runesmith.Shell.Views;

/// <summary>The main toolbar: the main menu, the project widget, search everywhere, the run area, the settings and the window's buttons.</summary>
public partial class MainWindow
{
    private const long DoubleShiftMilliseconds = 400;
    private const string PluginsCategory = "Plugins";
    private const double TitleBarDragThreshold = 4;

    private readonly MenuFlyout mainMenu = new() { Placement = PlacementMode.BottomEdgeAlignedLeft };
    private bool altAlone;
    private bool shiftAlone;
    private long lastShiftTap;
    private PointerPressedEventArgs? titleBarPress;
    private Point titleBarPressPoint;

    private void WireToolbar()
    {
        MainMenuButton.Flyout = mainMenu;
        ToolTip.SetTip(MainMenuButton, "Main menu (Alt)");

        ProjectButton.Flyout = MenuFlyouts.Dynamic(FillProjectMenu, PlacementMode.BottomEdgeAlignedLeft);

        SettingsButton.Flyout = MenuFlyouts.Dynamic(FillSettingsMenu, PlacementMode.BottomEdgeAlignedRight);
        ToolTip.SetTip(SettingsButton, "Settings, plugins and themes");

        SearchButton.Click += (_, _) => _ = commands!.ExecuteAsync(CommandIds.SearchEverywhere);
        SearchButton.SizeChanged += (_, e) => SearchKeys.IsVisible = e.NewSize.Width >= 280;
        UpdateSearchKeys();
    }

    // Leading widgets follow the project widget and trailing ones come before the run widget, spaced as the toolbar spaces its own.
    private void AddToolbarWidgets(ToolbarWidgets widgets)
    {
        var created = widgets.Create();
        LeftTools.Children.InsertRange(LeftTools.Children.IndexOf(ProjectButton) + 1, created[ToolbarSlot.Leading]);
        RightTools.Children.InsertRange(RightTools.Children.IndexOf(RunWidgetHost), created[ToolbarSlot.Trailing]);
    }

    private void BuildMenus()
    {
        mainMenu.Items.Clear();
        foreach (var menu in MenuBuilder.Build(commands!))
            mainMenu.Items.Add(menu);
        MainMenu.ItemsSource = MenuBuilder.Build(commands!);
    }

    private void ApplyMenuBarMode()
    {
        var separate = ShellSettings.ParseMenuBar(settings!.Get<string>(ShellSettings.MenuBar)) == MenuBarMode.Separate;
        MenuBarHost.IsVisible = separate;
        // A menu in the window takes Alt for itself, even while hidden.
        MenuBarHost.Child = separate ? MainMenu : null;
        MainMenuButton.IsVisible = !separate;
    }

    private void UpdateSearchKeys()
    {
        var gesture = commands!.GetKeyBinding(CommandIds.SearchEverywhere)?.Split(" | ")[0];
        SearchKeys.Text = "Shift Shift";
        ToolTip.SetTip(SearchButton, gesture is null ? "Search files, commands and settings (Shift Shift)" : $"Search files, commands and settings (Shift Shift or {gesture})");
    }

    private string Tip(string text, string commandId) => commands!.GetKeyBinding(commandId) is { } gesture ? $"{text} ({gesture.Split(" | ")[0]})" : text;

    private void UpdateProject()
    {
        var name = workspace!.Name;
        ProjectName.Text = name ?? "No folder";
        ProjectInitial.Text = string.IsNullOrEmpty(name) ? "R" : name[..1].ToUpperInvariant();
        ToolTip.SetTip(ProjectButton, workspace.RootPath);
        ProjectButton.IsVisible = workspace.RootPath is not null;
        RunWidgetHost.IsVisible = workspace.RootPath is not null;
        UpdateTitle();
    }

    private void FillProjectMenu(MenuFlyout menu)
    {
        var current = workspace!.RootPath;
        var folders = recent!.Items.Where(f => current is null || !string.Equals(f, current, StringComparison.Ordinal)).Take(8).ToList();
        if (folders.Count > 0)
        {
            menu.Items.Add(new MenuItem { Header = "Recent folders", IsEnabled = false, Classes = { "header" } });
            foreach (var folder in folders)
            {
                var item = new MenuItem
                {
                    Header = new StackPanel
                    {
                        Spacing = 0,
                        Children =
                        {
                            new TextBlock { Text = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar)) },
                            new TextBlock { Text = Pages.WelcomePage.HomeRelative(folder), Classes = { "caption" }, MaxWidth = 420, TextTrimming = TextTrimming.PathSegmentEllipsis },
                        },
                    },
                    Icon = new SymbolIcon { Data = Icons.Folder, Size = 16 },
                };
                item.Click += (_, _) => _ = workbench!.OpenFolderAsync(folder);
                menu.Items.Add(item);
            }

            menu.Items.Add(new Separator());
        }

        if (commands!.Find(CommandIds.NewProject) is not null)
            menu.Items.Add(CommandItem(CommandIds.NewProject, "New Project...", Icons.FilePlus));
        menu.Items.Add(CommandItem(CommandIds.OpenFolder, "Open Folder...", Icons.FolderOpen));
        if (commands.Find(CommandIds.CloneRepository) is not null)
            menu.Items.Add(CommandItem(CommandIds.CloneRepository, "Clone Repository...", Icons.Import));
        menu.Items.Add(CommandItem(CommandIds.CloseFolder, "Close Folder", Icons.X));
    }

    private void FillSettingsMenu(MenuFlyout menu)
    {
        menu.Items.Add(CommandItem(CommandIds.Settings, "Settings", Icons.Settings));
        var plugins = new MenuItem { Header = "Plugins", Icon = new SymbolIcon { Data = Icons.Puzzle, Size = 16 } };
        plugins.Click += (_, _) => pages!.ShowSettings(PluginsCategory);
        menu.Items.Add(plugins);
        if (commands!.Find(CommandIds.Sdks) is not null)
            menu.Items.Add(CommandItem(CommandIds.Sdks, "SDKs", Icons.Package));
        menu.Items.Add(CommandItem(CommandIds.KeyboardShortcuts, "Keyboard Shortcuts", Icons.Keyboard));
        menu.Items.Add(new Separator());
        menu.Items.Add(CommandItem(CommandIds.ToggleTheme, "Toggle Light and Dark Theme", Icons.Sun));
    }

    private CommandMenuItem CommandItem(string commandId, string header, Geometry icon)
    {
        var gestures = commands!.GetGestures(commandId);
        var item = new CommandMenuItem
        {
            Header = header,
            Icon = new SymbolIcon { Data = icon, Size = 16 },
            IsEnabled = commands.CanExecute(commandId),
            InputGesture = gestures.Count > 0 ? gestures[0] : null,
            GestureText = gestures.Count > 0 ? Commands.KeyBindings.Format(gestures[0]) : null,
        };
        item.Click += (_, _) => _ = commands.ExecuteAsync(commandId);
        return item;
    }

    // Alt alone opens the main menu, and Shift twice opens search everywhere; any other key between the press and the release cancels both.
    private void OnAnyKeyDown(object? sender, KeyEventArgs e)
    {
        var isAlt = e.Key is Key.LeftAlt or Key.RightAlt;
        var isShift = e.Key is Key.LeftShift or Key.RightShift;
        altAlone = isAlt && e.KeyModifiers == KeyModifiers.Alt;
        shiftAlone = isShift && e.KeyModifiers == KeyModifiers.Shift;
        if (!isShift)
            lastShiftTap = 0;
    }

    private void OnAnyKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.LeftAlt or Key.RightAlt)
        {
            var open = altAlone && MainMenuButton.IsVisible && !Dialogs.IsOpen;
            altAlone = false;
            if (open)
            {
                mainMenu.ShowAt(MainMenuButton);
                e.Handled = true;
            }
        }
        else if (e.Key is Key.LeftShift or Key.RightShift)
        {
            if (!shiftAlone)
                return;

            shiftAlone = false;
            var now = Environment.TickCount64;
            if (lastShiftTap > 0 && now - lastShiftTap <= DoubleShiftMilliseconds)
            {
                lastShiftTap = 0;
                if (!Dialogs.IsOpen)
                    _ = commands!.ExecuteAsync(CommandIds.SearchEverywhere);
            }
            else
            {
                lastShiftTap = now;
            }
        }
    }

    // Where the platform lets the window draw its own title bar, the toolbar is the title bar: drag it to move, double-click it to maximize.
    private void SetUpTitleBar()
    {
        if (OperatingSystem.IsMacOS() || OperatingSystem.IsBrowser() || ShellSettings.ParseTitleBar(settings!.Get<string>(ShellSettings.TitleBar)) == TitleBarMode.System)
            return;

        // Avalonia's drawn decorations on X11 would add a second title bar; the border and its resize grips are enough.
        if (OperatingSystem.IsLinux())
            WindowDecorations = WindowDecorations.BorderOnly;
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaTitleBarHeightHint = TitleBar.Height;
        TitleBar.PointerPressed += OnTitleBarPressed;
        TitleBar.PointerMoved += OnTitleBarMoved;
        TitleBar.PointerReleased += (_, _) => titleBarPress = null;
        MinimizeButton.Click += (_, _) => WindowState = WindowState.Minimized;
        MaximizeButton.Click += (_, _) => ToggleMaximized();
        CloseButton.Click += (_, _) => Close();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsExtendedIntoWindowDecorationsProperty || e.Property == WindowStateProperty)
                UpdateWindowButtons();
        };
        UpdateWindowButtons();
    }

    private void UpdateWindowButtons()
    {
        WindowButtons.IsVisible = IsExtendedIntoWindowDecorations;
        MaximizeIcon.Data = WindowState == WindowState.Maximized ? Icons.WindowRestore : Icons.WindowMaximize;
        ToolTip.SetTip(MaximizeButton, WindowState == WindowState.Maximized ? "Restore" : "Maximize");
        TitleBar.Padding = new Thickness(4, 0, 0, 0);
    }

    // Moving starts only once the pointer moves, since a window manager that takes the pointer on the press never reports the second click.
    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        titleBarPress = null;
        if (!IsExtendedIntoWindowDecorations || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        if (e.ClickCount == 2)
            ToggleMaximized();
        else
            (titleBarPress, titleBarPressPoint) = (e, e.GetPosition(this));
        e.Handled = true;
    }

    private void OnTitleBarMoved(object? sender, PointerEventArgs e)
    {
        if (titleBarPress is not { } press)
            return;

        var delta = e.GetPosition(this) - titleBarPressPoint;
        if (Math.Abs(delta.X) + Math.Abs(delta.Y) < TitleBarDragThreshold)
            return;

        titleBarPress = null;
        BeginMoveDrag(press);
    }

    private void ToggleMaximized() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void UpdateEmptyEditorHints()
    {
        if (!EmptyEditor.IsVisible || EmptyEditorHints.Children.Count > 0)
            return;

        foreach (var (text, commandId) in new[]
        {
            ("Search everywhere", CommandIds.SearchEverywhere),
            ("Go to file", CommandIds.QuickOpen),
            ("Find in files", CommandIds.FindInFiles),
            ("Show the Explorer", "view.explorer"),
            ("Command palette", CommandIds.CommandPalette),
        })
        {
            var keys = commandId == CommandIds.SearchEverywhere ? "Shift Shift" : commands!.GetKeyBinding(commandId)?.Split(" | ")[0];
            if (keys is null)
                continue;

            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("200,Auto"), Height = 28 };
            var label = new TextBlock { Text = text, Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 16, 0) };
            var badge = new ShortcutBadge { Text = keys, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(badge, 1);
            row.Children.AddRange([label, badge]);
            EmptyEditorHints.Children.Add(row);
        }
    }
}
