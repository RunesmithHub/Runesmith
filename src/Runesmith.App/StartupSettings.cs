using System.Text.Json;
using Runesmith.Sdk;
using Runesmith.Shell.Hub;
using Runesmith.Shell.Services;

namespace Runesmith.App;

/// <summary>Reads the few settings needed before the settings service exists, straight from the user's settings file.</summary>
internal static class StartupSettings
{
    /// <summary>Gets the ids of the plugins the user turned off.</summary>
    public static IReadOnlySet<string> DisabledPlugins() =>
        ReadString(ShellSettings.DisabledPlugins) is { } value
            ? value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>();

    /// <summary>Gets whether the window draws its own title bar, which on Linux has to be chosen before Avalonia starts.</summary>
    public static TitleBarMode TitleBar() => ShellSettings.ParseTitleBar(ReadString(ShellSettings.TitleBar));

    /// <summary>Gets whether the plugin hub is on, which decides whether hub plugins load.</summary>
    public static bool HubEnabled() => Read(HubSettings.Enabled) is not { ValueKind: JsonValueKind.False };

    /// <summary>Gets whether Runesmith registers itself for runesmith:// links.</summary>
    public static bool RegisterLinks() => Read(HubSettings.RegisterLinks) is not { ValueKind: JsonValueKind.False };

    private static string? ReadString(string key) => Read(key) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    private static JsonElement? Read(string key)
    {
        try
        {
            if (!File.Exists(RunesmithPaths.UserSettings))
                return null;

            using var document = JsonDocument.Parse(File.ReadAllText(RunesmithPaths.UserSettings), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty(key, out var value))
                return value.Clone();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
        }

        return null;
    }
}
