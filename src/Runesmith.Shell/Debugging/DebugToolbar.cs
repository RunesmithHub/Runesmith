using System.Composition;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using HammerUI;
using HammerUI.Controls;
using Runesmith.Sdk.Commands;
using Runesmith.Sdk.Shell;

namespace Runesmith.Shell.Debugging;

/// <summary>The debug toolbar in the main toolbar, beside the run widget, while a session runs: continue or pause and the steps. The run
/// widget's Stop ends the session, and the Debug tool window's header has every control.</summary>
[Export(typeof(IToolbarWidgetProvider))]
[method: ImportingConstructor]
public sealed class DebugToolbar(DebugService debug, Lazy<ICommandService> commands) : IToolbarWidgetProvider
{
    public ToolbarSlot Slot => ToolbarSlot.Trailing;

    public int Order => 100;

    public Control CreateWidget()
    {
        DebugIcons.Register();
        var service = commands.Value;
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
        var buttons = new List<(string Id, Button Button)>();
        foreach (var (id, icon, brush) in new[]
        {
            (CommandIds.Continue, DebugIcons.Continue, "SuccessBrush"),
            (CommandIds.Pause, "pause", null),
            (CommandIds.StepOver, DebugIcons.StepOver, null),
            (CommandIds.StepInto, DebugIcons.StepInto, null),
            (CommandIds.StepOut, DebugIcons.StepOut, null),
        })
        {
            var symbol = new SymbolIcon { Data = Icons.Find(icon), Size = 16 };
            var button = new Button { Classes = { "toolbar", "icon" }, Content = symbol, Tag = brush };
            ToolTip.SetShowOnDisabled(button, true);
            button.Click += (_, _) => _ = service.ExecuteAsync(id);
            buttons.Add((id, button));
            panel.Children.Add(button);
        }

        panel.Children.Add(new Border { Width = 1, Height = 16, Margin = new Avalonia.Thickness(4, 0), Classes = { "divider" } });

        void Update()
        {
            var session = debug.Current;
            panel.IsVisible = session is not null;
            if (session is null)
                return;

            foreach (var (id, button) in buttons)
            {
                button.IsEnabled = service.CanExecute(id);
                button.IsVisible = id switch
                {
                    CommandIds.Continue => session.State != DebugState.Running,
                    CommandIds.Pause => session.State == DebugState.Running,
                    _ => true,
                };
                if (button.Content is SymbolIcon symbol)
                    symbol[!SymbolIcon.ForegroundProperty] = new DynamicResourceExtension(button.IsEnabled && button.Tag is string brush ? brush : button.IsEnabled ? "TextBrush" : "TextDisabledBrush");
                var title = service.Find(id)?.Title ?? id;
                ToolTip.SetTip(button, service.GetKeyBinding(id) is { } keys ? $"{title} ({keys})" : title);
            }
        }

        debug.Changed += (_, _) => UiThread.Run(Update);
        service.Changed += (_, _) => UiThread.Run(Update);
        Update();
        return panel;
    }
}
