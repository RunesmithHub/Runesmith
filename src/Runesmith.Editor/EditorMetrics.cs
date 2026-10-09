using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Runesmith.Editor;

/// <summary>The editor's latency measurements, in milliseconds, readable with a <see cref="MeterListener"/> or <c>dotnet-counters</c>.</summary>
public static class EditorMetrics
{
    /// <summary>The name of the editor's meter.</summary>
    public const string MeterName = "Runesmith.Editor";

    private static readonly Meter Meter = new(MeterName);

    /// <summary>From a key press to the end of drawing the text it changed.</summary>
    public static readonly Histogram<double> InputToRender = Meter.CreateHistogram<double>("runesmith.editor.input_to_render", "ms");

    /// <summary>From a key press to the start of the next frame after the text was drawn.</summary>
    public static readonly Histogram<double> InputToFrame = Meter.CreateHistogram<double>("runesmith.editor.input_to_frame", "ms");

    /// <summary>Applying an edit to the text, including everything that listens to the change.</summary>
    public static readonly Histogram<double> Edit = Meter.CreateHistogram<double>("runesmith.editor.edit", "ms");

    /// <summary>Drawing the visible text.</summary>
    public static readonly Histogram<double> Render = Meter.CreateHistogram<double>("runesmith.editor.render", "ms");

    /// <summary>From asking for completions to showing the first list.</summary>
    public static readonly Histogram<double> CompletionShown = Meter.CreateHistogram<double>("runesmith.completion.shown", "ms");

    /// <summary>Filtering and sorting the list for the typed word.</summary>
    public static readonly Histogram<double> CompletionFilter = Meter.CreateHistogram<double>("runesmith.completion.filter", "ms");

    /// <summary>Showing a filtered list in the popup.</summary>
    public static readonly Histogram<double> CompletionPopupUpdate = Meter.CreateHistogram<double>("runesmith.completion.popup_update", "ms");

    /// <summary>The number of suggestions a request returned.</summary>
    public static readonly Histogram<int> CompletionItems = Meter.CreateHistogram<int>("runesmith.completion.items");

    /// <summary>Gets milliseconds since a <see cref="Stopwatch.GetTimestamp"/> value.</summary>
    public static double Since(long timestamp) => Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds;
}
