using System.Collections.Concurrent;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using HammerUI.Controls;
using Runesmith.Sdk.Sdks;
using Runesmith.Shell.Services;

namespace Runesmith.Shell.Pages;

/// <summary>Picks a release of a kind of SDK, by vendor, version line, package and processor, and installs it with progress.</summary>
internal sealed class SdkDownloadDialog
{
    private const string PlainPackage = "JDK";
    private const string PreferredVendor = "Temurin";

    private static readonly HttpClient Http = new();
    private static readonly ConcurrentDictionary<Uri, Task<long?>> Sizes = new();

    private readonly SdksSection owner;
    private readonly ISdkProvider provider;
    private readonly bool isDotnet;
    private readonly ComboBox vendor = Combo();
    private readonly ComboBox line = Combo();
    private readonly ComboBox package = Combo();
    private readonly ComboBox architecture = Combo();
    private readonly ListBox versions = new() { MaxHeight = 184, SelectionMode = SelectionMode.Single };
    private readonly TextBlock size = new() { Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock message = new() { TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private readonly TextBlock stage = new() { Classes = { "caption", "muted" } };
    private readonly ProgressBar progress = new() { Minimum = 0, Maximum = 1 };
    private readonly StackPanel progressPanel = new() { Spacing = 4, IsVisible = false };
    private readonly Button install = new() { Content = "Install", Classes = { "accent" }, IsDefault = true, MinWidth = 84, IsEnabled = false };
    private readonly Button cancel = new() { Content = "Cancel", IsCancel = true, MinWidth = 84 };
    private readonly Grid form = new() { ColumnDefinitions = new ColumnDefinitions("Auto,12,*"), RowSpacing = 10 };
    private IReadOnlyList<SdkRelease> releases = [];
    private CancellationTokenSource? installing;
    private bool filling;
    private bool refillPosted;

    public SdkDownloadDialog(SdksSection owner, ISdkProvider provider)
    {
        this.owner = owner;
        this.provider = provider;
        isDotnet = provider.Kind == "dotnet";
        Dialog = new Dialog
        {
            Header = $"Download {provider.Name}",
            Description = isDotnet
                ? "Releases from Microsoft. Runesmith installs .NET SDKs side by side in one folder, and global.json picks between them."
                : "Builds of OpenJDK from their vendors: Temurin from Adoptium, the others as the foojay Discovery API lists them.",
            Width = 560,
            ShowCloseButton = false,
            Content = new StackPanel { Spacing = 14, Children = { form, message, progressPanel } },
            Footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { cancel, install } },
        };
        progressPanel.Children.AddRange([stage, progress]);

        if (!isDotnet)
            AddField("Vendor", vendor);
        AddField(isDotnet ? "Channel" : "Version", line);
        if (!isDotnet)
        {
            AddField("Package", package);
            AddField("Architecture", architecture);
        }

        AddField(isDotnet ? "Version" : "Release", versions);
        AddField("Size", size);

        vendor.SelectionChanged += (_, _) => ScheduleRefill();
        line.SelectionChanged += (_, _) => ScheduleRefill();
        package.SelectionChanged += (_, _) => ScheduleRefill();
        architecture.SelectionChanged += (_, _) => ScheduleRefill();
        versions.SelectionChanged += (_, _) => _ = ShowSizeAsync();
        install.Click += (_, _) => _ = InstallAsync();
        cancel.Click += (_, _) =>
        {
            if (installing is { } running)
                running.Cancel();
            else
                Dialog.Close();
        };

        _ = LoadAsync();
    }

    /// <summary>Gets the dialog to show.</summary>
    public Dialog Dialog { get; }

    private SdkRelease? Selected => (versions.SelectedItem as ListBoxItem)?.Tag as SdkRelease;

    private static ComboBox Combo() => new() { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };

    private void AddField(string label, Control field)
    {
        var row = form.RowDefinitions.Count;
        form.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        var text = new TextBlock { Text = label, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = field is ListBox ? VerticalAlignment.Top : VerticalAlignment.Center };
        if (field is ListBox)
            text.Margin = new Thickness(0, 7, 0, 0);
        Grid.SetRow(text, row);
        Grid.SetRow(field, row);
        Grid.SetColumn(field, 2);
        form.Children.AddRange([text, field]);
    }

    private async Task LoadAsync()
    {
        Show("Loading the releases...", isError: false);
        try
        {
            releases = await owner.ReleasesAsync(provider);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or System.Text.Json.JsonException or TaskCanceledException)
        {
            Show($"Could not load the releases: {exception.Message}", isError: true);
            return;
        }

        message.IsVisible = false;
        if (releases.Count == 0)
        {
            Show($"There is no {provider.Name} to download for this computer.", isError: true);
            return;
        }

        filling = true;
        SetItems(vendor, releases.Select(r => r.Vendor).Distinct().Select(v => Item(v, new TextBlock { Text = v })), tag => Equals(tag, PreferredVendor));
        filling = false;
        Refill();
    }

    // A drop-down's own selection change must finish before its items are replaced, so the refill runs right after it.
    private void ScheduleRefill()
    {
        if (filling || refillPosted)
            return;

        refillPosted = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            refillPosted = false;
            Refill();
        });
    }

    // Each choice narrows the next: vendor, then version line, then package and processor, then the releases.
    private void Refill()
    {
        if (filling)
            return;

        filling = true;
        var matching = releases.Where(r => isDotnet || Equals(Tag(vendor), r.Vendor)).ToList();
        var lines = matching.GroupBy(r => r.Line ?? r.Version).OrderByDescending(g => g.Key, SdkService.VersionComparer).ToList();
        SetItems(line, lines.Select(g => Item(g.Key, LineLabel(g.Key, g.First()))), PreferredLine(lines));
        matching = [.. matching.Where(r => Equals(Tag(line), r.Line ?? r.Version))];

        SetItems(package, matching.Select(r => r.Package ?? PlainPackage).Distinct().OrderBy(p => p != PlainPackage).Select(p => Item(p, new TextBlock { Text = p })));
        package.IsEnabled = package.ItemCount > 1;
        matching = [.. matching.Where(r => isDotnet || Equals(Tag(package), r.Package ?? PlainPackage))];

        SetItems(architecture, matching.Select(r => r.Architecture ?? "").Distinct().Select(a => Item(a, new TextBlock { Text = a })));
        architecture.IsEnabled = architecture.ItemCount > 1;
        matching = [.. matching.Where(r => isDotnet || Equals(Tag(architecture), r.Architecture ?? ""))];

        var previous = Selected;
        var sorted = matching.OrderByDescending(r => r.Version, SdkService.VersionComparer).ToList();
        versions.Items.Clear();
        foreach (var release in sorted)
            versions.Items.Add(new ListBoxItem { Tag = release, Content = ReleaseLabel(release, release == sorted[0]) });
        versions.SelectedIndex = sorted.Count == 0 ? -1 : previous is null ? 0 : Math.Max(0, sorted.IndexOf(previous));
        filling = false;
        _ = ShowSizeAsync();
    }

    private static ComboBoxItem Item(object tag, Control content) => new() { Tag = tag, Content = content };

    private static object? Tag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag;

    // Keeps the selected choice when it is still offered, and otherwise selects the preferred one or the first.
    private static void SetItems(ComboBox box, IEnumerable<ComboBoxItem> items, Func<object?, bool>? preferred = null)
    {
        var previous = Tag(box);
        box.Items.Clear();
        foreach (var item in items)
            box.Items.Add(item);
        var all = box.Items.OfType<ComboBoxItem>().ToList();
        box.SelectedItem = all.Find(i => Equals(i.Tag, previous)) ?? (preferred is null ? null : all.Find(i => preferred(i.Tag))) ?? all.FirstOrDefault();
    }

    // The newest long-term support line, or else the newest that is not a preview.
    private static Func<object?, bool> PreferredLine(List<IGrouping<string, SdkRelease>> lines)
    {
        var stable = lines.Where(g => g.First().SupportPhase is not ("preview" or "go-live") && !g.First().Version.Contains('-', StringComparison.Ordinal)).ToList();
        var pick = stable.Find(g => g.First().IsLongTermSupport) ?? stable.FirstOrDefault();
        return tag => pick is not null && Equals(tag, pick.Key);
    }

    private StackPanel LineLabel(string name, SdkRelease sample)
    {
        var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center } } };
        if (sample.IsLongTermSupport)
            label.Children.Add(SdksSection.Badge("LTS", "success"));
        else if (isDotnet)
            label.Children.Add(SdksSection.Badge("STS"));
        if (sample.SupportPhase is "preview" or "go-live")
            label.Children.Add(SdksSection.Badge("Preview", "warning"));
        else if (sample.SupportPhase is "maintenance" or "eol")
            label.Children.Add(new TextBlock { Text = sample.SupportPhase == "eol" ? "out of support" : "maintenance", Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center });
        return label;
    }

    private static StackPanel ReleaseLabel(SdkRelease release, bool isLatest)
    {
        var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new TextBlock { Text = release.Version, VerticalAlignment = VerticalAlignment.Center } } };
        if (isLatest)
            label.Children.Add(SdksSection.Badge("Latest", "accent"));
        if (release.Version.Contains('-', StringComparison.Ordinal))
            label.Children.Add(SdksSection.Badge("Preview", "warning"));
        if (release.Released is { } date)
            label.Children.Add(new TextBlock { Text = date.ToString("d", CultureInfo.CurrentCulture), Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center });
        return label;
    }

    private async Task ShowSizeAsync()
    {
        install.IsEnabled = Selected is not null && installing is null;
        if (Selected is not { } release)
        {
            size.Text = "";
            return;
        }

        size.Text = release.Size is { } known ? SdksSection.FormatSize(known) : "...";
        if (release.Size is not null)
            return;

        var bytes = await Sizes.GetOrAdd(release.Download, HeadAsync);
        if (Selected == release)
            size.Text = bytes is { } length ? SdksSection.FormatSize(length) : "Unknown";
    }

    private static async Task<long?> HeadAsync(Uri uri)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, uri);
            using var response = await Http.SendAsync(request);
            return response.IsSuccessStatusCode ? response.Content.Headers.ContentLength : null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private async Task InstallAsync()
    {
        if (Selected is not { } release || installing is not null)
            return;

        using var cancellation = new CancellationTokenSource();
        installing = cancellation;
        form.IsEnabled = false;
        install.IsEnabled = false;
        message.IsVisible = false;
        progressPanel.IsVisible = true;
        stage.Text = "Starting";
        progress.IsIndeterminate = true;
        var (succeeded, error) = await owner.InstallAsync(provider, release, p =>
        {
            stage.Text = p.Fraction is { } f ? $"{p.Stage} {f:P0}" : p.Stage;
            progress.IsIndeterminate = p.Fraction is null;
            progress.Value = p.Fraction ?? 0;
        }, cancellation);
        installing = null;
        if (succeeded)
        {
            Dialog.Close();
            return;
        }

        form.IsEnabled = true;
        install.IsEnabled = true;
        progressPanel.IsVisible = false;
        Show(error is null ? "The install was cancelled." : $"Could not install: {error}", isError: error is not null);
    }

    private void Show(string text, bool isError)
    {
        message.Text = text;
        message[!TextBlock.ForegroundProperty] = new DynamicResourceExtension(isError ? "DangerBrush" : "TextSecondaryBrush");
        message.IsVisible = true;
    }
}
