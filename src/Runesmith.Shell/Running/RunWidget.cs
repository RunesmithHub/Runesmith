using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using HammerUI;
using HammerUI.Controls;
using Microsoft.VisualStudio.Composition;
using Runesmith.Sdk.Build;
using Runesmith.Sdk.Commands;
using Runesmith.Sdk.Running;
using Runesmith.Shell.Commands;

namespace Runesmith.Shell.Running;

/// <summary>The run widget of the main toolbar: the selected run configuration with a drop-down of all of them, and Run, Debug, Stop and
/// Build buttons whose tooltips show their keys.</summary>
public sealed class RunWidget : StackPanel
{
    private const int RecentCount = 3;

    private readonly IRunConfigurationService configurations;
    private readonly ICommandService commands;
    private readonly RunCommands runCommands;
    private readonly Button selector = new() { Classes = { "toolbar" }, Padding = new Thickness(6, 0, 8, 0) };
    private readonly SymbolIcon selectorIcon = new() { Size = 16, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock selectorName = new() { MaxWidth = 220, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button run;
    private readonly Button debug;
    private readonly Button stop;
    private readonly Button build;

    /// <summary>Creates the widget over the run services.</summary>
    public RunWidget(IRunConfigurationService configurations, RunService runs, RunCommands runCommands, ICommandService commands, IBuildService buildService)
    {
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(buildService);
        this.configurations = configurations;
        this.commands = commands;
        this.runCommands = runCommands;
        Orientation = Orientation.Horizontal;
        Spacing = 2;
        VerticalAlignment = VerticalAlignment.Center;

        var chevron = new SymbolIcon { Data = Icons.ChevronDown, Size = 12, VerticalAlignment = VerticalAlignment.Center };
        chevron.Bind(SymbolIcon.ForegroundProperty, chevron.GetResourceObservable("TextMutedBrush"));
        selector.Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { selectorIcon, selectorName, chevron } };
        selector.Flyout = Views.MenuFlyouts.Dynamic(FillMenu, PlacementMode.BottomEdgeAlignedLeft);

        run = CommandButton(Icons.Play, CommandIds.Run, "SuccessBrush");
        debug = CommandButton(Icons.Bug, CommandIds.Debug, null);
        stop = CommandButton(Icons.Stop, CommandIds.Stop, "DangerBrush");
        build = CommandButton(Icons.Hammer, CommandIds.Build, null);
        Children.AddRange([selector, run, debug, stop, build]);

        void Changed(object? sender, EventArgs e) => UiThread.Run(Update);
        configurations.Changed += Changed;
        runs.StateChanged += Changed;
        buildService.StateChanged += Changed;
        commands.Changed += Changed;
        Update();
    }

    /// <summary>Creates the widget from Runesmith's exports, for the main window.</summary>
    public static RunWidget Create(ExportProvider exports)
    {
        ArgumentNullException.ThrowIfNull(exports);
        return new RunWidget(exports.GetExportedValue<IRunConfigurationService>(), exports.GetExportedValue<RunService>(), exports.GetExportedValue<RunCommands>(),
            exports.GetExportedValue<ICommandService>(), exports.GetExportedValue<IBuildService>());
    }

    private void Update()
    {
        var selected = configurations.Selected;
        var type = selected is null ? null : configurations.FindType(selected.TypeId);
        selectorIcon.Data = Icons.Find(type?.Icon) ?? Icons.Play;
        selectorName.Text = selected?.Name ?? (configurations.IsDetecting ? "Finding configurations..." : "Add Configuration...");
        ToolTip.SetTip(selector, selected is null ? "Choose or add a run configuration" : $"{selected.Name} ({type?.Name ?? selected.TypeId})");

        var name = selected is null ? "" : $" {selected.Name}";
        Sync(run, CommandIds.Run, $"Run{name}");
        Sync(debug, CommandIds.Debug, runCommands.WhyCannotDebug(selected) is { } reason ? $"Debug{name}: {reason}" : $"Debug{name}");
        Sync(stop, CommandIds.Stop, "Stop");
        Sync(build, CommandIds.Build, selected is null ? "Build" : $"Build{name}");
    }

    private void Sync(Button button, string commandId, string tip)
    {
        button.IsEnabled = commands.CanExecute(commandId);
        if (button.Tag is string brush && button.Content is SymbolIcon symbol)
            symbol.Bind(SymbolIcon.ForegroundProperty, symbol.GetResourceObservable(button.IsEnabled ? brush : "TextDisabledBrush"));
        ToolTip.SetTip(button, commands.GetKeyBinding(commandId) is { } keys ? $"{tip} ({keys})" : tip);
    }

    private void FillMenu(MenuFlyout menu)
    {
        var all = configurations.Configurations;
        var recent = configurations.Recent.Select(configurations.Find).OfType<RunConfiguration>().Take(RecentCount).ToList();
        if (recent.Count > 0 && all.Count > RecentCount)
        {
            menu.Items.Add(Header("Recent"));
            foreach (var configuration in recent)
                menu.Items.Add(Item(configuration));
            menu.Items.Add(new Separator());
        }

        var rank = configurations.Recent.Select((name, index) => (name, index)).ToDictionary(p => p.name, p => p.index, StringComparer.Ordinal);
        var groups = all.GroupBy(c => c.TypeId).ToList();
        foreach (var group in groups)
        {
            if (groups.Count > 1)
                menu.Items.Add(Header(configurations.FindType(group.Key)?.Name ?? group.Key));
            foreach (var configuration in group.OrderBy(c => rank.GetValueOrDefault(c.Name, int.MaxValue)))
                menu.Items.Add(Item(configuration));
        }

        if (all.Count == 0)
            menu.Items.Add(new MenuItem { Header = configurations.IsDetecting ? "Finding configurations..." : "No configurations yet", IsEnabled = false });

        menu.Items.Add(new Separator());
        var edit = new CommandMenuItem
        {
            Header = "Edit Configurations...",
            Icon = new SymbolIcon { Data = Icons.Settings, Size = 14 },
            GestureText = commands.GetKeyBinding(CommandIds.EditConfigurations),
        };
        edit.Click += (_, _) => _ = commands.ExecuteAsync(CommandIds.EditConfigurations);
        menu.Items.Add(edit);
    }

    private MenuItem Item(RunConfiguration configuration)
    {
        var type = configurations.FindType(configuration.TypeId);
        var isSelected = configurations.Selected?.Name == configuration.Name;
        var item = new MenuItem
        {
            Header = configuration.Name,
            Icon = new SymbolIcon { Data = Icons.Find(type?.Icon) ?? Icons.Play, Size = 14 },
            FontWeight = isSelected ? FontWeight.SemiBold : FontWeight.Normal,
        };
        item.Click += (_, _) => configurations.Select(configuration);
        return item;
    }

    private static MenuItem Header(string text) => new() { Header = text, IsEnabled = false, Classes = { "header" } };

    private Button CommandButton(Geometry icon, string commandId, string? brush)
    {
        var button = new Button { Classes = { "toolbar", "icon" }, Content = new SymbolIcon { Data = icon, Size = 16 }, Tag = brush };
        ToolTip.SetShowOnDisabled(button, true);
        button.Click += (_, _) => _ = commands.ExecuteAsync(commandId);
        return button;
    }
}
