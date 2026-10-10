using System.Reflection;
using HammerUI.Controls;
using HammerUI.Services;
using Runesmith.Composition;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Plugins;
using Runesmith.Sdk.Shell;
using Runesmith.Sdk.ToolWindows;
using Runesmith.Sdk.Terminals;
using Runesmith.Shell.Appearance;
using Runesmith.Shell.Diffs;
using Runesmith.Shell.QuickInput;
using Runesmith.Shell.Services;
using Runesmith.Shell.TreeViews;
using Runesmith.Shell.Terminal;

namespace Runesmith.Shell.Tests.Services;

public sealed class HostExportsTests
{
    [Fact]
    public async Task PluginsCanImportTheHostServices()
    {
        string[] hostAssemblies = ["Runesmith.Sdk", "Runesmith.Workspace", "Runesmith.Languages", "Runesmith.Editor", "Runesmith.Shell"];
        using var result = await RunesmithComposition.CreateAsync(new CompositionOptions([.. hostAssemblies.Select(Assembly.Load)], [], null), TestContext.Current.CancellationToken);

        Assert.Empty(result.Errors);
        await HeadlessSession.Value.Dispatch(() =>
        {
            var notifications = result.Exports.GetExportedValue<NotificationService>();
            Assert.Same(notifications.Dialogs, result.Exports.GetExportedValue<IDialogService>());
            Assert.Same(notifications.Toasts, result.Exports.GetExportedValue<IToastService>());
            Assert.Same(result.Exports.GetExportedValue<ILauncher>(), result.Exports.GetExportedValue<ILauncher>());
            Assert.Same(result.Exports.GetExportedValue<ISecretStore>(), result.Exports.GetExportedValue<ISecretStore>());
            Assert.IsType<DiffService>(result.Exports.GetExportedValue<IDiffService>());
            Assert.IsType<PluginStorage>(result.Exports.GetExportedValue<IPluginStorage>());
            Assert.IsType<ClipboardService>(result.Exports.GetExportedValue<IClipboard>());
            Assert.Same(result.Exports.GetExportedValue<QuickInputService>(), result.Exports.GetExportedValue<IQuickInputService>());
            Assert.Same(result.Exports.GetExportedValue<TreeViewService>(), result.Exports.GetExportedValue<ITreeViewService>());
            Assert.Same(result.Exports.GetExportedValue<ITerminalService>(), result.Exports.GetExportedValue<TerminalService>());
            var appearance = result.Exports.GetExportedValue<AppearanceCatalog>();
            Assert.Equal(["dark", "light", "runesmith-low-contrast-dark", "runesmith-low-contrast-light"], appearance.Themes.Select(e => e.Theme.Id));
            Assert.Equal(["runesmith-dark", "runesmith-light", "runesmith-low-contrast-dark", "runesmith-low-contrast-light"], appearance.Schemes.Select(e => e.Scheme.Id));
            Assert.Empty(appearance.Problems);
        }, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("mailto:someone@example.com")]
    public void TheLauncherOpensOnlyWebAddresses(string address) =>
        Assert.Throws<ArgumentException>(() => new SystemLauncher(() => null, _ => { }).OpenUrl(new Uri(address)));
}
