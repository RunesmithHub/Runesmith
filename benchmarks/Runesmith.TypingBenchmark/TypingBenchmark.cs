using System.Composition;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Runesmith.Sdk.Commands;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Plugins;
using Runesmith.Text;

namespace Runesmith.TypingBenchmark;

/// <summary>Opens a generated file of 10,000 lines in the benchmark's own sample language, measures the memory it takes, types sixteen lines into a method in its middle at about
/// 140 words a minute, scrolls through it with the mouse wheel, writes what the editor measured meanwhile to the folder
/// <c>RUNESMITH_TYPING_BENCHMARK</c> names, and exits. It needs no language plugin: <see cref="SampleLanguage"/> brings the language and its grammar.</summary>
/// <remarks><c>RUNESMITH_TYPING_BENCHMARK_FILE</c> names the file, which is written when it does not exist. Before typing, a method of every
/// tenth class and the body of one class are folded, so lines are hidden, and <see cref="SampleLanguage"/> gives the file semantic tokens
/// and occurrence highlights; while scrolling, the occurrences of a name used 1,000 times are highlighted. Nothing is saved.</remarks>
[Export(typeof(IPlugin))]
[method: ImportingConstructor]
public sealed class TypingBenchmark(IEditorService editors, ICommandService commands) : IPlugin
{
    private const string OutputVariable = "RUNESMITH_TYPING_BENCHMARK";
    private const string FileVariable = "RUNESMITH_TYPING_BENCHMARK_FILE";

    // By its id rather than CommandIds.Fold, so the benchmark also builds against plugin APIs from before folding, which fold nothing.
    private const string FoldCommand = "editor.fold";

    private const string FrameInterval = "benchmark.scroll.frame_interval (ms)";
    private const int ScrollFrames = 400;
    private const int WheelNotchesPerFrame = 2;

    private static readonly TimeSpan KeyInterval = TimeSpan.FromMilliseconds(70);
    private static readonly TimeSpan WindowTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan OpenSettle = TimeSpan.FromSeconds(6);
    private static readonly Pointer Wheel = new(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);

    private static readonly string[] Lines =
    [
        "var probe = Snapshot.GetLineFromPosition(selection.Caret);",
        "if (probe.Length > 0 && Document.IsModified)",
        "Select(EditorSelection.At(probe.Start), scrollIntoView: false);",
        "var text = Snapshot.GetText(probe.Span).Trim();",
        "var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);",
        "var longest = words.OrderByDescending(word => word.Length).FirstOrDefault();",
        "var builder = new System.Text.StringBuilder(longest);",
        "builder.Append(value.Caret.ToString(System.Globalization.CultureInfo.InvariantCulture));",
        "foreach (var word in words.Where(word => word.StartsWith('a')))",
        "Console.WriteLine(word.ToUpperInvariant());",
        "var count = words.Count(word => char.IsUpper(word[0]));",
        "var lookup = words.ToDictionary(word => word, word => word.Length);",
        "if (lookup.TryGetValue(text, out var length) && length > count)",
        "ScrollToCaret();",
        "var number = Snapshot.GetLineFromPosition(value.Caret).LineNumber;",
        "System.Diagnostics.Debug.Assert(number >= 0, builder.ToString());",
    ];

    private readonly MetricRecorder recorder = new();

    /// <summary>Gets whether a document is the benchmark's sample file.</summary>
    internal static bool IsSample(IDocument document) => SamplePath is { } path && string.Equals(document.FilePath, path, StringComparison.Ordinal);

    private static string? SamplePath => Environment.GetEnvironmentVariable(FileVariable) is { Length: > 0 } path ? Path.GetFullPath(path) : null;

    // Runesmith opens the folder only after every plugin has started, so the file exists by then and the session runs after this returns.
    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (Environment.GetEnvironmentVariable(OutputVariable) is { Length: > 0 } output && SamplePath is { } path)
        {
            if (!File.Exists(path))
                File.WriteAllText(path, SampleFile.Create());
            _ = RunAndExitAsync(output, path, cancellationToken);
        }

        return Task.CompletedTask;
    }

    public void Dispose() => recorder.Dispose();

    private async Task RunAndExitAsync(string output, string path, CancellationToken cancellationToken)
    {
        var exitCode = 0;
        try
        {
            await RunAsync(output, path, cancellationToken);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"The typing benchmark failed: {exception}");
            exitCode = 1;
        }

        Environment.Exit(exitCode);
    }

    private async Task RunAsync(string output, string path, CancellationToken cancellationToken)
    {
        var results = new List<MetricSummary>();
        await WaitForWindowAsync(cancellationToken);
        await Task.Delay(Settle, cancellationToken);

        var empty = MemorySnapshot.Take();
        var editor = await editors.OpenAsync(path, new TextPosition(SampleFile.TypingLine, 0)) ?? throw new InvalidOperationException($"Could not open {path}.");
        await Task.Delay(OpenSettle, cancellationToken);
        results.AddRange(MemorySnapshot.Take().ToSummaries("open", empty));

        await FoldAsync(editor);
        editor.CaretOffset = LineStart(editor, SampleFile.TypingLine);
        editor.Focus();
        var input = FocusedElement() ?? throw new InvalidOperationException("The editor did not take the keyboard focus.");
        await Task.Delay(Settle, cancellationToken);

        Console.WriteLine("Typing started");
        var before = ProcessSnapshot.Take();
        recorder.Start("typing");
        await TypeAsync(input, cancellationToken);
        await Task.Delay(Settle, cancellationToken);
        recorder.Stop();
        results.AddRange((ProcessSnapshot.Take() - before).ToSummaries("typing"));

        Console.WriteLine("Scrolling started");
        Press(input, Key.Escape);
        editor.CaretOffset = LineStart(editor, SampleFile.LineOf(1, "    private int count;")) + "    private int ".Length;
        editor.ScrollTo(0);
        await Task.Delay(Settle, cancellationToken);
        before = ProcessSnapshot.Take();
        recorder.Start("scrolling");
        await ScrollAsync(input);
        recorder.Stop();
        results.AddRange((ProcessSnapshot.Take() - before).ToSummaries("scrolling"));
        results.AddRange(MemorySnapshot.Take().ToSummaries("end", empty));

        RunReport.Write(output, recorder.Summarize().Concat(results));
    }

    private static async Task WaitForWindowAsync(CancellationToken cancellationToken)
    {
        var deadline = Stopwatch.GetTimestamp() + (long)(WindowTimeout.TotalSeconds * Stopwatch.Frequency);
        while ((Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow is not { IsVisible: true })
        {
            if (Stopwatch.GetTimestamp() > deadline)
                throw new TimeoutException("The main window did not show.");
            await Task.Delay(100, cancellationToken);
        }
    }

    // Folds Describe in every tenth class and the body of class 5.
    private async Task FoldAsync(IEditorView editor)
    {
        var lines = Enumerable.Range(1, SampleFile.ClassCount / 10).Select(n => SampleFile.LineOf(n * 10, "        var builder"))
            .Append(SampleFile.LineOf(5, "    private int count;"));
        foreach (var line in lines)
        {
            editor.CaretOffset = LineStart(editor, line);
            await commands.ExecuteAsync(FoldCommand);
        }
    }

    private static async Task TypeAsync(InputElement input, CancellationToken cancellationToken)
    {
        Press(input, Key.End);
        Press(input, Key.Enter);
        var next = Stopwatch.GetTimestamp();
        foreach (var text in Lines)
        {
            foreach (var character in text)
            {
                await WaitUntilAsync(next, cancellationToken);
                input.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = character.ToString(), Source = input });
                next += (long)(KeyInterval.TotalSeconds * Stopwatch.Frequency);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(400), cancellationToken);
            Press(input, Key.Escape);
            Press(input, Key.Enter);
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
            next = Stopwatch.GetTimestamp();
        }
    }

    // Scrolls down two wheel notches each frame, as a fast flick does, and records how far apart the frames come.
    private Task ScrollAsync(InputElement input)
    {
        var top = TopLevel.GetTopLevel(input) ?? throw new InvalidOperationException("The editor is not in a window.");
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var frames = 0;
        var last = 0L;

        void OnFrame(TimeSpan time)
        {
            var now = Stopwatch.GetTimestamp();
            if (last != 0)
                recorder.Record(FrameInterval, Stopwatch.GetElapsedTime(last, now).TotalMilliseconds);
            last = now;
            if (frames++ == ScrollFrames)
            {
                done.TrySetResult();
                return;
            }

            input.RaiseEvent(new PointerWheelEventArgs(input, Wheel, top, new Point(200, 200), 0, PointerPointProperties.None, KeyModifiers.None,
                new Vector(0, -WheelNotchesPerFrame)));
            top.RequestAnimationFrame(OnFrame);
        }

        top.RequestAnimationFrame(OnFrame);
        return done.Task;
    }

    private static int LineStart(IEditorView editor, int line) => editor.Document.Buffer.Current.GetLine(line).Start;

    private static async Task WaitUntilAsync(long timestamp, CancellationToken cancellationToken)
    {
        var remaining = Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp(), timestamp);
        if (remaining > TimeSpan.Zero)
            await Task.Delay(remaining, cancellationToken);
    }

    private static void Press(InputElement input, Key key)
    {
        input.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, Source = input });
        input.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = key, Source = input });
    }

    private static InputElement? FocusedElement() =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow?.FocusManager?.GetFocusedElement() as InputElement;
}
