using System.Composition;
using Runesmith.Sdk.Commands;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Languages;
using Runesmith.Text;

namespace Runesmith.Shell.Debugging;

/// <summary>Draws breakpoints in the gutter and marks the line the paused program is on.</summary>
[Export(typeof(IDecorationProvider))]
[Languages(LanguagesAttribute.Any)]
[Shared]
public sealed class DebugDecorations : IDecorationProvider
{
    private readonly BreakpointService breakpoints;
    private readonly DebugService debug;
    private (string? Path, int Line) shown;

    [ImportingConstructor]
    public DebugDecorations(BreakpointService breakpoints, DebugService debug)
    {
        DebugIcons.Register();
        this.breakpoints = breakpoints;
        this.debug = debug;
        breakpoints.Changed += (_, path) => Changed?.Invoke(this, new DecorationsChangedEventArgs(path));
        breakpoints.StatusesChanged += (_, _) => Changed?.Invoke(this, new DecorationsChangedEventArgs(null));
        debug.Changed += (_, _) => OnDebugChanged();
    }

    public event EventHandler<DecorationsChangedEventArgs>? Changed;

    public Task<IReadOnlyList<Decoration>> GetDecorationsAsync(DecorationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Document.FilePath is not { } path)
            return Task.FromResult<IReadOnlyList<Decoration>>([]);

        return Task.FromResult(Decorate(path, request.Snapshot, breakpoints, Location(debug.Current?.CurrentFrame)));
    }

    /// <summary>Gets the decorations of a file: a marker per breakpoint, and the paused line's highlight and marker.</summary>
    /// <param name="paused">The file and line, from 0, the program is paused on, if any.</param>
    internal static IReadOnlyList<Decoration> Decorate(string path, TextSnapshot snapshot, BreakpointService breakpoints, (string? Path, int Line) paused)
    {
        var decorations = new List<Decoration>();
        var pausedLine = PathKey.Equals(paused.Path, path) && paused.Line >= 0 && paused.Line < snapshot.LineCount ? paused.Line : -1;
        foreach (var breakpoint in breakpoints.In(path))
        {
            if (breakpoint.Line >= snapshot.LineCount || breakpoint.Line == pausedLine)
                continue;

            var line = snapshot.GetLine(breakpoint.Line);
            var status = breakpoints.StatusOf(breakpoint);
            var unverified = breakpoint.IsEnabled && status is { Verified: false };
            decorations.Add(new GutterMarker(new TextSpan(line.Start, 0), breakpoint.IsLogPoint ? DebugIcons.LogPoint : DebugIcons.Breakpoint)
            {
                Tone = !breakpoint.IsEnabled ? DecorationTone.Neutral : breakpoint.IsLogPoint ? DecorationTone.Information : breakpoint.IsConditional ? DecorationTone.Warning : DecorationTone.Error,
                IsFilled = breakpoint.IsEnabled && !unverified,
                ToolTip = Describe(breakpoint, status),
                CommandId = CommandIds.ToggleBreakpoint,
                CommandArgument = new ContextMenuTarget(path) { Lines = (breakpoint.Line + 1, breakpoint.Line + 1) },
                ShowsInOverviewRuler = breakpoint.IsEnabled,
            });
        }

        if (pausedLine >= 0)
        {
            var line = snapshot.GetLine(pausedLine);
            var text = snapshot.GetLineText(pausedLine);
            var indent = text.Length - text.TrimStart().Length;
            var here = breakpoints.Find(path, pausedLine);
            decorations.Add(new GutterMarker(new TextSpan(line.Start, 0), DebugIcons.CurrentLine)
            {
                Tone = DecorationTone.Warning,
                IsFilled = true,
                ToolTip = here is null ? "The program is paused here." : "The program is paused here, at a breakpoint.",
                CommandId = CommandIds.ToggleBreakpoint,
                CommandArgument = new ContextMenuTarget(path) { Lines = (pausedLine + 1, pausedLine + 1) },
                ShowsInOverviewRuler = true,
            });
            decorations.Add(new TextHighlight(TextSpan.FromBounds(line.Start + indent, line.End))
            {
                Background = true,
                Underline = UnderlineStyle.None,
                Tone = DecorationTone.Warning,
            });
        }

        return decorations;
    }

    private static string Describe(LineBreakpoint breakpoint, BreakpointStatus? status)
    {
        var lines = new List<string> { breakpoint.IsLogPoint ? "**Log point**" : "**Breakpoint**" };
        if (!breakpoint.IsEnabled)
            lines.Add("Disabled.");
        if (!string.IsNullOrEmpty(breakpoint.Condition))
            lines.Add($"Stops when `{breakpoint.Condition}`.");
        if (!string.IsNullOrEmpty(breakpoint.HitCondition))
            lines.Add($"Hit count: `{breakpoint.HitCondition}`.");
        if (breakpoint.IsLogPoint)
            lines.Add($"Logs `{breakpoint.LogMessage}`.");
        if (status is { Verified: false })
            lines.Add(status.Message ?? "The debugger has not set it yet, such as before its code loads.");
        else if (status?.Message is { Length: > 0 } message)
            lines.Add(message);
        lines.Add("Click to remove it.");
        return string.Join("\n\n", lines);
    }

    private static (string? Path, int Line) Location(Dap.Protocol.StackFrame? frame) => (frame?.Source?.Path, (frame?.Line ?? 0) - 1);

    // Asks again only for the files the paused line left and entered, as sessions change state on every step.
    private void OnDebugChanged()
    {
        var now = Location(debug.Current?.CurrentFrame);
        var before = shown;
        if (now == before)
            return;

        shown = now;
        if (before.Path is not null)
            Changed?.Invoke(this, new DecorationsChangedEventArgs(before.Path));
        if (now.Path is not null && !PathKey.Equals(now.Path, before.Path))
            Changed?.Invoke(this, new DecorationsChangedEventArgs(now.Path));
    }
}
