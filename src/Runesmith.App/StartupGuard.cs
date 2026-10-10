using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Runesmith.App;

/// <summary>Remembers how the last starts went, so that after two starts in a row that failed, or crashed within a minute of the window
/// showing, the next start can offer safe mode and name the plugins that were loading.</summary>
internal sealed class StartupGuard
{
    /// <summary>How long Runesmith must run after its window shows for a start to count as good.</summary>
    public static readonly TimeSpan StableAfter = TimeSpan.FromMinutes(1);

    private const int Kept = 5;

    private readonly string path;
    private readonly TimeProvider time;
    private readonly Lock gate = new();
    private readonly List<StartRecord> history;

    private StartupGuard(string path, TimeProvider time, List<StartRecord> history, SafeModeOffer? offer)
    {
        this.path = path;
        this.time = time;
        this.history = history;
        Offer = offer;
    }

    /// <summary>Gets the safe mode offer for this start, or null when the last starts went well.</summary>
    public SafeModeOffer? Offer { get; }

    private StartRecord Current => history[^1];

    /// <summary>Records that Runesmith is starting and decides whether to offer safe mode.</summary>
    /// <param name="safeMode">Whether this start is in safe mode already.</param>
    public static StartupGuard Begin(string path, TimeProvider time, bool safeMode)
    {
        var history = Load(path);
        var offer = safeMode ? null : OfferFrom(history);
        history.Add(new StartRecord { StartedAt = time.GetUtcNow(), SafeMode = safeMode || offer is not null });
        if (history.Count > Kept)
            history.RemoveRange(0, history.Count - Kept);

        var guard = new StartupGuard(path, time, history, offer);
        guard.Save();
        return guard;
    }

    /// <summary>Records the plugins that were loading.</summary>
    public void Loaded(IEnumerable<SuspectPlugin> plugins) => Change(record => record with { Plugins = [.. plugins] });

    public void Shown() => Change(record => record with { ShownAt = time.GetUtcNow() });

    /// <summary>Records that Runesmith ran long enough after its window showed.</summary>
    public void Stable() => Change(record => record with { StableAt = time.GetUtcNow() });

    public void Exited() => Change(record => record with { ExitedAt = time.GetUtcNow() });

    /// <summary>Records a crash, and the plugins whose code is on its stack traces.</summary>
    /// <param name="pluginOf">Tells which plugin an assembly belongs to, or null for Runesmith's own and .NET's.</param>
    public void Crashed(Exception exception, Func<Assembly, SuspectPlugin?> pluginOf)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var suspects = new List<SuspectPlugin>();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            foreach (var frame in new StackTrace(current, fNeedFileInfo: false).GetFrames())
            {
                if (frame.GetMethod()?.DeclaringType?.Assembly is { } assembly && pluginOf(assembly) is { } plugin && !suspects.Any(s => s.Id == plugin.Id))
                    suspects.Add(plugin);
            }
        }

        Change(record => record with { CrashedAt = time.GetUtcNow(), Suspects = [.. record.Suspects.Concat(suspects).DistinctBy(s => s.Id)] });
    }

    /// <summary>Offers safe mode when the two starts before this one both went badly: the window never showed, or Runesmith ended within a
    /// minute of it without being closed.</summary>
    internal static SafeModeOffer? OfferFrom(IReadOnlyList<StartRecord> history)
    {
        if (history.Count < 2 || !IsBad(history[^1]) || !IsBad(history[^2]))
            return null;

        var suspects = history[^1].Suspects.Concat(history[^2].Suspects).DistinctBy(s => s.Id).ToList();
        if (suspects.Count == 0)
            suspects = [.. history[^1].Plugins.Concat(history[^2].Plugins).DistinctBy(s => s.Id)];

        var neverShown = history[^1].ShownAt is null && history[^2].ShownAt is null;
        return new SafeModeOffer(neverShown ? "Runesmith failed to start twice in a row." : "Runesmith closed unexpectedly twice while starting.", suspects);
    }

    private static bool IsBad(StartRecord record) => !record.SafeMode && record.ExitedAt is null && record.StableAt is null;

    private void Change(Func<StartRecord, StartRecord> change)
    {
        lock (gate)
        {
            history[^1] = change(Current);
            Save();
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(history, StartupJson.Default.ListStartRecord));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static List<StartRecord> Load(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize(File.ReadAllBytes(path), StartupJson.Default.ListStartRecord) ?? [] : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }
}

/// <summary>One start of Runesmith and how far it got.</summary>
internal sealed record StartRecord
{
    public DateTimeOffset StartedAt { get; init; }

    public bool SafeMode { get; init; }

    public DateTimeOffset? ShownAt { get; init; }

    public DateTimeOffset? StableAt { get; init; }

    public DateTimeOffset? ExitedAt { get; init; }

    public DateTimeOffset? CrashedAt { get; init; }

    /// <summary>Gets the plugins that loaded.</summary>
    public IReadOnlyList<SuspectPlugin> Plugins { get; init; } = [];

    /// <summary>Gets the plugins whose code was on the stack when Runesmith crashed.</summary>
    public IReadOnlyList<SuspectPlugin> Suspects { get; init; } = [];
}

/// <summary>A plugin that may have stopped Runesmith from starting.</summary>
internal sealed record SuspectPlugin(string Id, string Name, string Version);

/// <summary>Why the start offers safe mode, and the plugins that were loading.</summary>
internal sealed record SafeModeOffer(string Reason, IReadOnlyList<SuspectPlugin> Plugins);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(List<StartRecord>))]
internal sealed partial class StartupJson : JsonSerializerContext;
