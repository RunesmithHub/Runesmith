using System.Composition;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Runesmith.Composition;
using Runesmith.Sdk;
using Runesmith.Sdk.Messaging;
using Runesmith.Sdk.Settings;
using Runesmith.Workspace.Files;

namespace Runesmith.Workspace.Settings;

/// <summary>Settings from the defaults, the user's <c>settings.json</c> and the open folder's <c>.runesmith/settings.json</c>, in that order of
/// strength.</summary>
/// <remarks>Both files are read again when they change on disk. A value of the wrong type, or outside a setting's choices, is ignored. A plugin
/// changes only its own settings: those whose keys start with its id and a dot, and those it contributes.</remarks>
[Export(typeof(ISettingsService))]
[Export(typeof(SettingsService))]
[Shared]
public sealed class SettingsService : ISettingsService, IDisposable
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, SettingDefinition> _definitions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _owners = new(StringComparer.Ordinal);
    private readonly Func<PluginInfo?> _caller;
    private readonly SettingsFile _user;
    private readonly FileSystemWatcher? _userWatcher;
    private readonly Timer _reloadTimer;
    private readonly IDisposable _workspaceSubscription;
    private readonly IDisposable _filesSubscription;
    private SettingsFile? _workspace;
    private bool _reloadUser;
    private bool _reloadWorkspace;

    [ImportingConstructor]
    public SettingsService([ImportMany] IEnumerable<ISettingContributor> contributors, IMessageBus messageBus)
        : this(contributors, messageBus, RunesmithPaths.UserSettings)
    {
    }

    internal SettingsService(IEnumerable<ISettingContributor> contributors, IMessageBus messageBus, string userSettingsPath, Func<PluginInfo?>? caller = null,
        Func<ISettingContributor, PluginInfo?>? ownerOf = null)
    {
        _caller = caller ?? PluginCallers.Current;
        ownerOf ??= contributor => PluginCallers.Of(contributor.GetType().Assembly);
        foreach (var contributor in contributors)
        {
            var owner = ownerOf(contributor)?.Manifest.Id;
            foreach (var definition in contributor.Settings)
            {
                if (_definitions.TryAdd(definition.Key, definition) && owner is not null)
                    _owners[definition.Key] = owner;
            }
        }

        Definitions = [.. _definitions.Values];

        _user = new SettingsFile(userSettingsPath);
        _user.Load();
        _reloadTimer = new Timer(_ => ReloadPending());
        _userWatcher = Watch(userSettingsPath);
        _workspaceSubscription = messageBus.Subscribe<WorkspaceOpenedMessage>(message => OpenWorkspace(message.RootPath));
        _filesSubscription = messageBus.Subscribe<FilesChangedMessage>(OnFilesChanged);
    }

    public IReadOnlyList<SettingDefinition> Definitions { get; }

    public event EventHandler<SettingChangedEventArgs>? Changed;

    /// <summary>Raised, on the UI thread, when a settings file cannot be read; its argument says which file and why.</summary>
    public event EventHandler<string>? LoadFailed;

    /// <summary>Gets why a scope's file could not be read, or null.</summary>
    public string? GetLoadError(SettingScope scope)
    {
        lock (_gate)
            return File(scope)?.Error;
    }

    public T Get<T>(string key)
    {
        var value = GetEffective(Definition(key));
        if (value is T typed)
            return typed;
        if (typeof(T) == typeof(string))
            return (T)(object)Convert.ToString(value, CultureInfo.InvariantCulture)!;
        return (T)Convert.ChangeType(value, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T), CultureInfo.InvariantCulture);
    }

    public object? GetValue(string key, SettingScope scope)
    {
        var definition = Definition(key);
        if (scope == SettingScope.Default)
            return definition.DefaultValue;

        lock (_gate)
            return File(scope) is { } file && file.Values.TryGetPropertyValue(key, out var node) ? ToValue(node, definition) : null;
    }

    public SettingScope GetEffectiveScope(string key)
    {
        var definition = Definition(key);
        lock (_gate)
        {
            foreach (var scope in (ReadOnlySpan<SettingScope>)[SettingScope.Workspace, SettingScope.User])
            {
                if (File(scope) is { } file && file.Values.TryGetPropertyValue(key, out var node) && ToValue(node, definition) is not null)
                    return scope;
            }
        }

        return SettingScope.Default;
    }

    public void Set(string key, object value, SettingScope scope = SettingScope.User)
    {
        ArgumentNullException.ThrowIfNull(value);
        CheckOwner(key);
        var definition = Definition(key);
        var node = ToNode(value, definition) ?? throw new ArgumentException($"{value} is not a valid value for {key}.", nameof(value));
        Change(key, scope, file => file.Values[key] = node);
    }

    public void Reset(string key, SettingScope scope = SettingScope.User)
    {
        CheckOwner(key);
        Definition(key);
        Change(key, scope, file => file.Values.Remove(key));
    }

    public void Dispose()
    {
        _userWatcher?.Dispose();
        _reloadTimer.Dispose();
        _workspaceSubscription.Dispose();
        _filesSubscription.Dispose();
    }

    private void CheckOwner(string key)
    {
        if (_caller() is not { } plugin || key.StartsWith(plugin.Manifest.Id + ".", StringComparison.Ordinal)
            || (_owners.TryGetValue(key, out var owner) && owner == plugin.Manifest.Id))
            return;

        throw new UnauthorizedAccessException($"{plugin.Manifest.Name} ({plugin.Manifest.Id}) cannot change the setting {key}: a plugin changes only the settings it contributes and those whose keys start with its id.");
    }

    private void Change(string key, SettingScope scope, Action<SettingsFile> change)
    {
        if (scope == SettingScope.Default)
            throw new InvalidOperationException("Default values cannot be changed.");

        bool changed;
        lock (_gate)
        {
            var file = File(scope) ?? throw new InvalidOperationException("Workspace settings need an open folder.");
            var before = GetEffective(Definition(key));
            var backup = file.Values.DeepClone().AsObject();
            change(file);
            try
            {
                file.Save();
            }
            catch
            {
                RestoreValues(file, backup);
                throw;
            }

            changed = !Equals(before, GetEffective(Definition(key)));
        }

        if (changed)
            UiThread.Run(() => Changed?.Invoke(this, new SettingChangedEventArgs(key)));
    }

    private static void RestoreValues(SettingsFile file, JsonObject values)
    {
        file.Values.Clear();
        foreach (var (key, value) in values)
            file.Values[key] = value?.DeepClone();
    }

    private SettingsFile? File(SettingScope scope) => scope switch
    {
        SettingScope.User => _user,
        SettingScope.Workspace => _workspace,
        _ => null,
    };

    private SettingDefinition Definition(string key) =>
        _definitions.TryGetValue(key, out var definition) ? definition : throw new KeyNotFoundException($"There is no setting {key}.");

    private object GetEffective(SettingDefinition definition)
    {
        lock (_gate)
        {
            foreach (var file in (ReadOnlySpan<SettingsFile?>)[_workspace, _user])
            {
                if (file is not null && file.Values.TryGetPropertyValue(definition.Key, out var node) && ToValue(node, definition) is { } value)
                    return value;
            }
        }

        return definition.DefaultValue;
    }

    private void OpenWorkspace(string? rootPath) =>
        ReloadAndNotify(() => _workspace = rootPath is null ? null : Load(new SettingsFile(RunesmithPaths.WorkspaceSettings(rootPath))));

    private void OnFilesChanged(FilesChangedMessage message)
    {
        var workspace = _workspace;
        if (workspace is null)
            return;

        var folder = Path.GetDirectoryName(workspace.Path)!;
        if (message.Paths.Any(path => PathComparison.IsInside(workspace.Path, PathComparison.Normalize(path)) || PathComparison.IsInside(path, folder)))
            Schedule(workspace: true);
    }

    private FileSystemWatcher? Watch(string path)
    {
        try
        {
            var directory = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(directory);
            var watcher = new FileSystemWatcher(directory, Path.GetFileName(path))
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            watcher.Changed += (_, _) => Schedule(workspace: false);
            watcher.Created += (_, _) => Schedule(workspace: false);
            watcher.Deleted += (_, _) => Schedule(workspace: false);
            watcher.Renamed += (_, _) => Schedule(workspace: false);
            watcher.EnableRaisingEvents = true;
            return watcher;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private void Schedule(bool workspace)
    {
        lock (_gate)
        {
            if (workspace)
                _reloadWorkspace = true;
            else
                _reloadUser = true;
        }

        _reloadTimer.Change(TimeSpan.FromMilliseconds(200), Timeout.InfiniteTimeSpan);
    }

    private void ReloadPending() => ReloadAndNotify(() =>
    {
        if (_reloadUser)
            Load(_user);
        if (_reloadWorkspace && _workspace is not null)
            Load(_workspace);
        _reloadUser = _reloadWorkspace = false;
    });

    private SettingsFile Load(SettingsFile file)
    {
        file.Load();
        if (file.Error is { } error)
            UiThread.Run(() => LoadFailed?.Invoke(this, $"{file.Path}: {error}"));
        return file;
    }

    private void ReloadAndNotify(Action reload)
    {
        List<string> changed;
        lock (_gate)
        {
            var before = Definitions.ToDictionary(d => d.Key, GetEffective);
            reload();
            changed = [.. Definitions.Where(d => !Equals(before[d.Key], GetEffective(d))).Select(d => d.Key)];
        }

        if (changed.Count > 0)
        {
            UiThread.Run(() =>
            {
                foreach (var key in changed)
                    Changed?.Invoke(this, new SettingChangedEventArgs(key));
            });
        }
    }

    private static object? ToValue(JsonNode? node, SettingDefinition definition)
    {
        if (node is not JsonValue value)
            return null;

        var type = definition.ValueType;
        try
        {
            if (type == typeof(bool))
                return value.GetValueKind() is JsonValueKind.True or JsonValueKind.False ? value.GetValue<bool>() : null;

            if (type == typeof(int) || type == typeof(double))
            {
                if (value.GetValueKind() != JsonValueKind.Number || Number(value) is not { } read)
                    return null;
                var number = Clamp(read, definition);
                return type == typeof(int) ? (int)Math.Round(number) : (object)number;
            }

            if (value.GetValueKind() != JsonValueKind.String)
                return null;

            var text = value.GetValue<string>();
            if (type.IsEnum)
                return Enum.TryParse(type, text, ignoreCase: true, out var member) ? member : null;
            if (definition.Choices is { } choices)
                return choices.FirstOrDefault(choice => string.Equals(choice, text, StringComparison.OrdinalIgnoreCase));
            return text;
        }
        catch (Exception exception) when (exception is FormatException or InvalidOperationException)
        {
            return null;
        }
    }

    private static JsonValue? ToNode(object value, SettingDefinition definition)
    {
        var type = definition.ValueType;
        if (type == typeof(bool))
            return value is bool flag ? JsonValue.Create(flag) : null;

        if (type == typeof(int) || type == typeof(double))
        {
            if (value is not (int or long or float or double or decimal))
                return null;
            var number = Clamp(Convert.ToDouble(value, CultureInfo.InvariantCulture), definition);
            return type == typeof(int) ? JsonValue.Create((int)Math.Round(number)) : JsonValue.Create(number);
        }

        var text = value is Enum ? value.ToString() : value as string;
        if (text is null)
            return null;
        if (type.IsEnum)
            return Enum.TryParse(type, text, ignoreCase: true, out var member) ? JsonValue.Create(member!.ToString()) : null;
        if (definition.Choices is { } choices)
            return choices.FirstOrDefault(choice => string.Equals(choice, text, StringComparison.OrdinalIgnoreCase)) is { } choice ? JsonValue.Create(choice) : null;
        return JsonValue.Create(text);
    }

    // A value Set stored holds the int or double it was given and converts to no other number type, unlike one read from the file.
    private static double? Number(JsonValue value) =>
        value.TryGetValue<double>(out var real) ? real : value.TryGetValue<int>(out var integer) ? integer : value.TryGetValue<long>(out var wide) ? wide : null;

    private static double Clamp(double number, SettingDefinition definition) =>
        Math.Clamp(number, definition.Minimum ?? double.MinValue, definition.Maximum ?? double.MaxValue);
}
