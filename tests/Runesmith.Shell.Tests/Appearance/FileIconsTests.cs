using Avalonia.Media;
using HammerUI;
using HammerUI.Controls;
using Runesmith.Sdk.Appearance;
using Runesmith.Sdk.Languages;
using Runesmith.Sdk.Settings;
using Runesmith.Sdk.Shell;
using Runesmith.Shell.Appearance;

namespace Runesmith.Shell.Tests.Appearance;

public sealed class FileIconsTests
{
    private const string Box = "M4 4h16v16H4z";
    private const string Dot = "M12 12m-4 0a4 4 0 1 0 8 0a4 4 0 1 0-8 0";
    private const string Bar = "M4 12h16";
    private const string Fold = "M3 6h7l2 2h9v11H3z";
    private const string Page = "M6 2h9l5 5v15H6z";
    private const string Ring = "M12 3a9 9 0 1 0 0.01 0";

    private static readonly FileIconTheme Ember = new("ember.icons", "Ember Icons")
    {
        File = new FileIcon(Page),
        Folder = new FileIcon(Fold, "#E4A23C"),
        FileNames = new Dictionary<string, FileIcon> { ["Dockerfile"] = new(Box, "#2496ED") },
        Extensions = new Dictionary<string, FileIcon> { [".ts"] = new(Dot, "#3178C6"), [".test.ts"] = new(Bar, "#C63131") { LightColor = "#8A1F1F" } },
        Languages = new Dictionary<string, FileIcon> { ["csharp"] = new(Ring, "#9B4F96") { IsFilled = true } },
        FolderNames = new Dictionary<string, FileIcon> { ["src"] = new(Box, "#22C55E") },
    };

    [Fact]
    public Task PicksTheNameThenTheLongestExtensionThenTheLanguageThenTheThemesDefault() => HeadlessSession.Value.Dispatch(() =>
    {
        var (icons, _, _) = Create("ember.icons");

        Assert.Equal(Color.Parse("#2496ED"), icons.For("/repo/dockerfile").Color);
        Assert.Equal(Color.Parse("#C63131"), icons.For("/repo/a.test.ts").Color);
        Assert.Equal(Color.Parse("#3178C6"), icons.For("/repo/a.ts").Color);
        var csharp = icons.For("/repo/Program.cs");
        Assert.Equal(Color.Parse("#9B4F96"), csharp.Color);
        Assert.True(csharp.IsFilled);
        Assert.Null(icons.For("/repo/notes.txt").Color);
        Assert.Equal(Color.Parse("#22C55E"), icons.For("/repo/SRC", isDirectory: true).Color);
        Assert.Equal(Color.Parse("#E4A23C"), icons.For("/repo/docs", isDirectory: true).Color);
    }, CancellationToken.None);

    [Fact]
    public Task KeepsRunesmithsIconsForWhatTheThemeLeavesOutAndWhenTheThemeIsGone() => HeadlessSession.Value.Dispatch(() =>
    {
        var (partial, _, _) = Create("ember.partial", new FileIconTheme("ember.partial", "Partial") { Extensions = new Dictionary<string, FileIcon> { [".ts"] = new(Dot) } });
        var (gone, settings, _) = Create("ember.missing");

        Assert.Same(Icons.Folder, partial.For("/repo/src", isDirectory: true).Data);
        Assert.Same(Icons.FileCode, partial.For("/repo/Program.cs").Data);
        Assert.Equal(BuiltInAppearance.IconTheme, gone.Current.Theme.Id);
        Assert.Equal("ember.missing", settings.Get<string>(SettingKeys.FileIconTheme));
        Assert.Same(Icons.FileCode, gone.For("/repo/Program.cs").Data);
    }, CancellationToken.None);

    [Fact]
    public Task SwitchesThemesLiveAndUsesTheLightColorOnALightTheme() => HeadlessSession.Value.Dispatch(() =>
    {
        var (icons, settings, theme) = Create(BuiltInAppearance.IconTheme);
        var changes = 0;
        icons.Changed += (_, _) => changes++;

        settings.Set(SettingKeys.FileIconTheme, "ember.icons");
        Assert.Equal(1, changes);
        Assert.Equal(Color.Parse("#C63131"), icons.For("a.test.ts").Color);

        theme.IsDark = false;
        theme.RaiseChanged();
        Assert.Equal(2, changes);
        Assert.Equal(Color.Parse("#8A1F1F"), icons.For("a.test.ts").Color);
    }, CancellationToken.None);

    [Fact]
    public Task DrawsAThemedIconInItsColorOrInThePlacesOwnBrush() => HeadlessSession.Value.Dispatch(() =>
    {
        var icon = new SymbolIcon();
        new ThemedIcon(Icons.File, Color.Parse("#3178C6"), IsFilled: true).ApplyTo(icon, "TextSecondaryBrush");
        Assert.True(icon.IsFilled);
        Assert.Equal(Color.Parse("#3178C6"), Assert.IsAssignableFrom<ISolidColorBrush>(icon.Foreground).Color);

        new ThemedIcon(Icons.File).ApplyTo(icon, "TextSecondaryBrush");
        Assert.False(icon.IsFilled);
    }, CancellationToken.None);

    private static (FileIcons Icons, MemorySettings Settings, FakeTheme Theme) Create(string iconTheme, FileIconTheme? extra = null)
    {
        var settings = new MemorySettings();
        settings.Values[SettingKeys.FileIconTheme] = iconTheme;
        var theme = new FakeTheme();
        var catalog = FakeAppearance.Catalog(new FakeAppearance(iconThemes: extra is null ? [Ember] : [Ember, extra]));
        return (new FileIcons(catalog, settings, new FakeLanguages(), theme, new Lazy<IOutputService>(() => new FakeOutput())), settings, theme);
    }

    internal sealed class FakeTheme : IThemeService
    {
        public bool IsDark { get; set; } = true;

        public event EventHandler? Changed;

        public void Toggle() => IsDark = !IsDark;

        public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed class FakeLanguages : ILanguageRegistry
    {
        private static readonly LanguageDefinition Plain = new(ILanguageRegistry.PlainText, "Plain Text") { Icon = "file-text" };
        private static readonly LanguageDefinition CSharp = new("csharp", "C#") { Extensions = [".cs"], Icon = "file-code" };

        public IReadOnlyList<LanguageDefinition> Languages => [Plain, CSharp];

        public LanguageDefinition? Find(string languageId) => Languages.FirstOrDefault(l => l.Id == languageId);

        public LanguageDefinition GetLanguageForFile(string filePath) => filePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ? CSharp : Plain;
    }
}
