using System.Composition;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Plugins;
using Runesmith.Text;

namespace Runesmith.TypingBenchmark;

/// <summary>Once the language's analyzer has loaded and warmed up the folder's projects, types sixteen lines into a method at about 140 words a minute, writes what
/// the editor and the language services measured meanwhile to the folder <c>RUNESMITH_TYPING_BENCHMARK</c> names, and exits.</summary>
/// <remarks><c>RUNESMITH_TYPING_BENCHMARK_FILE</c> names the file and the line to type after, as <c>path:line</c>, and
/// <c>RUNESMITH_TYPING_BENCHMARK_LANGUAGE</c> the language, <c>csharp</c> (the default) or <c>java</c>. Nothing is saved.</remarks>
[Export(typeof(IPlugin))]
[method: ImportingConstructor]
public sealed class TypingBenchmark(IEditorService editors) : IPlugin
{
    private const string OutputVariable = "RUNESMITH_TYPING_BENCHMARK";
    private const string FileVariable = "RUNESMITH_TYPING_BENCHMARK_FILE";
    private const string LanguageVariable = "RUNESMITH_TYPING_BENCHMARK_LANGUAGE";
    private const string ReadyInstrument = "runesmith.language.request";

    private static readonly TimeSpan KeyInterval = TimeSpan.FromMilliseconds(70);
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(2);

    private static readonly Scenario CSharp = new("Load C# projects",
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
    ]);

    private static readonly Scenario Java = new("Load Java projects",
    [
        "var probe = new ArrayList<String>();",
        "probe.add(String.valueOf(probe.size()));",
        "var words = List.of(\"alpha\", \"beta\", \"gamma\");",
        "var longest = words.stream().max(Comparator.comparingInt(String::length)).orElse(\"\");",
        "var builder = new StringBuilder(longest);",
        "builder.append(words.size()).append(':');",
        "for (var word : words)",
        "System.out.println(word.toUpperCase(Locale.ROOT));",
        "var count = words.stream().filter(word -> Character.isUpperCase(word.charAt(0))).count();",
        "var lookup = new HashMap<String, Integer>();",
        "lookup.put(builder.toString(), (int) count);",
        "if (lookup.containsKey(longest) && count > 0)",
        "probe.clear();",
        "var text = String.join(\",\", words).strip();",
        "var number = text.length() + probe.size();",
        "Objects.requireNonNull(builder.toString(), text);",
    ]);

    private readonly MetricRecorder recorder = new();

    // Runesmith opens the folder only after every plugin has started, so the session runs after this returns.
    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (Environment.GetEnvironmentVariable(OutputVariable) is { Length: > 0 } output
            && Environment.GetEnvironmentVariable(FileVariable) is { Length: > 0 } target)
        {
            _ = RunAndExitAsync(output, target, cancellationToken);
        }

        return Task.CompletedTask;
    }

    private async Task RunAndExitAsync(string output, string target, CancellationToken cancellationToken)
    {
        var exitCode = 0;
        try
        {
            await RunAsync(output, target, cancellationToken);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"The typing benchmark failed: {exception}");
            exitCode = 1;
        }

        Environment.Exit(exitCode);
    }

    public void Dispose() => recorder.Dispose();

    private async Task RunAsync(string output, string target, CancellationToken cancellationToken)
    {
        var separator = target.LastIndexOf(':');
        var path = target[..separator];
        var line = int.Parse(target[(separator + 1)..], System.Globalization.CultureInfo.InvariantCulture);
        var scenario = Environment.GetEnvironmentVariable(LanguageVariable) == "java" ? Java : CSharp;

        await recorder.WaitForAsync(ReadyInstrument, scenario.ReadyRequest).WaitAsync(ReadyTimeout, cancellationToken);
        var editor = await editors.OpenAsync(path, new TextPosition(line - 1, 0))
            ?? throw new InvalidOperationException($"Could not open {path}.");
        await Task.Delay(Settle, cancellationToken);

        editor.Focus();
        var input = FocusedElement() ?? throw new InvalidOperationException("The editor did not take the keyboard focus.");
        var before = ProcessSnapshot.Take();
        Console.WriteLine("Typing started");
        recorder.Start();

        Press(input, Key.End);
        Press(input, Key.Enter);
        var next = Stopwatch.GetTimestamp();
        foreach (var text in scenario.Lines)
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

        await Task.Delay(Settle, cancellationToken);
        recorder.Stop();
        RunReport.Write(output, recorder.Summarize(), ProcessSnapshot.Take() - before);
    }

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

    /// <summary>What to type, and the analyzer request whose end means the folder is loaded.</summary>
    private sealed record Scenario(string ReadyRequest, string[] Lines);
}
