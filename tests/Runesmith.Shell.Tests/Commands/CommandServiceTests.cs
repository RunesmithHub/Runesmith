using Runesmith.Sdk.Commands;
using Runesmith.Sdk.Shell;
using Runesmith.Shell.Commands;

namespace Runesmith.Shell.Tests.Commands;

public sealed class CommandServiceTests
{
    [Fact]
    public void ACommandSitsInAMenuOnceWhenTwoPartsPlaceIt()
    {
        var commands = new CommandService([], new Lazy<INotificationService>(() => throw new InvalidOperationException()));

        commands.AddMenuItem(new MenuItemDefinition(Menus.File, CommandIds.OpenFolder, "1-new", 3));
        commands.AddMenuItem(new MenuItemDefinition(Menus.File, CommandIds.OpenFolder, "vcs", 0));
        commands.AddMenuItem(new MenuItemDefinition("Graph", CommandIds.OpenFolder));

        Assert.Equal([Menus.File, "Graph"], commands.MenuItems.Select(i => i.Menu));
        Assert.Equal("1-new", commands.MenuItems[0].Group);
    }

    [Fact]
    public void AfterTheLanguageChangesLocalizedContributorsGiveTheirTitlesAgain()
    {
        var contributor = new Titles { Language = "en" };
        var runs = 0;
        var commands = new CommandService([new Lazy<ICommandContributor>(() => contributor)], new Lazy<INotificationService>(() => throw new InvalidOperationException()));
        contributor.Run = () => runs++;
        contributor.Contribute(commands);
        var changed = 0;
        commands.Changed += (_, _) => changed++;

        contributor.Language = "nl";
        commands.Retitle(_ => true);

        var save = commands.Find("tests.save")!;
        Assert.Equal(("Opslaan", "Bestand", "Bewaart het bestand."), (save.Title, save.Category, save.Description));
        Assert.Equal("Ctrl+S", save.KeyBinding);
        Assert.Equal("Extra", Assert.Single(commands.Menus).Title);
        Assert.Single(commands.MenuItems);
        Assert.Equal(1, changed);
        Assert.True(commands.CanExecute("tests.save"));
        Assert.Equal(0, runs);
    }

    [Fact]
    public void AContributorWithoutTranslationsIsNotAskedAgain()
    {
        var contributor = new Titles { Language = "en" };
        var commands = new CommandService([new Lazy<ICommandContributor>(() => contributor)], new Lazy<INotificationService>(() => throw new InvalidOperationException()));
        contributor.Contribute(commands);

        contributor.Language = "nl";
        commands.Retitle();

        Assert.Equal("Save", commands.Find("tests.save")!.Title);
        Assert.Equal(1, contributor.Asked);
    }

    private sealed class Titles : ICommandContributor
    {
        public string Language { get; set; } = "en";

        public Action Run { get; set; } = () => { };

        public int Asked { get; private set; }

        public void Contribute(ICommandRegistry registry)
        {
            Asked++;
            var dutch = Language == "nl";
            registry.Add(
                new CommandDefinition("tests.save", dutch ? "Opslaan" : "Save", dutch ? "Bestand" : "File") { KeyBinding = "Ctrl+S", Description = dutch ? "Bewaart het bestand." : "Saves the file." },
                _ =>
                {
                    Run();
                    return Task.CompletedTask;
                });
            registry.AddMenu(new MenuDefinition("tests.extra", dutch ? "Extra" : "Extras"));
            registry.AddMenuItem(new MenuItemDefinition("tests.extra", "tests.save"));
        }
    }
}
