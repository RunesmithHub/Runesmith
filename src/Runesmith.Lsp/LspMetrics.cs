using System.Diagnostics.Metrics;

namespace Runesmith.Lsp;

/// <summary>Measurements of the conversation with a language server, readable with a <see cref="MeterListener"/> or <c>dotnet-counters</c>.</summary>
public static class LspMetrics
{
    /// <summary>The name of the client's meter.</summary>
    public const string MeterName = "Runesmith.Lsp";

    private static readonly Meter Meter = new(MeterName);

    /// <summary>From sending a request to receiving its answer, in milliseconds, tagged with the method.</summary>
    public static readonly Histogram<double> Request = Meter.CreateHistogram<double>("runesmith.lsp.request", "ms");

    /// <summary>The size of an answer, in bytes, tagged with the method.</summary>
    public static readonly Histogram<int> ResponseBytes = Meter.CreateHistogram<int>("runesmith.lsp.response_bytes", "By");
}
