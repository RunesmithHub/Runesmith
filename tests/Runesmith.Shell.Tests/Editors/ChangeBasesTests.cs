using System.ComponentModel;
using System.Text;
using Runesmith.Editor;
using Runesmith.Sdk.Documents;
using Runesmith.Shell.Editors;
using Runesmith.Shell.Tests.Running;
using Runesmith.Text;

namespace Runesmith.Shell.Tests.Editors;

public sealed class ChangeBasesTests
{
    private static readonly string FilePath = Path.Combine(Path.GetTempPath(), "runesmith-tests", "Program.cs");

    [Fact]
    public Task TakesTheFirstBaseAProviderHasAndSkipsOnesThatFail() => Run(async () =>
    {
        var output = new FakeOutput();
        var area = new TextArea(new FileDocument(FilePath, "new"));
        var bases = new ChangeBases(
            [new(() => new Provider(null)), new(() => throw new InvalidOperationException("broken")), new(() => new Provider("failing") { Fails = true }), new(() => new Provider("old")), new(() => new Provider("later"))],
            output);

        bases.Track(area);
        await WaitAsync(() => area.BaseText is not null);

        Assert.Equal("old", area.BaseText);
        Assert.Contains(output.Lines, line => line.Contains("broken", StringComparison.Ordinal));
    });

    [Fact]
    public Task AsksAgainWhenAProviderSaysTheBaseOfTheFileChanged() => Run(async () =>
    {
        var provider = new Provider("first");
        var area = new TextArea(new FileDocument(FilePath, "text"));
        var bases = new ChangeBases([new(() => provider)], new FakeOutput());
        bases.Track(area);
        await WaitAsync(() => area.BaseText == "first");

        provider.Text = "second";
        provider.Raise([Path.Combine(Path.GetDirectoryName(FilePath)!, "Other.cs")]);
        await Task.Delay(50);
        Assert.Equal("first", area.BaseText);

        provider.Raise([FilePath]);
        await WaitAsync(() => area.BaseText == "second");

        provider.Text = "third";
        provider.Raise(null);
        await WaitAsync(() => area.BaseText == "third");

        bases.Untrack(area);
        provider.Text = "fourth";
        provider.Raise(null);
        await Task.Delay(50);
        Assert.Equal("third", area.BaseText);
    });

    private static Task<bool> Run(Func<Task> test) =>
        HeadlessSession.Value.Dispatch(async () =>
        {
            await test();
            return true;
        }, TestContext.Current.CancellationToken);

    private static async Task WaitAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
            await Task.Delay(10);
        Assert.True(condition());
    }

    private sealed class Provider(string? text) : IChangeBaseProvider
    {
        public string? Text { get; set; } = text;

        public bool Fails { get; init; }

        public event EventHandler<BaseTextChangedEventArgs>? BaseTextChanged;

        public async Task<string?> GetBaseTextAsync(string filePath, CancellationToken cancellationToken)
        {
            await Task.Yield();
            return Fails ? throw new IOException("no base") : Text;
        }

        public void Raise(IReadOnlyList<string>? paths) => BaseTextChanged?.Invoke(this, new BaseTextChangedEventArgs(paths));
    }

    private sealed class FileDocument(string path, string text) : IDocument
    {
        event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
        {
            add { }
            remove { }
        }

        public string? FilePath => path;

        public string Name => Path.GetFileName(path);

        public TextBuffer Buffer { get; } = new(text);

        public UndoHistory History { get; } = new();

        public string LanguageId { get; set; } = "plaintext";

        public LineEnding LineEnding { get; set; }

        public Encoding Encoding { get; set; } = Encoding.UTF8;

        public bool IsModified => false;

        public bool IsReadOnly => false;
    }
}
