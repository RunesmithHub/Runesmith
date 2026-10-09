using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Runesmith.Sdk.Sdks;
using Runesmith.Shell.Pages;
using Runesmith.Shell.Services;
using Runesmith.Shell.Tests.Services;

namespace Runesmith.Shell.Tests.Pages;

public sealed class SdkDownloadDialogTests
{
    private static readonly SdkRelease[] Releases =
    [
        Release("Temurin", "25.0.1+8", "25", lts: true),
        Release("Temurin", "25.0.0+36", "25", lts: true),
        Release("Temurin", "21.0.9+10", "21", lts: true),
        Release("Zulu", "25.0.1+8", "25", lts: true),
    ];

    [Fact]
    public Task ChoosingAnotherVersionLineListsItsReleases() => HeadlessSession.Value.Dispatch(async () =>
    {
        var provider = new ReleasesProvider();
        var section = new SdksSection(new SdkService([provider], new SdkServiceTests.FakeSettings()), new NotificationService(), null);
        var dialog = new SdkDownloadDialog(section, provider);
        var window = new Window { Content = dialog.Dialog };
        window.Show();
        await Settle();

        var line = Field<ComboBox>(dialog, "line");
        var versions = Field<ListBox>(dialog, "versions");
        Assert.Equal("25", Tag(line));

        var index21 = line.Items.OfType<ComboBoxItem>().ToList().FindIndex(i => Equals(i.Tag, "21"));
        Choose(line, index21);
        await Settle();
        Choose(line, 0);
        await Settle();
        Choose(line, index21);
        await Settle();

        Assert.Equal("21", Tag(line));
        Assert.Equal(["21.0.9+10"], versions.Items.OfType<ListBoxItem>().Select(i => ((SdkRelease)i.Tag!).Version));
        window.Close();
    }, CancellationToken.None);

    // A click selects through the control's own selection update, which is where replacing its items during the change broke.
    private static void Choose(ComboBox box, int index) =>
        typeof(SelectingItemsControl).GetMethod("UpdateSelection", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
            [typeof(int), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool)])!
            .Invoke(box, [index, true, false, false, false, false]);

    private static async Task Settle()
    {
        for (var i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Yield();
        }
    }

    private static object? Tag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag;

    private static T Field<T>(SdkDownloadDialog dialog, string name) =>
        (T)typeof(SdkDownloadDialog).GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(dialog)!;

    private static SdkRelease Release(string vendor, string version, string line, bool lts) =>
        new("jdk", version, vendor, new Uri($"https://example.com/{vendor}-{version}.tar.gz")) { Line = line, Architecture = "x64", IsLongTermSupport = lts };

    private sealed class ReleasesProvider : ISdkProvider
    {
        public string Kind => "jdk";

        public string Name => "JDK";

        public Task<IReadOnlyList<InstalledSdk>> FindInstalledAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<InstalledSdk>>([]);

        public Task<IReadOnlyList<SdkRelease>> GetReleasesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<SdkRelease>>(Releases);

        public Task<InstalledSdk> InstallAsync(SdkRelease release, IProgress<SdkInstallProgress> progress, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UninstallAsync(InstalledSdk sdk, CancellationToken cancellationToken) => throw new NotSupportedException();

        public SdkRelease? FindUpdate(InstalledSdk sdk, IReadOnlyList<SdkRelease> releases) => null;
    }
}
