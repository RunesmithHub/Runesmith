using Runesmith.Sdk.Running;

namespace Runesmith.Shell.Running;

/// <summary>Combines detected configurations with saved ones: a saved configuration with the same type and name as a detected one replaces
/// it in its place, and the other saved ones follow.</summary>
internal static class RunConfigurationMerger
{
    public static IReadOnlyList<RunConfiguration> Merge(IReadOnlyList<RunConfiguration> detected, IReadOnlyList<RunConfiguration> saved)
    {
        var byKey = new Dictionary<(string, string), RunConfiguration>();
        foreach (var configuration in saved)
            byKey.TryAdd(Key(configuration), configuration);

        var result = new List<RunConfiguration>();
        var used = new HashSet<(string, string)>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var configuration in detected)
        {
            var key = Key(configuration);
            if (!used.Add(key))
                continue;

            var chosen = byKey.TryGetValue(key, out var replacement) ? replacement : configuration with { IsDetected = true };
            if (names.Add(chosen.Name))
                result.Add(chosen);
        }

        foreach (var configuration in saved)
        {
            if (used.Add(Key(configuration)) && names.Add(configuration.Name))
                result.Add(configuration with { IsDetected = false });
        }

        return result;
    }

    private static (string, string) Key(RunConfiguration configuration) => (configuration.TypeId, configuration.Name);
}
