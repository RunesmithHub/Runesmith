using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Runesmith.LanguageServices;

/// <summary>Measurements of the language services, readable with a <see cref="MeterListener"/> or <c>dotnet-counters</c>.</summary>
public static class LanguageServiceMetrics
{
    /// <summary>The name of the language services' meter.</summary>
    public const string MeterName = "Runesmith.LanguageServices";

    private static readonly Meter Meter = new(MeterName);

    /// <summary>From receiving a request to returning its answer, in milliseconds, tagged with the language and the request.</summary>
    public static readonly Histogram<double> Request = Meter.CreateHistogram<double>("runesmith.language.request", "ms");

    /// <summary>How long a request waited for its lane, in milliseconds, tagged with the language and the request.</summary>
    public static readonly Histogram<double> Queue = Meter.CreateHistogram<double>("runesmith.language.queue", "ms");

    /// <summary>Ranking and capping a completion list, in milliseconds, tagged with the language.</summary>
    public static readonly Histogram<double> Rank = Meter.CreateHistogram<double>("runesmith.language.rank", "ms");

    /// <summary>How many candidates an analyzer offered for a completion request, tagged with the language.</summary>
    public static readonly Histogram<int> Candidates = Meter.CreateHistogram<int>("runesmith.language.candidates");

    /// <summary>How many suggestions a completion list returned, tagged with the language.</summary>
    public static readonly Histogram<int> Items = Meter.CreateHistogram<int>("runesmith.language.items");

    internal static KeyValuePair<string, object?>[] Tags(string language, string request) =>
        [new("language", language), new("request", request)];

    internal static double Since(long timestamp) => Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds;
}
