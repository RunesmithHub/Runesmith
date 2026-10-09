using System.Collections.ObjectModel;
using System.Composition;
using System.Text;
using Runesmith.Sdk.Shell;
using Runesmith.Sdk.ToolWindows;

namespace Runesmith.Shell.Services;

/// <summary>Keeps the Output panel's channels.</summary>
[Export(typeof(IOutputService))]
[Export]
[Shared]
[method: ImportingConstructor]
public sealed class OutputService(Lazy<IToolWindowManager> toolWindows) : IOutputService
{
    private readonly Dictionary<string, OutputChannel> byName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the channels, in the order they were created.</summary>
    public ObservableCollection<OutputChannel> Channels { get; } = [];

    /// <summary>Raised when a channel asks to be shown.</summary>
    public event EventHandler<OutputChannel>? ShowRequested;

    public IOutputChannel GetChannel(string name)
    {
        lock (byName)
        {
            if (byName.TryGetValue(name, out var existing))
                return existing;

            var channel = new OutputChannel(name, Show);
            byName[name] = channel;
            UiThread.Run(() => Channels.Add(channel));
            return channel;
        }
    }

    private void Show(OutputChannel channel) => UiThread.Run(() =>
    {
        toolWindows.Value.Show(OutputToolWindowId);
        ShowRequested?.Invoke(this, channel);
    });

    /// <summary>The id of the Output tool window.</summary>
    public const string OutputToolWindowId = "output";
}

/// <summary>A channel of the Output panel: lines of text, of which it keeps the most recent.</summary>
public sealed class OutputChannel : IOutputChannel
{
    private const int MaximumLines = 20_000;

    private readonly Action<OutputChannel> show;
    private readonly StringBuilder partial = new();

    internal OutputChannel(string name, Action<OutputChannel> show)
    {
        Name = name;
        this.show = show;
    }

    public string Name { get; }

    /// <summary>Gets the lines, on the UI thread.</summary>
    public ObservableCollection<string> Lines { get; } = [];

    public void Append(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        List<string> complete;
        lock (partial)
        {
            partial.Append(text);
            var all = partial.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
            var lastBreak = all.LastIndexOf('\n');
            if (lastBreak < 0)
                return;

            complete = [.. all[..lastBreak].Split('\n')];
            partial.Clear().Append(all[(lastBreak + 1)..]);
        }

        Add(complete);
    }

    public void AppendLine(string line) => Append(line + "\n");

    public void Clear() => UiThread.Run(Lines.Clear);

    public void Show() => show(this);

    public override string ToString() => Name;

    private void Add(List<string> lines) => UiThread.Run(() =>
    {
        foreach (var line in lines)
            Lines.Add(line);
        var excess = Lines.Count - MaximumLines;
        for (var i = 0; i < excess; i++)
            Lines.RemoveAt(0);
    });
}
