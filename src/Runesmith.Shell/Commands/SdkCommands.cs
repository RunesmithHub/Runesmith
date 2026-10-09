using System.Composition;
using Runesmith.Sdk.Commands;
using Runesmith.Shell.Pages;

namespace Runesmith.Shell.Commands;

/// <summary>The command that opens the SDKs settings, next to Settings in the File menu.</summary>
[Export(typeof(ICommandContributor))]
[method: ImportingConstructor]
public sealed class SdkCommands(PageService pages) : ICommandContributor
{
    public void Contribute(ICommandRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.Add(
            new CommandDefinition(CommandIds.Sdks, "SDKs...", "File") { Icon = "package", Description = "Show, download, update and remove .NET SDKs and JDKs." },
            _ =>
            {
                pages.ShowSettings(SdksSection.Category);
                return Task.CompletedTask;
            });
        registry.AddMenuItem(new MenuItemDefinition(Menus.File, CommandIds.Sdks, "4-settings", 1));
    }
}
