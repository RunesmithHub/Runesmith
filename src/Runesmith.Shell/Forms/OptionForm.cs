using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using HammerUI;
using HammerUI.Controls;
using Runesmith.Sdk.Commands;
using Runesmith.Sdk.Options;
using Runesmith.Sdk.Sdks;

namespace Runesmith.Shell.Forms;

/// <summary>What a form's fields can use beyond the values: the SDKs for SDK options and the commands for their Download entry.</summary>
/// <param name="Sdks">The SDK service, or null when no plugin provides one; SDK options then show as folder fields.</param>
/// <param name="Commands">The commands, for running the SDKs page from an SDK option.</param>
public sealed record OptionFormServices(ISdkService? Sdks, ICommandService? Commands)
{
    /// <summary>Gets services that offer nothing, for forms without SDK options.</summary>
    public static OptionFormServices None { get; } = new(null, null);
}

/// <summary>Draws an <see cref="OptionSet"/> as a two-column form bound to its <see cref="OptionValues"/>: labels right-aligned on the
/// left, fields on the right with hints and problems under them, and the advanced options in a collapsed section.</summary>
public sealed class OptionForm : StackPanel
{
    /// <summary>The height of a row's field.</summary>
    public const double RowHeight = 32;

    private const string LabelColumn = "OptionFormLabel";
    private const int MaximumSegments = 4;
    private const int MaximumSegmentLabelLength = 14;

    private readonly OptionFormServices services;
    private readonly List<FieldRow> rows = [];
    private readonly SectionPanel advanced;
    private readonly ContentControl beforeAdvanced = new();
    private bool syncing;

    /// <summary>Creates a form over some values, which it changes in place as the user edits.</summary>
    /// <param name="validate">Checks the option set cannot express; returns problems by option id.</param>
    public OptionForm(OptionSet options, OptionValues values, OptionFormServices? services = null,
        Func<OptionValues, IReadOnlyDictionary<string, string>>? validate = null)
    {
        Model = new OptionFormModel(options, values, validate);
        this.services = services ?? OptionFormServices.None;
        Spacing = 0;
        Grid.SetIsSharedSizeScope(this, true);

        var main = new StackPanel { Spacing = 4 };
        var advancedRows = new StackPanel { Spacing = 4, Margin = new Thickness(0, 6, 0, 0) };
        foreach (var option in options.Options)
        {
            var row = CreateRow(option);
            rows.Add(row);
            (option.Group == OptionGroup.Advanced ? advancedRows : main).Children.Add(row.Root);
        }

        advanced = new SectionPanel
        {
            Header = "Advanced",
            IsCollapsible = true,
            IsExpanded = false,
            Content = advancedRows,
            Margin = new Thickness(0, 12, 0, 0),
            IsVisible = advancedRows.Children.Count > 0,
        };
        Children.Add(main);
        Children.Add(beforeAdvanced);
        Children.Add(advanced);

        Model.ValueChanged += (_, id) => Refresh(changedId: id);
        Model.ValidityChanged += (_, _) => ValidityChanged?.Invoke(this, EventArgs.Empty);
        Refresh(changedId: null);
    }

    /// <summary>Gets the form's state: its values, conditions and problems.</summary>
    public OptionFormModel Model { get; }

    /// <summary>Gets whether the values can be used; a dialog keeps its main button disabled while this is false.</summary>
    public bool IsValid => Model.IsValid;

    /// <summary>Gets or sets content shown after the main options and before the Advanced section, such as a description.</summary>
    public object? BeforeAdvanced
    {
        get => beforeAdvanced.Content;
        set => beforeAdvanced.Content = value;
    }

    /// <summary>Gets or sets whether the Advanced section is open.</summary>
    public bool IsAdvancedExpanded
    {
        get => advanced.IsExpanded;
        set => advanced.IsExpanded = value;
    }

    /// <summary>Raised when <see cref="IsValid"/> changes.</summary>
    public event EventHandler? ValidityChanged;

    /// <summary>Raised after a value changes, by the user or through <see cref="SetValue(string, string?)"/>, with the option's id.</summary>
    public event EventHandler<string>? ValueChanged;

    /// <summary>Sets a value from code, updating its field without counting as the user's change.</summary>
    public void SetValue(string id, string? value) => Model.Set(id, value, byUser: false);

    /// <summary>Replaces an option's hint, such as a path that follows from other values; null restores the option's own description.</summary>
    public void SetHint(string id, string? hint)
    {
        if (rows.Find(r => r.Option.Id == id) is not { } row)
            return;

        row.HintOverride = hint;
        UpdateHint(row);
    }

    /// <summary>Shows every problem, such as when the user tries to submit, and checks the values again.</summary>
    public void RevealProblems()
    {
        Model.RevealProblems();
        Refresh(changedId: null);
    }

    /// <summary>Checks the values again, such as after something <c>validate</c> reads changed outside the form.</summary>
    public void Revalidate()
    {
        Model.Revalidate();
        Refresh(changedId: null);
    }

    /// <summary>Moves the keyboard focus to an option's field.</summary>
    public void FocusField(string id)
    {
        if (rows.Find(r => r.Option.Id == id)?.Focus is { } focus)
            focus.Focus();
    }

    private void Refresh(string? changedId)
    {
        foreach (var row in rows)
        {
            row.Root.IsVisible = Model.IsVisible(row.Option);
            row.Field.IsEnabled = Model.IsEnabled(row.Option);
            if (changedId == row.Option.Id)
                Sync(row);
            var problem = Model.GetShownProblem(row.Option.Id);
            row.Problem.Text = problem;
            row.Problem.IsVisible = problem is not null;
            UpdateHint(row);
        }

        if (changedId is not null)
            ValueChanged?.Invoke(this, changedId);
    }

    private static void UpdateHint(FieldRow row)
    {
        var hint = row.HintOverride ?? row.Option.Description;
        row.Hint.Text = hint;
        row.Hint.IsVisible = !string.IsNullOrEmpty(hint) && !row.Problem.IsVisible;
    }

    private void Sync(FieldRow row)
    {
        if (syncing)
            return;

        syncing = true;
        try
        {
            row.Sync(Model.Values.Get(row.Option.Id));
        }
        finally
        {
            syncing = false;
        }
    }

    private void SetFromField(Option option, string? value)
    {
        if (!syncing)
            Model.Set(option.Id, value);
    }

    private FieldRow CreateRow(Option option)
    {
        var (field, focus, sync) = CreateField(option);
        var hint = new TextBlock { Classes = { "caption", "muted" }, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) };
        var problem = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2), IsVisible = false };
        problem.Bind(TextBlock.ForegroundProperty, problem.GetResourceObservable("DangerBrush"));

        var grid = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Auto) { SharedSizeGroup = LabelColumn }, new ColumnDefinition(12, GridUnitType.Pixel), new ColumnDefinition(GridLength.Star) } };
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        if (option.Kind != OptionKind.Toggle)
        {
            var label = new TextBlock
            {
                Text = option.Label,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Right,
                MinWidth = 110,
                MaxWidth = 220,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            label.Bind(TextBlock.ForegroundProperty, label.GetResourceObservable("TextSecondaryBrush"));
            if (option.Label.Length > 24)
                ToolTip.SetTip(label, option.Label);
            var labelCell = new Panel { Height = RowHeight, Children = { label } };
            grid.Children.Add(labelCell);
        }

        var fieldCell = new Panel { MinHeight = RowHeight, Children = { field } };
        field.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(fieldCell, 2);
        Grid.SetColumn(hint, 2);
        Grid.SetRow(hint, 1);
        Grid.SetColumn(problem, 2);
        Grid.SetRow(problem, 2);
        grid.Children.AddRange([fieldCell, hint, problem]);
        var row = new FieldRow(option, grid, field, focus, hint, problem, sync);
        Sync(row);
        return row;
    }

    private (Control Field, Control? Focus, Action<string?> Sync) CreateField(Option option) => option.Kind switch
    {
        OptionKind.Toggle => CreateToggle(option),
        OptionKind.Choice when IsSegmented(option) => CreateSegmented(option),
        OptionKind.Choice => CreateDropDown(option),
        OptionKind.Path => CreatePath(option, option.PathKind),
        OptionKind.List => CreateList(option),
        OptionKind.Sdk when services.Sdks is not null => CreateSdk(option, services.Sdks),
        OptionKind.Sdk => CreatePath(option, PathKind.Folder),
        _ => CreateText(option),
    };

    /// <summary>Whether a choice option shows as a segmented control rather than a drop-down.</summary>
    internal static bool IsSegmented(Option option) =>
        option.Choices.Count is >= 2 and <= MaximumSegments && option.Choices.All(c => c.Label.Length <= MaximumSegmentLabelLength);

    private (Control, Control?, Action<string?>) CreateText(Option option)
    {
        var box = new TextBox { PlaceholderText = option.Placeholder, HorizontalAlignment = HorizontalAlignment.Stretch };
        if (option.Kind == OptionKind.Number)
        {
            box.HorizontalAlignment = HorizontalAlignment.Left;
            box.Width = 140;
        }

        OnTextChanged(box, () => SetFromField(option, box.Text));
        return (box, box, value => box.Text = value ?? "");
    }

    private (Control, Control?, Action<string?>) CreateToggle(Option option)
    {
        var check = new CheckBox { Content = option.Label, MinHeight = 24 };
        check.IsCheckedChanged += (_, _) => SetFromField(option, check.IsChecked == true ? "true" : "false");
        return (check, check, value => check.IsChecked = bool.TryParse(value, out var on) && on);
    }

    private (Control, Control?, Action<string?>) CreateSegmented(Option option)
    {
        var segments = new SegmentedControl();
        foreach (var choice in option.Choices)
        {
            var item = new SegmentedControlItem { Content = choice.Label, Tag = choice.Value, MinWidth = 64 };
            if (choice.Description is { } description)
                ToolTip.SetTip(item, description);
            segments.Items.Add(item);
        }

        segments.SelectionChanged += (_, _) =>
        {
            if (segments.SelectedItem is SegmentedControlItem { Tag: string value })
                SetFromField(option, value);
        };
        return (segments, segments, value => segments.SelectedItem = segments.Items.OfType<SegmentedControlItem>().FirstOrDefault(i => (string?)i.Tag == value));
    }

    private (Control, Control?, Action<string?>) CreateDropDown(Option option)
    {
        var box = new ComboBox
        {
            ItemsSource = option.Choices,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            PlaceholderText = option.Placeholder ?? "Choose...",
            MaxDropDownHeight = 320,
            ItemTemplate = new FuncDataTemplate<OptionChoice>((choice, _) =>
            {
                var text = new TextBlock { Text = choice?.Label, TextTrimming = TextTrimming.CharacterEllipsis };
                if (choice?.Description is { } description)
                    ToolTip.SetTip(text, description);
                return text;
            }),
        };
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is OptionChoice choice)
                SetFromField(option, choice.Value);
        };
        return (box, box, value => box.SelectedItem = option.Choices.FirstOrDefault(c => c.Value == value));
    }

    private (Control, Control?, Action<string?>) CreatePath(Option option, PathKind kind)
    {
        var box = new TextBox { PlaceholderText = option.Placeholder, HorizontalAlignment = HorizontalAlignment.Stretch };
        var browse = new Button { Classes = { "icon" }, Content = new SymbolIcon { Data = Icons.FolderOpen, Size = 16 }, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(browse, kind == PathKind.Folder ? "Choose a folder" : "Choose a file");
        browse.Click += async (_, _) =>
        {
            if (await BrowseAsync(browse, option, kind, box.Text) is { } chosen)
                box.Text = chosen;
        };
        OnTextChanged(box, () => SetFromField(option, box.Text));
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,4,Auto"), Children = { box, browse } };
        Grid.SetColumn(browse, 2);
        return (grid, box, value => box.Text = value ?? "");
    }

    private static async Task<string?> BrowseAsync(Visual anchor, Option option, PathKind kind, string? current)
    {
        if (TopLevel.GetTopLevel(anchor)?.StorageProvider is not { } storage)
            return null;

        var startPath = string.IsNullOrWhiteSpace(current) ? null : Directory.Exists(current) ? current : Path.GetDirectoryName(current);
        var start = startPath is not null && Directory.Exists(startPath) ? await storage.TryGetFolderFromPathAsync(startPath) : null;
        if (kind == PathKind.Folder)
        {
            var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = option.Label, SuggestedStartLocation = start });
            return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = option.Label, SuggestedStartLocation = start });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    private (Control, Control?, Action<string?>) CreateList(Option option)
    {
        var items = new StackPanel { Spacing = 4 };
        var add = new Button { Classes = { "subtle", "small" }, Content = option.IsKeyValueList ? "Add variable" : "Add", HorizontalAlignment = HorizontalAlignment.Left };
        var panel = new StackPanel { Spacing = 4, Children = { items, add } };
        var filling = false;

        void Write()
        {
            if (filling)
                return;

            var lines = items.Children.OfType<Grid>().Select(row =>
            {
                var boxes = row.Children.OfType<TextBox>().ToList();
                return option.IsKeyValueList ? $"{boxes[0].Text}={boxes[1].Text}" : boxes[0].Text ?? "";
            }).Where(line => line.Length > 0 && line != "=");
            SetFromField(option, string.Join('\n', lines));
        }

        void AddRow(string key, string value)
        {
            var row = new Grid { ColumnDefinitions = option.IsKeyValueList ? new ColumnDefinitions("2*,4,3*,4,Auto") : new ColumnDefinitions("*,4,Auto") };
            var first = new TextBox { Text = key, PlaceholderText = option.IsKeyValueList ? "Name" : option.Placeholder ?? "Value" };
            OnTextChanged(first, Write);
            row.Children.Add(first);
            if (option.IsKeyValueList)
            {
                var second = new TextBox { Text = value, PlaceholderText = "Value" };
                OnTextChanged(second, Write);
                Grid.SetColumn(second, 2);
                row.Children.Add(second);
            }

            var remove = new Button { Classes = { "icon" }, Content = new SymbolIcon { Data = Icons.X, Size = 14 }, VerticalAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(remove, "Remove");
            remove.Click += (_, _) =>
            {
                items.Children.Remove(row);
                Write();
            };
            Grid.SetColumn(remove, option.IsKeyValueList ? 4 : 2);
            row.Children.Add(remove);
            items.Children.Add(row);
        }

        add.Click += (_, _) =>
        {
            AddRow("", "");
            items.Children.OfType<Grid>().Last().Children.OfType<TextBox>().First().Focus();
        };

        void Fill(string? text)
        {
            filling = true;
            items.Children.Clear();
            var values = new OptionValues();
            values.Set(option.Id, text);
            if (option.IsKeyValueList)
            {
                foreach (var (key, value) in values.GetPairs(option.Id))
                    AddRow(key, value);
            }
            else
            {
                foreach (var item in values.GetList(option.Id))
                    AddRow(item, "");
            }

            filling = false;
        }

        return (panel, add, Fill);
    }

    private (Control, Control?, Action<string?>) CreateSdk(Option option, ISdkService sdks)
    {
        var kind = option.SdkKind ?? "";
        var canDownload = services.Commands?.Find(CommandIds.Sdks) is not null;
        var box = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            PlaceholderText = "Looking for SDKs...",
            MaxDropDownHeight = 320,
            ItemTemplate = new FuncDataTemplate<SdkChoice>((choice, _) => SdkChoiceView(choice)),
        };
        string? current = null;
        var loading = false;

        async Task LoadAsync()
        {
            IReadOnlyList<InstalledSdk> installed;
            InstalledSdk? fallback;
            try
            {
                installed = await sdks.GetInstalledAsync(kind);
                fallback = await sdks.GetDefaultAsync(kind);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                installed = [];
                fallback = null;
            }

            loading = true;
            var choices = installed.Select(sdk => new SdkChoice(sdk, IsDefault: fallback is not null && sdk.Path == fallback.Path)).ToList();
            if (canDownload)
                choices.Add(new SdkChoice(null, IsDefault: false));
            box.ItemsSource = choices;
            box.PlaceholderText = installed.Count == 0 ? "No SDK found" : "Choose an SDK";
            if (string.IsNullOrEmpty(current) && fallback is not null)
                Model.Set(option.Id, fallback.Path, byUser: false);
            Select(choices);
            loading = false;
        }

        void Select(IEnumerable<SdkChoice>? choices) =>
            box.SelectedItem = choices?.FirstOrDefault(c => c.Sdk is not null && string.Equals(c.Sdk.Path, current, StringComparison.Ordinal));

        box.SelectionChanged += (_, _) =>
        {
            if (loading || box.SelectedItem is not SdkChoice choice)
                return;

            if (choice.Sdk is null)
            {
                Dispatcher.UIThread.Post(() => Select(box.ItemsSource as IEnumerable<SdkChoice>));
                _ = services.Commands?.ExecuteAsync(CommandIds.Sdks);
                return;
            }

            SetFromField(option, choice.Sdk.Path);
        };

        EventHandler changed = (_, _) => Dispatcher.UIThread.Post(() => _ = LoadAsync());
        box.AttachedToVisualTree += (_, _) =>
        {
            sdks.Changed += changed;
            _ = LoadAsync();
        };
        box.DetachedFromVisualTree += (_, _) => sdks.Changed -= changed;
        void Show(string? value)
        {
            current = value;
            Select(box.ItemsSource as IEnumerable<SdkChoice>);
        }

        return (box, box, Show);
    }

    private static Control SdkChoiceView(SdkChoice? choice)
    {
        if (choice?.Sdk is not { } sdk)
        {
            return new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { new SymbolIcon { Data = Icons.Import, Size = 14 }, new TextBlock { Text = "Download..." } },
            };
        }

        var path = new TextBlock { Text = sdk.Path, Classes = { "caption", "muted" }, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.PathSegmentEllipsis };
        var name = new TextBlock { Text = sdk.DisplayName, VerticalAlignment = VerticalAlignment.Center };
        var line = new DockPanel { Children = { name } };
        if (choice.IsDefault)
        {
            var badge = new Badge { Content = "default", Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            line.Children.Add(badge);
        }

        path.Margin = new Thickness(10, 0, 0, 0);
        line.Children.Add(path);
        foreach (var child in line.Children)
            DockPanel.SetDock(child, Dock.Left);
        ToolTip.SetTip(line, sdk.Path + (sdk.Architecture is { } architecture ? string.Create(CultureInfo.InvariantCulture, $" ({architecture})") : ""));
        return line;
    }

    // TextChanged arrives after the property change, so values follow the Text property itself to stay in step with code that sets it.
    private static void OnTextChanged(TextBox box, Action changed) =>
        box.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
                changed();
        };

    private sealed record SdkChoice(InstalledSdk? Sdk, bool IsDefault);

    private sealed class FieldRow(Option option, Control root, Control field, Control? focus, TextBlock hint, TextBlock problem, Action<string?> sync)
    {
        public Option Option { get; } = option;

        public Control Root { get; } = root;

        public Control Field { get; } = field;

        public Control? Focus { get; } = focus;

        public TextBlock Hint { get; } = hint;

        public TextBlock Problem { get; } = problem;

        public Action<string?> Sync { get; } = sync;

        public string? HintOverride { get; set; }
    }
}
