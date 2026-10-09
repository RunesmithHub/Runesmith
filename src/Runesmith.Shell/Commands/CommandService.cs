using System.Composition;
using System.Text.Json;
using Avalonia.Input;
using Runesmith.Sdk;
using Runesmith.Sdk.Commands;
using Runesmith.Sdk.Shell;

namespace Runesmith.Shell.Commands;

/// <summary>Keeps every command, its key bindings and its menu items, and runs commands.</summary>
/// <remarks>The user's own bindings in <c>keybindings.json</c>, next to the settings, override the defaults: an object of command ids and
/// gestures, where an empty gesture removes a binding.</remarks>
[Export(typeof(ICommandService))]
[Export]
[Shared]
public sealed class CommandService : ICommandService, ICommandRegistry
{
    private static readonly JsonSerializerOptions ReadOptions = new() { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly IEnumerable<Lazy<ICommandContributor>> contributors;
    private readonly Lazy<INotificationService> notifications;
    private readonly Dictionary<string, Registered> commands = new(StringComparer.Ordinal);
    private readonly List<MenuItemDefinition> menuItems = [];
    private readonly List<MenuDefinition> menus = [];
    private readonly Dictionary<string, IReadOnlyList<KeyGesture>> bindings = new(StringComparer.Ordinal);
    private Dictionary<string, string> userBindings = new(StringComparer.Ordinal);

    [ImportingConstructor]
    public CommandService([ImportMany] IEnumerable<Lazy<ICommandContributor>> contributors, Lazy<INotificationService> notifications)
    {
        this.contributors = contributors;
        this.notifications = notifications;
    }

    public IReadOnlyList<CommandDefinition> Commands => [.. commands.Values.Select(c => c.Definition)];

    /// <summary>Gets the menu items, in no particular order.</summary>
    public IReadOnlyList<MenuItemDefinition> MenuItems => menuItems;

    /// <summary>Gets the top-level menus plugins added, in the order they were added.</summary>
    public IReadOnlyList<MenuDefinition> Menus => menus;

    /// <summary>Gets the problems met while collecting commands, such as a contributor that threw.</summary>
    public List<string> Errors { get; } = [];

    /// <summary>Gets the file of the user's key bindings.</summary>
    public static string UserBindingsPath => Path.Combine(RunesmithPaths.Config, "keybindings.json");

    public event EventHandler? Changed;

    /// <summary>Collects the commands of every contributor and reads the user's key bindings; called once, when the window opens.</summary>
    public void Initialize()
    {
        foreach (var contributor in contributors)
        {
            try
            {
                contributor.Value.Contribute(this);
            }
            catch (Exception exception)
            {
                Errors.Add($"Commands could not be added: {exception.Message}");
            }
        }

        LoadUserBindings();
    }

    public void Add(CommandDefinition command, Func<object?, Task> execute, Func<object?, bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(execute);
        commands[command.Id] = new Registered(command, execute, canExecute);
        RebuildBinding(command.Id);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void AddMenuItem(MenuItemDefinition item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (menuItems.Exists(i => i.Menu == item.Menu && i.CommandId == item.CommandId))
            return;

        menuItems.Add(item);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void AddMenu(MenuDefinition menu)
    {
        ArgumentNullException.ThrowIfNull(menu);
        if (string.IsNullOrWhiteSpace(menu.Id) || menu.Id.Contains('/', StringComparison.Ordinal) || Runesmith.Sdk.Commands.Menus.All.Contains(menu.Id) || ContextMenus.IsContextMenu(menu.Id))
        {
            Errors.Add($"The menu {menu.Title} could not be added: its id \"{menu.Id}\" is empty, has a /, or is one of Runesmith's own menus.");
            return;
        }

        if (menus.Exists(m => m.Id == menu.Id))
            return;

        menus.Add(menu);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public CommandDefinition? Find(string commandId) => commands.GetValueOrDefault(commandId)?.Definition;

    public bool CanExecute(string commandId, object? argument = null)
    {
        if (!commands.TryGetValue(commandId, out var command))
            return false;

        try
        {
            return command.CanExecute?.Invoke(argument) ?? true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<bool> ExecuteAsync(string commandId, object? argument = null)
    {
        if (!CanExecute(commandId, argument))
            return false;

        try
        {
            await commands[commandId].Execute(argument);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception exception)
        {
            notifications.Value.Notify(NotificationKind.Error, $"{commands[commandId].Definition.Title} failed", exception.Message);
            return false;
        }
    }

    public string? GetKeyBinding(string commandId) =>
        bindings.TryGetValue(commandId, out var gestures) && gestures.Count > 0 ? KeyBindings.Format(gestures) : null;

    /// <summary>Gets the gestures a command is bound to.</summary>
    public IReadOnlyList<KeyGesture> GetGestures(string commandId) => bindings.GetValueOrDefault(commandId) ?? [];

    /// <summary>Finds the command a key press is bound to.</summary>
    public string? FindByGesture(KeyEventArgs e)
    {
        foreach (var (id, gestures) in bindings)
        {
            if (gestures.Any(g => g.Matches(e)))
                return id;
        }

        return null;
    }

    /// <summary>Binds a command to other keys, or to none with an empty text, and saves the user's bindings.</summary>
    public void SetUserBinding(string commandId, string? gestures)
    {
        if (gestures is null)
            userBindings.Remove(commandId);
        else
            userBindings[commandId] = gestures;

        RebuildBinding(commandId);
        SaveUserBindings();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void RebuildBinding(string commandId)
    {
        bindings[commandId] = userBindings.TryGetValue(commandId, out var user)
            ? KeyBindings.Parse(user)
            : KeyBindings.Parse(commands.GetValueOrDefault(commandId)?.Definition.KeyBinding, isDefault: true);
    }

    private void LoadUserBindings()
    {
        try
        {
            if (File.Exists(UserBindingsPath))
            {
                using var stream = File.OpenRead(UserBindingsPath);
                userBindings = new Dictionary<string, string>(JsonSerializer.Deserialize<Dictionary<string, string>>(stream, ReadOptions) ?? [], StringComparer.Ordinal);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            Errors.Add($"{UserBindingsPath} could not be read: {exception.Message}");
        }

        foreach (var id in commands.Keys)
            RebuildBinding(id);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void SaveUserBindings()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(UserBindingsPath)!);
        var json = JsonSerializer.Serialize(new SortedDictionary<string, string>(userBindings, StringComparer.Ordinal), WriteOptions);
        var temporary = UserBindingsPath + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, UserBindingsPath, overwrite: true);
    }

    private sealed record Registered(CommandDefinition Definition, Func<object?, Task> Execute, Func<object?, bool>? CanExecute);
}
