using System.Collections.Specialized;
using System.Composition;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using HammerUI;
using HammerUI.Controls;
using Runesmith.Editor;
using Runesmith.Sdk.ToolWindows;
using Runesmith.Shell.Services;

namespace Runesmith.Shell.ToolWindows;

/// <summary>The Output panel: the log of builds, language analyzers, language servers and plugins, one channel at a time.</summary>
[Export(typeof(IToolWindowProvider))]
[method: ImportingConstructor]
public sealed class OutputToolWindow(OutputService output) : IToolWindowProvider
{
    public ToolWindowDefinition Definition { get; } =
        new(OutputService.OutputToolWindowId, "Output", "terminal", DockSide.Bottom) { Order = 1, IsVisibleByDefault = true, KeyBinding = "Ctrl+Shift+U" };

    public Control CreateContent()
    {
        var channels = new ComboBox { ItemsSource = output.Channels, MinWidth = 200, PlaceholderText = "No output yet", Classes = { "small" } };
        var lines = new ListBox
        {
            Classes = { "rows" },
            FontFamily = new FontFamily(EditorOptions.BundledFontFamily),
            FontSize = 12.5,
            ItemTemplate = new FuncDataTemplate<string?>((line, _) => new SelectableTextBlock { Text = line, TextWrapping = TextWrapping.NoWrap }),
        };
        var clear = new Button { Classes = { "icon", "small" }, Content = new SymbolIcon { Data = Icons.Trash, Size = 14 } };
        ToolTip.SetTip(clear, "Clear the channel");
        clear.Click += (_, _) => (channels.SelectedItem as OutputChannel)?.Clear();

        var empty = new EmptyState { Icon = Icons.Terminal, Title = "No output", Hint = "Builds, language analyzers and plugins write their logs here." };

        void Select(OutputChannel? channel)
        {
            if (lines.ItemsSource is INotifyCollectionChanged old)
                old.CollectionChanged -= OnLinesChanged;
            lines.ItemsSource = channel?.Lines;
            if (channel is not null)
                channel.Lines.CollectionChanged += OnLinesChanged;
            empty.IsVisible = channel is null;
            ScrollToEnd();
        }

        void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e) => ScrollToEnd();

        void ScrollToEnd()
        {
            if (lines.ItemCount > 0)
                lines.ScrollIntoView(lines.ItemCount - 1);
        }

        channels.SelectionChanged += (_, _) => Select(channels.SelectedItem as OutputChannel);
        output.ShowRequested += (_, channel) => channels.SelectedItem = channel;
        output.Channels.CollectionChanged += (_, _) =>
        {
            if (channels.SelectedItem is null && output.Channels.Count > 0)
                channels.SelectedIndex = 0;
        };
        if (output.Channels.Count > 0)
            channels.SelectedIndex = 0;

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Avalonia.Thickness(8, 6), Children = { channels, clear } };
        var root = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);
        root.Children.Add(new Panel { Children = { lines, empty } });
        empty.IsVisible = output.Channels.Count == 0;
        return root;
    }
}
