using System.Text.Json;
using System.Text.Json.Serialization;
using Runesmith.Sdk;
using Runesmith.Sdk.Options;
using Runesmith.Sdk.Running;

namespace Runesmith.Shell.Running;

/// <summary>What a folder remembers about running for this user: its local configurations, the selected one and the recently run ones.</summary>
/// <param name="Configurations">The configurations saved only for this user.</param>
/// <param name="Selected">The name of the selected configuration, or null.</param>
/// <param name="Recent">The names of the configurations run last, most recent first.</param>
internal sealed record RunLocalState(IReadOnlyList<RunConfiguration> Configurations, string? Selected, IReadOnlyList<string> Recent)
{
    public static RunLocalState Empty { get; } = new([], null, []);
}

/// <summary>Reads and writes run configurations: the shared ones in the folder's <c>.runesmith/run.json</c> and the local ones, with the
/// selection, in Runesmith's state folder. A missing or damaged file reads as empty.</summary>
internal static class RunConfigurationFile
{
    /// <summary>Gets the shared configurations file of a folder.</summary>
    public static string SharedPath(string rootPath) => Path.Combine(rootPath, RunesmithPaths.WorkspaceFolderName, "run.json");

    /// <summary>Gets the file of a folder's local state, in a state folder.</summary>
    public static string LocalPath(string stateFolder, string rootPath)
    {
        var key = OperatingSystem.IsLinux() ? rootPath : rootPath.ToUpperInvariant();
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)))[..16];
        return Path.Combine(stateFolder, "run", hash + ".json");
    }

    public static IReadOnlyList<RunConfiguration> ReadShared(string rootPath) =>
        Read(SharedPath(rootPath), RunJson.Default.RunFileModel) is { } model ? ToConfigurations(model.Configurations, isLocal: false) : [];

    /// <summary>Writes the shared configurations; with none, removes the file instead of leaving an empty one.</summary>
    public static void WriteShared(string rootPath, IReadOnlyList<RunConfiguration> configurations)
    {
        var path = SharedPath(rootPath);
        if (configurations.Count == 0)
        {
            if (File.Exists(path))
                File.Delete(path);
            return;
        }

        Write(path, new RunFileModel(ToModels(configurations)), RunJson.Default.RunFileModel);
    }

    public static RunLocalState ReadLocal(string path) =>
        Read(path, RunJson.Default.RunStateModel) is { } model
            ? new RunLocalState(ToConfigurations(model.Configurations ?? [], isLocal: true), model.Selected, model.Recent ?? [])
            : RunLocalState.Empty;

    public static void WriteLocal(string path, RunLocalState state) =>
        Write(path, new RunStateModel(ToModels(state.Configurations), state.Selected, state.Recent), RunJson.Default.RunStateModel);

    private static List<RunConfigurationModel> ToModels(IEnumerable<RunConfiguration> configurations) =>
    [
        .. configurations.Select(c => new RunConfigurationModel(
            c.TypeId,
            c.Name,
            new SortedDictionary<string, string>(c.Values.All.ToDictionary(), StringComparer.Ordinal),
            [.. c.BeforeLaunch.Select(step => new BeforeLaunchModel(KindName(step.Kind), step.Argument))])),
    ];

    private static List<RunConfiguration> ToConfigurations(IEnumerable<RunConfigurationModel?> models, bool isLocal)
    {
        var configurations = new List<RunConfiguration>();
        foreach (var model in models)
        {
            if (model is null || string.IsNullOrWhiteSpace(model.Type) || string.IsNullOrWhiteSpace(model.Name))
                continue;

            var values = new OptionValues(model.Values ?? new SortedDictionary<string, string>());
            var configuration = new RunConfiguration(model.Type, model.Name, values) { IsLocal = isLocal };
            if (model.BeforeLaunch is { } steps)
                configuration = configuration with { BeforeLaunch = [.. steps.Where(s => s is not null).Select(s => new BeforeLaunchStep(ParseKind(s.Kind), s.Argument))] };
            configurations.Add(configuration);
        }

        return configurations;
    }

    private static string KindName(BeforeLaunchKind kind) => kind switch
    {
        BeforeLaunchKind.RunConfiguration => "runConfiguration",
        BeforeLaunchKind.Command => "command",
        _ => "build",
    };

    private static BeforeLaunchKind ParseKind(string? name) => name switch
    {
        "runConfiguration" => BeforeLaunchKind.RunConfiguration,
        "command" => BeforeLaunchKind.Command,
        _ => BeforeLaunchKind.Build,
    };

    private static T? Read<T>(string path, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type)
        where T : class
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize(File.ReadAllText(path), type) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static void Write<T>(string path, T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, type) + Environment.NewLine);
        File.Move(temporary, path, overwrite: true);
    }
}

internal sealed record RunFileModel(IReadOnlyList<RunConfigurationModel?> Configurations);

internal sealed record RunStateModel(IReadOnlyList<RunConfigurationModel?>? Configurations, string? Selected, IReadOnlyList<string>? Recent);

internal sealed record RunConfigurationModel(string Type, string Name, SortedDictionary<string, string>? Values, IReadOnlyList<BeforeLaunchModel>? BeforeLaunch);

internal sealed record BeforeLaunchModel(string Kind, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Argument);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[JsonSerializable(typeof(RunFileModel))]
[JsonSerializable(typeof(RunStateModel))]
internal sealed partial class RunJson : JsonSerializerContext;
