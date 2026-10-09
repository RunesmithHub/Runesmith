using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using HammerUI;
using HammerUI.Controls;
using Runesmith.Sdk.Options;
using Runesmith.Sdk.Running;
using Runesmith.Shell.Forms;

namespace Runesmith.Shell.Running;

/// <summary>What the configurations dialog returns when the user presses OK.</summary>
/// <param name="Configurations">Every configuration, as the user left them.</param>
/// <param name="Selected">The name of the configuration selected in the dialog.</param>
internal sealed record RunConfigurationsResult(IReadOnlyList<RunConfiguration> Configurations, string? Selected);

/// <summary>Run > Edit Configurations: the configurations on the left, grouped by type, with Add, Remove and Copy, and the selected one's
/// name, storage, settings and before-launch steps on the right. It edits copies and returns them only when the user presses OK.</summary>
internal sealed class RunConfigurationsDialog : Dialog
{
    /// <summary>The id the name field has in the form, which cannot clash with a type's option ids.</summary>
    internal const string NameId = "$name";

    /// <summary>The id the Store as field has in the form.</summary>
    internal const string StoreId = "$store";

    private const double ListWidth = 300;

    private readonly IRunConfigurationService service;
    private readonly IReadOnlyDictionary<string, OptionSet> typeOptions;
    private readonly OptionFormServices services;
    private readonly List<Entry> entries = [];
    private readonly ListBox list = new() { Classes = { "configurations" }, SelectionMode = SelectionMode.Single };
    private readonly ContentControl detail = new();
    private readonly Button ok = new() { Content = "OK", Classes = { "accent" }, IsDefault = true, MinWidth = 84 };
    private readonly Button cancel = new() { Content = "Cancel", MinWidth = 84 };
    private readonly Button remove;
    private readonly Button copy;
    private readonly Dictionary<Entry, TextBlock> rowNames = [];
    private Entry? selected;
    private OptionForm? form;

    public RunConfigurationsDialog(IRunConfigurationService service, IReadOnlyDictionary<string, OptionSet> typeOptions, OptionFormServices services)
    {
        this.service = service;
        this.typeOptions = typeOptions;
        this.services = services;
        foreach (var configuration in service.Configurations)
            entries.Add(Entry.From(configuration, service.FindType(configuration.TypeId), OptionsOf(configuration.TypeId)));

        Header = "Run Configurations";
        Width = 1000;
        Height = 740;
        Padding = new Thickness(0);
        ShowCloseButton = true;
        Styles.Add(CreateStyles());

        list.ItemTemplate = new FuncDataTemplate<object>((row, _) => row switch
        {
            GroupRow group => GroupView(group),
            Entry entry => EntryView(entry),
            _ => new Panel(),
        });
        list.ContainerPrepared += (_, e) =>
        {
            var isGroup = e.Container.DataContext is GroupRow;
            e.Container.IsEnabled = !isGroup;
            e.Container.Focusable = !isGroup;
            e.Container.Height = 30;
            e.Container.Classes.Set("group", isGroup);
        };
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is Entry entry && !ReferenceEquals(entry, selected))
                Select(entry);
        };

        var add = ToolButton(Icons.Plus, "Add a configuration");
        add.Flyout = AddMenu();
        remove = ToolButton(Icons.Minus, "Remove the configuration");
        remove.Click += (_, _) => RemoveSelected();
        copy = ToolButton(Icons.Copy, "Copy the configuration");
        copy.Click += (_, _) => CopySelected();
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(10, 10, 10, 6), Children = { add, remove, copy } };

        var left = new DockPanel { Width = ListWidth };
        left.Bind(BackgroundProperty, left.GetResourceObservable("SurfaceBrush"));
        DockPanel.SetDock(tools, Dock.Top);
        left.Children.AddRange([tools, list]);

        var divider = new Border { Width = 1 };
        divider.Bind(Border.BackgroundProperty, divider.GetResourceObservable("BorderSubtleBrush"));
        var right = new ScrollViewer { Content = detail, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, Padding = new Thickness(24, 16, 28, 20) };
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions($"{ListWidth.ToString(CultureInfo.InvariantCulture)},Auto,*"), Children = { left, divider, right } };
        Grid.SetColumn(divider, 1);
        Grid.SetColumn(right, 2);
        var border = new Border { Margin = new Thickness(0, 12, 0, 0), BorderThickness = new Thickness(0, 1, 0, 0), Child = body };
        border.Bind(Border.BorderBrushProperty, border.GetResourceObservable("BorderSubtleBrush"));
        Content = border;

        Footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { cancel, ok } };
        ok.Click += (_, _) => _ = AcceptAsync();
        cancel.Click += (_, _) => Close();
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        Refill(service.Selected is { } current ? entries.Find(e => e.Name == current.Name) : null);
    }

    protected override Type StyleKeyOverride => typeof(Dialog);

    /// <summary>Gets the entries the dialog edits, for tests.</summary>
    internal IReadOnlyList<Entry> Entries => entries;

    /// <summary>Gets the configurations as the dialog holds them now, with the values the forms filled in that the user did not choose
    /// left out; detected configurations the user changed become saved ones.</summary>
    /// <param name="defaultSdks">The default SDK's path per SDK kind, whose values the forms fill in on their own.</param>
    internal IReadOnlyList<RunConfiguration> Collect(IReadOnlyDictionary<string, string> defaultSdks) =>
        [.. entries.Select(entry => entry.ToConfiguration(defaultSdks))];

    private void Refill(Entry? keep)
    {
        var rows = new List<object>();
        rowNames.Clear();
        foreach (var group in entries.GroupBy(e => e.TypeId))
        {
            var first = group.First();
            rows.Add(new GroupRow(first.Type?.Name ?? group.Key, first.Type?.Icon));
            rows.AddRange(group);
        }

        list.ItemsSource = rows;
        var target = keep is not null && entries.Contains(keep) ? keep : entries.FirstOrDefault();
        list.SelectedItem = target;
        if (target is null)
            Select(null);
        else if (!ReferenceEquals(target, selected))
            Select(target);
        remove.IsEnabled = copy.IsEnabled = target is not null;
    }

    private void Select(Entry? entry)
    {
        selected = entry;
        form = null;
        remove.IsEnabled = copy.IsEnabled = entry is not null;
        if (entry is null)
        {
            detail.Content = new EmptyState
            {
                Icon = Icons.Play,
                Title = "No run configurations",
                Hint = "Add one with the + button. Configurations for runnable projects appear here by themselves.",
            };
            return;
        }

        var options = new OptionSet([.. HeaderOptions(), .. entry.Options.Options]);
        form = new OptionForm(options, entry.Values, services, values => ValidateName(entry, values));
        var current = form;
        form.ValueChanged += (_, id) =>
        {
            if (id == NameId && rowNames.TryGetValue(entry, out var name))
                name.Text = entry.Name;
        };
        form.ValidityChanged += (_, _) => ok.IsEnabled = current.IsValid;
        ok.IsEnabled = current.IsValid;

        var content = new StackPanel { Spacing = 18, Children = { EntryHeader(entry), current, BeforeLaunchSection(entry) } };
        if (entry.Type is null)
            content.Children.Insert(1, new MessageBox($"No plugin provides the configuration type {entry.TypeId}; turn on the plugin that adds it to change the settings."));
        detail.Content = content;
    }

    private static Option[] HeaderOptions() =>
    [
        new(NameId, "Name", OptionKind.Text) { IsRequired = true, Placeholder = "My configuration" },
        new(StoreId, "Store as", OptionKind.Choice)
        {
            Default = "shared",
            Choices =
            [
                new("shared", "Shared") { Description = "Saved in .runesmith/run.json, so it can be committed with the folder." },
                new("local", "Local") { Description = "Saved only for you, outside the folder." },
            ],
            Description = "Shared configurations are saved in .runesmith/run.json; local ones only for you.",
        },
    ];

    private Dictionary<string, string> ValidateName(Entry entry, OptionValues values)
    {
        var name = values.Get(NameId)?.Trim() ?? "";
        var problems = new Dictionary<string, string>(StringComparer.Ordinal);
        if (name.Length > 0 && entries.Any(e => !ReferenceEquals(e, entry) && e.Name == name))
            problems[NameId] = $"Another configuration is named {name}.";
        return problems;
    }

    private static Grid EntryHeader(Entry entry)
    {
        var title = new TextBlock { Text = entry.Type?.Name ?? entry.TypeId, FontSize = 18, FontWeight = FontWeight.SemiBold };
        var text = new StackPanel { Spacing = 4, Children = { title } };
        if (entry.Type?.Description is { Length: > 0 } description)
        {
            var summary = new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap };
            summary.Bind(TextBlock.ForegroundProperty, summary.GetResourceObservable("TextSecondaryBrush"));
            text.Children.Add(summary);
        }

        if (entry.IsDetected)
        {
            var note = new TextBlock { Text = "Found in the folder. Changing it saves it as your own.", FontSize = 12 };
            note.Bind(TextBlock.ForegroundProperty, note.GetResourceObservable("TextMutedBrush"));
            text.Children.Add(note);
        }

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,14,*"), Children = { IconTile(entry.Type?.Icon, 40, 20), text } };
        Grid.SetColumn(text, 2);
        return grid;
    }

    private SectionPanel BeforeLaunchSection(Entry entry)
    {
        var rows = new StackPanel { Spacing = 4 };
        var add = new Button { Classes = { "subtle", "small" }, Content = Label(Icons.Plus, "Add step") };
        var section = new SectionPanel
        {
            Header = "Before launch",
            Content = new StackPanel { Spacing = 6, Children = { rows, add } },
            Margin = new Thickness(0, 4, 0, 0),
        };

        void Fill()
        {
            rows.Children.Clear();
            if (entry.Steps.Count == 0)
            {
                var none = new TextBlock { Text = "Nothing runs first; the program starts as it is.", FontSize = 12, Margin = new Thickness(2, 4) };
                none.Bind(TextBlock.ForegroundProperty, none.GetResourceObservable("TextMutedBrush"));
                rows.Children.Add(none);
            }

            for (var i = 0; i < entry.Steps.Count; i++)
                rows.Children.Add(StepRow(entry, i, Fill));
        }

        var menu = new MenuFlyout();
        menu.Items.Add(MenuItem("Build", Icons.Hammer, "Build the configuration's project", () => AddStep(new BeforeLaunchStep(BeforeLaunchKind.Build))));
        var others = new MenuItem { Header = "Run Configuration", Icon = new SymbolIcon { Data = Icons.Play, Size = 14 } };
        menu.Opening += (_, _) =>
        {
            others.Items.Clear();
            foreach (var other in entries.Where(e => !ReferenceEquals(e, entry)))
                others.Items.Add(MenuItem(other.Name, Icons.Find(other.Type?.Icon), null, () => AddStep(new BeforeLaunchStep(BeforeLaunchKind.RunConfiguration, other.Name))));
            others.IsEnabled = others.Items.Count > 0;
        };
        menu.Items.Add(others);
        menu.Items.Add(MenuItem("Command", Icons.Terminal, "Run a command line in the folder", () => AddStep(new BeforeLaunchStep(BeforeLaunchKind.Command, ""))));
        add.Flyout = menu;

        void AddStep(BeforeLaunchStep step)
        {
            entry.Steps.Add(step);
            Fill();
            if (step.Kind == BeforeLaunchKind.Command && rows.Children.LastOrDefault() is Grid row && row.Children.OfType<TextBox>().FirstOrDefault() is { } box)
                box.Focus();
        }

        Fill();
        return section;
    }

    private Grid StepRow(Entry entry, int index, Action refill)
    {
        var step = entry.Steps[index];
        var (icon, text) = step.Kind switch
        {
            BeforeLaunchKind.Build => (Icons.Hammer, "Build"),
            BeforeLaunchKind.RunConfiguration => (Icons.Play, "Run"),
            _ => (Icons.Terminal, "Command"),
        };
        Control field = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
        if (step.Kind == BeforeLaunchKind.Command)
        {
            var box = new TextBox { Text = step.Argument, PlaceholderText = "A command line, such as npm install", Classes = { "small" } };
            box.PropertyChanged += (_, e) =>
            {
                if (e.Property == TextBox.TextProperty)
                    entry.Steps[index] = step with { Argument = box.Text };
            };
            field = Labeled(text, box);
        }
        else if (step.Kind == BeforeLaunchKind.RunConfiguration)
        {
            var names = entries.Where(e => !ReferenceEquals(e, entry)).Select(e => e.Name).ToList();
            if (step.Argument is { } argument && !names.Contains(argument))
                names.Add(argument);
            var box = new ComboBox { ItemsSource = names, SelectedItem = step.Argument, HorizontalAlignment = HorizontalAlignment.Stretch, Classes = { "small" } };
            box.SelectionChanged += (_, _) =>
            {
                if (box.SelectedItem is string name)
                    entry.Steps[index] = step with { Argument = name };
            };
            field = Labeled(text, box);
        }

        var up = ToolButton(Icons.ArrowUp, "Move up");
        up.IsEnabled = index > 0;
        up.Click += (_, _) =>
        {
            (entry.Steps[index - 1], entry.Steps[index]) = (entry.Steps[index], entry.Steps[index - 1]);
            refill();
        };
        var down = ToolButton(Icons.ArrowDown, "Move down");
        down.IsEnabled = index < entry.Steps.Count - 1;
        down.Click += (_, _) =>
        {
            (entry.Steps[index + 1], entry.Steps[index]) = (entry.Steps[index], entry.Steps[index + 1]);
            refill();
        };
        var delete = ToolButton(Icons.X, "Remove the step");
        delete.Click += (_, _) =>
        {
            entry.Steps.RemoveAt(index);
            refill();
        };

        var symbol = new SymbolIcon { Data = icon, Size = 14, VerticalAlignment = VerticalAlignment.Center };
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,10,*,8,Auto,Auto,Auto"),
            MinHeight = 32,
            Classes = { "step" },
            Children = { symbol, field, up, down, delete },
        };
        Grid.SetColumn(field, 2);
        Grid.SetColumn(up, 4);
        Grid.SetColumn(down, 5);
        Grid.SetColumn(delete, 6);
        return row;
    }

    private static Grid Labeled(string label, Control field)
    {
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, MinWidth = 70 };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,8,*"), Children = { text, field } };
        Grid.SetColumn(field, 2);
        return grid;
    }

    private MenuFlyout AddMenu()
    {
        var menu = new MenuFlyout();
        foreach (var type in service.Types)
        {
            var header = new StackPanel
            {
                Spacing = 1,
                Children =
                {
                    new TextBlock { Text = type.Name, FontWeight = FontWeight.Medium },
                    Muted(type.Description, 12),
                },
            };
            var item = new MenuItem { Header = header, Icon = new SymbolIcon { Data = Icons.Find(type.Icon) ?? Icons.Play, Size = 14 } };
            item.Click += (_, _) => AddConfiguration(type);
            menu.Items.Add(item);
        }

        if (menu.Items.Count == 0)
            menu.Items.Add(new MenuItem { Header = "No plugin adds configuration types", IsEnabled = false });
        return menu;
    }

    private void AddConfiguration(IRunConfigurationType type)
    {
        var options = OptionsOf(type.Id);
        var entry = Entry.From(new RunConfiguration(type.Id, UniqueName(type.Name), options.Defaults()), type, options);
        entries.Add(entry);
        Refill(entry);
        form?.FocusField(NameId);
    }

    private OptionSet OptionsOf(string typeId) => typeOptions.GetValueOrDefault(typeId) ?? OptionSet.Empty;

    private void CopySelected()
    {
        if (selected is not { } entry)
            return;

        var copied = entry.Copy(UniqueName(entry.Name + " (copy)"));
        entries.Insert(entries.IndexOf(entry) + 1, copied);
        Refill(copied);
    }

    private void RemoveSelected()
    {
        if (selected is not { } entry)
            return;

        var index = entries.IndexOf(entry);
        entries.Remove(entry);
        Refill(entries.Count == 0 ? null : entries[Math.Min(index, entries.Count - 1)]);
    }

    private string UniqueName(string name)
    {
        var candidate = name;
        for (var i = 2; entries.Any(e => e.Name == candidate); i++)
            candidate = string.Create(CultureInfo.InvariantCulture, $"{name} ({i})");
        return candidate;
    }

    private async Task AcceptAsync()
    {
        foreach (var entry in entries)
        {
            var options = new OptionSet([.. HeaderOptions(), .. entry.Options.Options]);
            var problems = new Dictionary<string, string>(options.Validate(entry.Values));
            foreach (var (id, problem) in ValidateName(entry, entry.Values))
                problems.TryAdd(id, problem);
            if (entry.Type is not null && problems.Count > 0)
            {
                list.SelectedItem = entry;
                form?.RevealProblems();
                return;
            }
        }

        var defaults = new Dictionary<string, string>(StringComparer.Ordinal);
        if (services.Sdks is { } sdks)
        {
            var kinds = entries.SelectMany(e => e.Options.Options).Where(o => o.Kind == OptionKind.Sdk && o.SdkKind is not null).Select(o => o.SdkKind!).Distinct();
            foreach (var kind in kinds)
            {
                try
                {
                    if (await sdks.GetDefaultAsync(kind) is { } sdk)
                        defaults[kind] = sdk.Path;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                }
            }
        }

        Close(new RunConfigurationsResult(Collect(defaults), selected?.Name));
    }

    private static TextBlock Muted(string? text, double size)
    {
        var block = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, MaxWidth = 360 };
        block.Bind(TextBlock.ForegroundProperty, block.GetResourceObservable("TextMutedBrush"));
        return block;
    }

    private static StackPanel Label(Geometry icon, string text) => new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 6,
        Children = { new SymbolIcon { Data = icon, Size = 14, VerticalAlignment = VerticalAlignment.Center }, new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center } },
    };

    private static MenuItem MenuItem(string header, Geometry? icon, string? tip, Action action)
    {
        var item = new MenuItem { Header = header, Icon = icon is null ? null : new SymbolIcon { Data = icon, Size = 14 } };
        if (tip is not null)
            ToolTip.SetTip(item, tip);
        item.Click += (_, _) => action();
        return item;
    }

    private static Button ToolButton(Geometry icon, string tip)
    {
        var button = new Button { Classes = { "icon", "small" }, Content = new SymbolIcon { Data = icon, Size = 14 } };
        ToolTip.SetTip(button, tip);
        return button;
    }

    private static StackPanel GroupView(GroupRow group)
    {
        var title = new TextBlock { Text = group.Title.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeight.SemiBold, LetterSpacing = 0.6, VerticalAlignment = VerticalAlignment.Center };
        title.Bind(TextBlock.ForegroundProperty, title.GetResourceObservable("TextMutedBrush"));
        var icon = new SymbolIcon { Data = Icons.Find(group.Icon) ?? Icons.Play, Size = 12, VerticalAlignment = VerticalAlignment.Center };
        icon.Bind(SymbolIcon.ForegroundProperty, icon.GetResourceObservable("TextMutedBrush"));
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(2, 4, 0, 0), VerticalAlignment = VerticalAlignment.Center, Children = { icon, title } };
    }

    private Grid EntryView(Entry entry)
    {
        var name = new TextBlock { Text = entry.Name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        rowNames[entry] = name;
        var tag = new TextBlock { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Text = entry.IsDetected ? "found" : entry.IsLocal ? "local" : "" };
        tag.Bind(TextBlock.ForegroundProperty, tag.GetResourceObservable("TextDisabledBrush"));
        var icon = new SymbolIcon { Data = Icons.Find(entry.Type?.Icon) ?? Icons.Play, Size = 14, VerticalAlignment = VerticalAlignment.Center, Classes = { "type-icon" } };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,8,*,6,Auto"), Margin = new Thickness(12, 0, 0, 0), Children = { icon, name, tag } };
        Grid.SetColumn(name, 2);
        Grid.SetColumn(tag, 4);
        return grid;
    }

    private static Border IconTile(string? icon, double size, double iconSize) => new()
    {
        Width = size,
        Height = size,
        CornerRadius = new CornerRadius(size / 4),
        VerticalAlignment = VerticalAlignment.Top,
        Classes = { "type-tile" },
        Child = new SymbolIcon { Data = Icons.Find(icon) ?? Icons.Play, Size = iconSize, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
    };

    private static Styles CreateStyles() =>
    [
        new Style(x => x.OfType<ListBox>().Class("configurations"))
        {
            Setters = { new Setter(BackgroundProperty, Brushes.Transparent), new Setter(PaddingProperty, new Thickness(8, 0, 8, 8)) },
        },
        new Style(x => x.OfType<ListBox>().Class("configurations").Descendant().OfType<ListBoxItem>())
        {
            Setters = { new Setter(PaddingProperty, new Thickness(8, 0)), new Setter(MinHeightProperty, 0d), new Setter(CornerRadiusProperty, new CornerRadius(6)) },
        },
        new Style(x => x.OfType<ListBox>().Class("configurations").Descendant().OfType<ListBoxItem>().Class("group"))
        {
            Setters = { new Setter(OpacityProperty, 1d), new Setter(CursorProperty, Cursor.Default) },
        },
        new Style(x => x.OfType<Border>().Class("type-tile"))
        {
            Setters = { new Setter(Border.BackgroundProperty, new DynamicResourceExtension("AccentSubtleBrush")) },
        },
        new Style(x => x.OfType<Border>().Class("type-tile").Descendant().OfType<SymbolIcon>())
        {
            Setters = { new Setter(ForegroundProperty, new DynamicResourceExtension("AccentBrush")) },
        },
        new Style(x => x.OfType<SymbolIcon>().Class("type-icon"))
        {
            Setters = { new Setter(ForegroundProperty, new DynamicResourceExtension("AccentBrush")) },
        },
    ];

    private sealed record GroupRow(string Title, string? Icon);

    private sealed class MessageBox : Border
    {
        public MessageBox(string text)
        {
            Padding = new Thickness(12, 8);
            CornerRadius = new CornerRadius(6);
            this.Bind(BackgroundProperty, this.GetResourceObservable("SurfaceSunkenBrush"));
            Child = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        }
    }

    /// <summary>A configuration as the dialog edits it: its values with the name and storage in them, and its steps.</summary>
    internal sealed class Entry
    {
        private Entry(string typeId, IRunConfigurationType? type, OptionSet options, OptionValues values, List<BeforeLaunchStep> steps, bool isDetected, RunConfiguration? original)
        {
            TypeId = typeId;
            Type = type;
            Options = options;
            Values = values;
            Steps = steps;
            IsDetected = isDetected;
            Original = original;
        }

        public string TypeId { get; }

        public IRunConfigurationType? Type { get; }

        public OptionSet Options { get; }

        public OptionValues Values { get; }

        public List<BeforeLaunchStep> Steps { get; }

        public bool IsDetected { get; }

        public RunConfiguration? Original { get; }

        public string Name => Values.Get(NameId)?.Trim() ?? "";

        public bool IsLocal => Values.Get(StoreId) == "local";

        public static Entry From(RunConfiguration configuration, IRunConfigurationType? type, OptionSet options)
        {
            var values = configuration.Values.Copy();
            values.Set(NameId, configuration.Name);
            values.Set(StoreId, configuration.IsLocal ? "local" : "shared");
            return new Entry(configuration.TypeId, type, options, values, [.. configuration.BeforeLaunch], configuration.IsDetected, configuration);
        }

        public Entry Copy(string name)
        {
            var values = Values.Copy();
            values.Set(NameId, name);
            return new Entry(TypeId, Type, Options, values, [.. Steps], isDetected: false, original: null);
        }

        public RunConfiguration ToConfiguration(IReadOnlyDictionary<string, string> defaultSdks)
        {
            var values = new OptionValues();
            foreach (var (id, value) in Values.All)
            {
                if (!id.StartsWith('$') && !IsFilledSdk(id, value, defaultSdks))
                    values.Set(id, value);
            }

            var changed = Original is null
                || Original.Name != Name
                || Original.IsLocal != IsLocal
                || !Original.BeforeLaunch.SequenceEqual(Steps)
                || !SameValues(Original.Values, values);
            return new RunConfiguration(TypeId, Name, values) { BeforeLaunch = [.. Steps], IsLocal = IsLocal, IsDetected = IsDetected && !changed };
        }

        // An SDK field fills in the default SDK by itself; keeping that path would tie a shared configuration to this machine.
        private bool IsFilledSdk(string id, string value, IReadOnlyDictionary<string, string> defaultSdks) =>
            Original?.Values.Get(id) is null
            && Options.Options.FirstOrDefault(o => o.Id == id) is { Kind: OptionKind.Sdk, SdkKind: { } kind }
            && defaultSdks.TryGetValue(kind, out var fallback)
            && string.Equals(fallback, value, StringComparison.Ordinal);

        private static bool SameValues(OptionValues a, OptionValues b) =>
            a.All.Count == b.All.Count && a.All.All(pair => b.Get(pair.Key) == pair.Value);
    }
}
