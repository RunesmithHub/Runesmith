using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using HammerUI;
using HammerUI.Controls;
using Runesmith.Sdk.Options;
using Runesmith.Sdk.Shell;
using Runesmith.Sdk.Templates;
using Runesmith.Shell.Forms;
using Runesmith.Shell.Services;
using Runesmith.Shell.Session;

namespace Runesmith.Shell.Pages;

/// <summary>The New Project dialog: the templates of every provider on the left, searchable and grouped by kind, and the chosen template's
/// form on the right. It creates the project itself, showing the progress, and closes with the <see cref="ProjectCreationResult"/>.</summary>
internal sealed class NewProjectDialog : Dialog, IDisposable
{
    private const double ListWidth = 372;
    private const double RowHeight = 44;
    private const double GroupHeight = 30;

    private readonly IReadOnlyList<IProjectTemplateProvider> providers;
    private readonly OptionFormServices services;
    private readonly IOutputChannel log;
    private readonly bool offerGit;
    private readonly List<TemplateEntry> entries = [];
    private readonly SearchBox search = new() { PlaceholderText = "Search templates", Margin = new Thickness(12, 12, 8, 8) };
    private readonly WrapPanel languageChips = new() { ItemSpacing = 4, LineSpacing = 4, Margin = new Thickness(0, 12, 12, 8), VerticalAlignment = VerticalAlignment.Center };
    private readonly WrapPanel categoryChips = new() { ItemSpacing = 4, LineSpacing = 4, Margin = new Thickness(12, 0, 12, 8) };
    private readonly ListBox list = new() { Classes = { "templates" }, SelectionMode = SelectionMode.Single };
    private readonly TextBlock listStatus = new() { Classes = { "muted" }, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(16, 24), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
    private readonly ContentControl detail = new();
    private readonly Button create = new() { Content = "Create", Classes = { "accent" }, IsDefault = true, MinWidth = 96, IsEnabled = false };
    private readonly Button cancel = new() { Content = "Cancel", MinWidth = 84 };
    private readonly SymbolIcon spinner = new() { Data = Icons.RotateCw, Size = 14, Classes = { "spin" }, IsVisible = false, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock progress = new() { Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 560, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly CancellationTokenSource closing = new();
    private NewProjectSession session;
    private string? language;
    private string? category;
    private int pendingProviders;
    private TemplateEntry? selected;
    private OptionForm? form;
    private string? typedName;
    private string? location;
    private bool createGit;
    private bool isCreating;
    private bool isDisposed;
    private bool userPicked;
    private bool refilling;

    public NewProjectDialog(IReadOnlyList<IProjectTemplateProvider> providers, OptionFormServices services, IOutputChannel log, bool offerGit, NewProjectSession session)
    {
        this.providers = providers;
        this.services = services;
        this.log = log;
        this.offerGit = offerGit;
        this.session = session;
        location = session.Location is { } last && Directory.Exists(last) ? last : DefaultLocation();
        createGit = session.CreateGitRepository;

        Header = "New Project";
        Width = 1040;
        Height = 760;
        Padding = new Thickness(0);
        ShowCloseButton = true;
        Styles.Add(CreateStyles());

        list.ItemTemplate = new FuncDataTemplate<TemplateListRow>((row, _) => row switch
        {
            TemplateGroupRow group => GroupView(group),
            TemplateRow template => TemplateView(template.Entry.Template),
            _ => new Panel(),
        });
        list.ContainerPrepared += (_, e) =>
        {
            var isGroup = e.Container.DataContext is TemplateGroupRow;
            e.Container.IsEnabled = !isGroup;
            e.Container.Focusable = !isGroup;
            e.Container.Height = isGroup ? GroupHeight : RowHeight;
            e.Container.Classes.Set("group", isGroup);
        };
        list.SelectionChanged += (_, _) => OnListSelectionChanged();
        search.PropertyChanged += (_, e) =>
        {
            if (e.Property == SearchBox.TextProperty)
                Refill(keepSelection: false);
        };
        search.AddHandler(KeyDownEvent, OnSearchKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        var left = new DockPanel { Width = ListWidth };
        left.Bind(BackgroundProperty, left.GetResourceObservable("SurfaceBrush"));
        var top = new DockPanel { Children = { languageChips, search } };
        DockPanel.SetDock(languageChips, Dock.Right);
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(categoryChips, Dock.Top);
        left.Children.AddRange([top, categoryChips, new Panel { Children = { list, listStatus } }]);

        var divider = new Border { Width = 1 };
        divider.Bind(Border.BackgroundProperty, divider.GetResourceObservable("BorderSubtleBrush"));
        var right = new ScrollViewer { Content = detail, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(24, 16, 28, 20) };
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions($"{ListWidth.ToString(CultureInfo.InvariantCulture)},Auto,*"), Children = { left, divider, right, ResizeGrip() } };
        Grid.SetColumn(divider, 1);
        Grid.SetColumn(right, 2);
        Content = new Border { Margin = new Thickness(0, 12, 0, 0), Child = body };
        if (Content is Border border)
        {
            border.BorderThickness = new Thickness(0, 1, 0, 0);
            border.Bind(Border.BorderBrushProperty, border.GetResourceObservable("BorderSubtleBrush"));
        }

        Footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { spinner, progress, new Border { Width = 16 }, cancel, create },
        };
        create.Click += (_, _) => _ = CreateAsync();
        cancel.Click += (_, _) => Close();
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None && e.Source is not (Button or ComboBox or ComboBoxItem) && create.IsEnabled)
            {
                e.Handled = true;
                _ = CreateAsync();
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        AttachedToVisualTree += (_, _) => _ = LoadAsync();
        DetachedFromVisualTree += (_, _) => Dispose();
    }

    protected override Type StyleKeyOverride => typeof(Dialog);

    /// <summary>Stops loading templates and creating a project, such as when the dialog closes.</summary>
    public void Dispose()
    {
        if (isDisposed)
            return;

        isDisposed = true;
        closing.Cancel();
        closing.Dispose();
    }

    /// <summary>Gets the folder new projects go in when none was used yet: a Projects folder in the home folder when there is one.</summary>
    private static string DefaultLocation()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var projects = Path.Combine(home, "Projects");
        return Directory.Exists(projects) ? projects : home;
    }

    private async Task LoadAsync()
    {
        var token = closing.Token;
        pendingProviders = providers.Count;
        UpdateListStatus();
        var tasks = providers.Select(provider => Task.Run(() => provider.GetTemplatesAsync(token), token).ContinueWith(task => (provider, task), TaskScheduler.Default)).ToList();
        while (tasks.Count > 0)
        {
            var done = await Task.WhenAny(tasks);
            tasks.Remove(done);
            var (provider, task) = await done;
            pendingProviders--;
            if (task.IsCompletedSuccessfully)
            {
                entries.AddRange(task.Result.Select(template => new TemplateEntry(template, provider)));
            }
            else if (!token.IsCancellationRequested)
            {
                log.AppendLine($"{provider.GetType().FullName} could not list its templates: {task.Exception?.GetBaseException()}");
            }

            if (!token.IsCancellationRequested)
            {
                FillChips();
                Refill(keepSelection: true);
            }
        }
    }

    private void UpdateListStatus()
    {
        var hasRows = list.ItemCount > 0;
        listStatus.IsVisible = !hasRows;
        listStatus.Text = entries.Count == 0 && pendingProviders > 0 ? "Loading templates..."
            : entries.Count == 0 ? "No templates are installed. Template plugins, such as C# templates and Java templates, add them; check that they are on in Settings > Plugins."
            : "No templates match. Try other words or clear the filters.";
    }

    private void FillChips()
    {
        Fill(languageChips, TemplateCatalog.Languages(entries), language, value => language = value);
        Fill(categoryChips, TemplateCatalog.Categories(entries), category, value => category = value);

        // No chip checked means no filter; clicking the checked chip turns its filter off.
        void Fill(WrapPanel panel, IReadOnlyList<string> values, string? current, Action<string?> set)
        {
            panel.Children.Clear();
            panel.IsVisible = values.Count > 1;
            foreach (var value in values)
            {
                var chip = Chip(value, string.Equals(current, value, StringComparison.OrdinalIgnoreCase));
                chip.Click += (_, _) =>
                {
                    set(string.Equals(current, value, StringComparison.OrdinalIgnoreCase) ? null : value);
                    FillChips();
                    Refill(keepSelection: false);
                };
                panel.Children.Add(chip);
            }
        }
    }

    private static ToggleButton Chip(string text, bool isChecked) => new() { Content = text, IsChecked = isChecked, Classes = { "filter" } };

    /// <param name="keepSelection">Whether the selected template stays selected when it still shows; otherwise the first match is selected.</param>
    private void Refill(bool keepSelection)
    {
        var rows = TemplateCatalog.Rows(entries, session.Recent, search.Text, language, category);
        var keep = keepSelection && userPicked ? selected : null;
        refilling = true;
        list.ItemsSource = rows;
        var match = rows.OfType<TemplateRow>().FirstOrDefault(r => ReferenceEquals(r.Entry, keep)) ?? rows.OfType<TemplateRow>().FirstOrDefault();
        list.SelectedItem = match;
        if (rows.Count > 0)
            list.ScrollIntoView(0);
        if (match is not null)
            list.ScrollIntoView(match);
        refilling = false;
        UpdateListStatus();
        if (match is null)
            Select(null);
    }

    private void OnListSelectionChanged()
    {
        if (list.SelectedItem is TemplateGroupRow && list.ItemsSource is IReadOnlyList<TemplateListRow> rows)
        {
            var index = list.SelectedIndex;
            var next = rows.Skip(index + 1).OfType<TemplateRow>().FirstOrDefault() ?? rows.Take(index).OfType<TemplateRow>().LastOrDefault();
            list.SelectedItem = next;
            return;
        }

        if (list.SelectedItem is not TemplateRow row)
            return;

        userPicked |= !refilling;
        if (!ReferenceEquals(row.Entry, selected))
            Select(row.Entry);
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Down or Key.Up) || list.ItemsSource is not IReadOnlyList<TemplateListRow> rows)
            return;

        var templates = rows.OfType<TemplateRow>().ToList();
        var index = list.SelectedItem is TemplateRow current ? templates.IndexOf(current) : -1;
        var next = e.Key == Key.Down ? Math.Min(index + 1, templates.Count - 1) : Math.Max(index - 1, 0);
        if (next >= 0 && next < templates.Count)
        {
            list.SelectedItem = templates[next];
            list.ScrollIntoView(templates[next]);
        }

        e.Handled = true;
    }

    private void Select(TemplateEntry? entry)
    {
        if (form is not null)
            RememberDialogFields(form.Model.Values);

        selected = entry;
        form = null;
        if (entry is null)
        {
            detail.Content = null;
            create.IsEnabled = false;
            return;
        }

        var template = entry.Template;
        session.Values.TryGetValue(template.Id, out var remembered);
        var values = NewProjectForm.InitialValues(template, remembered);
        values.Set(NewProjectForm.Name, typedName ?? NewProjectForm.SuggestName(template, location));
        values.Set(NewProjectForm.Location, location);
        if (offerGit)
            values.Set(NewProjectForm.Git, createGit ? "true" : "false");

        form = new OptionForm(NewProjectForm.Options(template, offerGit), values, services, NewProjectForm.Validate) { BeforeAdvanced = AboutSection(template) };
        var current = form;
        form.ValueChanged += (_, id) =>
        {
            if (id == NewProjectForm.Name)
                typedName = current.Model.Values.Get(id);
            UpdateLocationHint(current);
        };
        form.ValidityChanged += (_, _) => UpdateCreateButton();
        UpdateLocationHint(form);
        detail.Content = new StackPanel { Spacing = 18, Children = { TemplateHeader(template), form } };
        UpdateCreateButton();
    }

    private void RememberDialogFields(OptionValues values)
    {
        location = values.Get(NewProjectForm.Location) ?? location;
        if (offerGit)
            createGit = values.GetBool(NewProjectForm.Git);
    }

    private static void UpdateLocationHint(OptionForm form) =>
        form.SetHint(NewProjectForm.Location, NewProjectForm.TargetFolder(form.Model.Values) is { } folder ? $"The project will be created in {folder}" : null);

    private void UpdateCreateButton() => create.IsEnabled = !isCreating && form?.IsValid == true;

    private async Task CreateAsync()
    {
        if (isCreating || form is null || selected is null)
            return;

        form.Revalidate();
        if (!form.IsValid)
        {
            form.RevealProblems();
            return;
        }

        var entry = selected;
        var values = form.Model.Values;
        RememberDialogFields(values);
        var name = values.Get(NewProjectForm.Name)!.Trim();
        var target = NewProjectForm.TargetFolder(values)!;
        var gitRequested = offerGit && values.GetBool(NewProjectForm.Git);
        var request = new ProjectCreationRequest(name, values.Get(NewProjectForm.Location)!.Trim(), NewProjectForm.TemplateValues(values));

        var token = closing.Token;
        SetCreating(true);
        ShowProgress($"Creating {name}...", isError: false);
        var reporter = new Progress<string>(step => ShowProgress(step, isError: false));
        try
        {
            var result = await Task.Run(() => entry.Provider.CreateAsync(entry.Template, request, reporter, token), token);
            if (gitRequested)
            {
                ShowProgress("Creating the Git repository...", isError: false);
                await Git.InitAsync(result.Folder, log, token);
            }

            session = session.WithCreated(entry.Template.Id, request.Location, NewProjectForm.ChangedValues(entry.Template, values), gitRequested || (createGit && !offerGit));
            session.Save();
            Close(result);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            log.AppendLine($"Creating {name} in {target} from {entry.Template.Name} failed: {exception}");
            ShowProgress(exception.Message, isError: true);
            SetCreating(false);
        }
    }

    private void SetCreating(bool creating)
    {
        isCreating = creating;
        spinner.IsVisible = creating;
        list.IsEnabled = !creating;
        search.IsEnabled = !creating;
        detail.IsEnabled = !creating;
        UpdateCreateButton();
    }

    private void ShowProgress(string text, bool isError)
    {
        progress.Text = text;
        ToolTip.SetTip(progress, text);
        progress.Classes.Set("muted", !isError);
        if (isError)
            progress.Bind(TextBlock.ForegroundProperty, progress.GetResourceObservable("DangerBrush"));
        else
            progress.ClearValue(TextBlock.ForegroundProperty);
    }

    private static StackPanel GroupView(TemplateGroupRow group)
    {
        var title = new TextBlock { Text = group.Title.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeight.SemiBold, LetterSpacing = 0.6, VerticalAlignment = VerticalAlignment.Bottom };
        title.Bind(TextBlock.ForegroundProperty, title.GetResourceObservable("TextMutedBrush"));
        var count = new TextBlock { Text = group.Count.ToString(CultureInfo.InvariantCulture), FontSize = 11, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(6, 0, 0, 0) };
        count.Bind(TextBlock.ForegroundProperty, count.GetResourceObservable("TextDisabledBrush"));
        return new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 0, 0, 5), VerticalAlignment = VerticalAlignment.Bottom, Children = { title, count } };
    }

    private static Grid TemplateView(ProjectTemplate template)
    {
        var name = new TextBlock { Text = template.Name, FontWeight = FontWeight.Medium, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var badge = new Badge { Content = template.Language, Margin = new Thickness(6, 0, 0, 0), Classes = { "language" } };
        var line = new DockPanel { Children = { badge, name } };
        DockPanel.SetDock(badge, Dock.Right);
        var description = new TextBlock { Text = template.Description ?? template.Source ?? "", FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
        description.Bind(TextBlock.ForegroundProperty, description.GetResourceObservable("TextMutedBrush"));
        var text = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Children = { line, description } };
        var icon = IconTile(template, 28, 15);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,10,*"), Children = { icon, text } };
        Grid.SetColumn(text, 2);
        ToolTip.SetTip(grid, template.Description);
        ToolTip.SetShowDelay(grid, 700);
        return grid;
    }

    private static Border IconTile(ProjectTemplate template, double size, double iconSize)
    {
        var tile = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 4),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new SymbolIcon { Data = Icons.Find(template.Icon) ?? Icons.FileCode, Size = iconSize, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            Classes = { "template-icon" },
        };
        return tile;
    }

    private static Grid TemplateHeader(ProjectTemplate template)
    {
        var name = new TextBlock { Text = template.Name, FontSize = 18, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
        var facts = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new Badge { Content = template.Language, Classes = { "language" } }, new Badge { Content = template.Category } } };
        if (template.Source is { } source)
        {
            var sourceText = new TextBlock { Text = source, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
            sourceText.Bind(TextBlock.ForegroundProperty, sourceText.GetResourceObservable("TextMutedBrush"));
            facts.Children.Add(sourceText);
        }

        var text = new StackPanel { Spacing = 6, Children = { name, facts } };
        if (template.Description is { Length: > 0 } description)
        {
            var summary = new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.WordEllipsis };
            summary.Bind(TextBlock.ForegroundProperty, summary.GetResourceObservable("TextSecondaryBrush"));
            text.Children.Add(summary);
        }

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,14,*"), Children = { IconTile(template, 44, 22), text } };
        Grid.SetColumn(text, 2);
        return grid;
    }

    private static SectionPanel AboutSection(ProjectTemplate template)
    {
        var facts = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,12,*"), Margin = new Thickness(0, 4, 0, 0) };
        (string Name, string? Value)[] rows =
        [
            ("Description", template.Description),
            ("Author", template.Author),
            ("Tags", template.Tags.Count > 0 ? string.Join(", ", template.Tags) : null),
            ("Source", template.Source),
            ("Id", template.Id),
        ];
        foreach (var (label, value) in rows.Where(r => !string.IsNullOrWhiteSpace(r.Value)))
        {
            var row = facts.RowDefinitions.Count;
            facts.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var name = new TextBlock { Text = label, Margin = new Thickness(0, 3), HorizontalAlignment = HorizontalAlignment.Right };
            name.Bind(TextBlock.ForegroundProperty, name.GetResourceObservable("TextSecondaryBrush"));
            var text = new SelectableTextBlock { Text = value, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3) };
            Grid.SetRow(name, row);
            Grid.SetRow(text, row);
            Grid.SetColumn(text, 2);
            facts.Children.AddRange([name, text]);
        }

        return new SectionPanel { Header = "About this template", IsCollapsible = true, IsExpanded = false, Content = facts, Margin = new Thickness(0, 12, 0, 0), Padding = new Thickness(12, 0, 0, 8), BorderThickness = new Thickness(0, 1, 0, 0) };
    }

    private Thumb ResizeGrip()
    {
        var grip = new Thumb
        {
            Width = 14,
            Height = 14,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Cursor = new Cursor(StandardCursorType.BottomRightCorner),
            Classes = { "resize-grip" },
        };
        ToolTip.SetTip(grip, "Drag to resize");
        grip.DragDelta += (_, e) =>
        {
            // The dialog stays centered, so the corner follows the pointer when the size changes by twice the move.
            Width = Math.Max(760, Bounds.Width + (2 * e.Vector.X));
            Height = Math.Max(480, Bounds.Height + (2 * e.Vector.Y));
        };
        Grid.SetColumnSpan(grip, 3);
        return grip;
    }

    private static Styles CreateStyles() =>
    [
        new Style(x => x.OfType<ListBox>().Class("templates"))
        {
            Setters = { new Setter(BackgroundProperty, Brushes.Transparent), new Setter(PaddingProperty, new Thickness(8, 0, 8, 8)) },
        },
        new Style(x => x.OfType<ListBox>().Class("templates").Descendant().OfType<ListBoxItem>())
        {
            Setters = { new Setter(PaddingProperty, new Thickness(8, 0)), new Setter(MinHeightProperty, 0d), new Setter(CornerRadiusProperty, new CornerRadius(6)) },
        },
        new Style(x => x.OfType<ListBox>().Class("templates").Descendant().OfType<ListBoxItem>().Class("group"))
        {
            Setters = { new Setter(OpacityProperty, 1d), new Setter(CursorProperty, Cursor.Default) },
        },
        new Style(x => x.OfType<ToggleButton>().Class("filter"))
        {
            Setters =
            {
                new Setter(HeightProperty, 24d),
                new Setter(MinHeightProperty, 24d),
                new Setter(PaddingProperty, new Thickness(8, 0)),
                new Setter(FontSizeProperty, 12d),
                new Setter(CornerRadiusProperty, new CornerRadius(12)),
                new Setter(BorderThicknessProperty, new Thickness(1)),
            },
        },
        new Style(x => x.OfType<Border>().Class("template-icon"))
        {
            Setters = { new Setter(Border.BackgroundProperty, new DynamicResourceExtension("AccentSubtleBrush")) },
        },
        new Style(x => x.OfType<Border>().Class("template-icon").Descendant().OfType<SymbolIcon>())
        {
            Setters = { new Setter(ForegroundProperty, new DynamicResourceExtension("AccentBrush")) },
        },
        new Style(x => x.OfType<Thumb>().Class("resize-grip"))
        {
            Setters = { new Setter(BackgroundProperty, Brushes.Transparent), new Setter(OpacityProperty, 0d) },
        },
    ];
}
