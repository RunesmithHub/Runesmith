using System.Composition;
using Runesmith.Sdk.Commands;
using Runesmith.Sdk.Shell;

namespace MyPlugin;

/// <summary>Adds the plugin's commands: they show in the command palette (Ctrl+K) and in the Help menu.</summary>
[Export(typeof(ICommandContributor))]
[method: ImportingConstructor]
public sealed class MyPluginCommands(INotificationService notifications) : ICommandContributor
{
    public const string SayHelloId = "myplugin.sayHello";

    public void Contribute(ICommandRegistry registry)
    {
        var sayHello = new CommandDefinition(SayHelloId, Strings.SayHelloTitle, "MyPlugin")
        {
            Icon = "sparkles",
            Description = Strings.SayHelloDescription,
        };
        registry.Add(sayHello, _ =>
        {
            notifications.Notify(NotificationKind.Success, Strings.HelloTitle, Strings.HelloMessage);
            return Task.CompletedTask;
        });
        registry.AddMenuItem(new MenuItemDefinition(Menus.Help, SayHelloId, Group: "plugins"));
    }
}
