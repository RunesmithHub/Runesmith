using System.Reflection;
using Runesmith.Composition;
using Runesmith.Sdk.Commands;
using Runesmith.Shell.Commands;

namespace Runesmith.Shell.Tests.Commands;

public sealed class KeyBindingClashTests
{
    [Fact]
    public async Task NoTwoOfRunesmithsCommandsShareAKey()
    {
        string[] hostAssemblies = ["Runesmith.Sdk", "Runesmith.Workspace", "Runesmith.Languages", "Runesmith.Editor", "Runesmith.Shell"];
        using var result = await RunesmithComposition.CreateAsync(new CompositionOptions([.. hostAssemblies.Select(Assembly.Load)], [], null), TestContext.Current.CancellationToken);

        await HeadlessSession.Value.Dispatch(() =>
        {
            var registry = new Recorder();
            foreach (var contributor in result.Exports.GetExportedValues<ICommandContributor>())
                contributor.Contribute(registry);

            var gestures = registry.Commands
                .SelectMany(c => KeyBindings.Parse(c.KeyBinding, isDefault: true).Select(g => (Gesture: KeyBindings.Format(g), c.Id)))
                .GroupBy(g => g.Gesture)
                .Where(g => g.Count() > 1)
                .Select(g => $"{g.Key}: {string.Join(", ", g.Select(c => c.Id))}");
            Assert.Empty(gestures);
            Assert.Contains(registry.Commands, c => c.Id == CommandIds.FindReferences && c.KeyBinding == "Shift+F12");
            Assert.Contains(registry.Commands, c => c.Id == CommandIds.GoToImplementation && c.KeyBinding == "Ctrl+F12");
        }, TestContext.Current.CancellationToken);
    }

    private sealed class Recorder : ICommandRegistry
    {
        public List<CommandDefinition> Commands { get; } = [];

        public void Add(CommandDefinition command, Func<object?, Task> execute, Func<object?, bool>? canExecute = null) => Commands.Add(command);

        public void AddMenuItem(MenuItemDefinition item)
        {
        }

        public void AddMenu(MenuDefinition menu)
        {
        }
    }
}
