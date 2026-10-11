using Runesmith.Sdk.Analyzers.Text;

namespace Runesmith.Sdk.Analyzers.Tests;

public sealed class UserVisibleTextAnalyzerTests
{
    private const string Code = """
        using Avalonia.Controls;
        using Runesmith.Sdk.Commands;
        using Runesmith.Sdk.Shell;
        using Runesmith.Sdk.ToolWindows;

        public static class Views
        {
            public static void Build(INotificationService notifications, string name, bool flag)
            {
                var title = new TextBlock { Text = "Hello" };
                title.Text = $"Hello {name}";
                var button = new Button { Content = "Save" };
                var window = new Window { Title = flag ? "Yes" : "No" };
                var box = new TextBox { PlaceholderText = "Search files" };
                var expander = new Expander { Header = "More" };
                ToolTip.SetTip(button, "Saves the file");
                notifications.Notify(NotificationKind.Info, "Saved", "All files were saved.", "Undo");
                _ = notifications.ConfirmAsync("Delete?", "The file goes to the trash.");
                var command = new CommandDefinition("todo.add", "Add To-do", "To-do");
                var item = new TreeItem("Inbox");
                var pick = new PickItem<int>("Pick me", 1);
                var separator = PickItem.Separator<int>("Recent");

                title.Text = "";
                title.Text = "—";
                title.Text = "42 %";
                title.Text = name;
                title.Tag = "Not shown";
                title.Text = $"{name}";
                notifications.Notify(NotificationKind.Info, name);
            }
        }
        """;

    private static readonly Dictionary<string, string> OptedIn = new() { [UserVisibleTextAnalyzer.EnabledProperty] = "true" };

    [Fact]
    public async Task LiteralsGivenToWhatUsersSeeAreFlagged()
    {
        var diagnostics = await TestCompilation.AnalyzeAsync(new UserVisibleTextAnalyzer(), OptedIn, Code);

        Assert.All(diagnostics, d => Assert.Equal("RSL0100", d.Id));
        Assert.Equal(
            ["Hello", "Hello {name}", "Save", "Yes", "No", "Search files", "More", "Saves the file", "Saved", "All files were saved.", "Delete?",
                "The file goes to the trash.", "Add To-do", "Inbox", "Pick me", "Recent"],
            diagnostics.OrderBy(d => d.Location.SourceSpan.Start).Select(d => d.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Split('"')[1]));
    }

    [Fact]
    public async Task ADiagnosticPointsAtTheLiteral()
    {
        var diagnostics = await TestCompilation.AnalyzeAsync(new UserVisibleTextAnalyzer(), OptedIn, Code);

        var first = diagnostics.OrderBy(d => d.Location.SourceSpan.Start).First();
        var span = first.Location.GetLineSpan();
        Assert.Equal((9, 43), (span.StartLinePosition.Line, span.StartLinePosition.Character));
        Assert.Equal("\"Hello\"", Code.Replace("\r\n", "\n", StringComparison.Ordinal)[first.Location.SourceSpan.Start..first.Location.SourceSpan.End]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    [InlineData("")]
    public async Task NothingIsFlaggedUnlessTheProjectOptsIn(string? value)
    {
        var properties = value is null ? new Dictionary<string, string>() : new Dictionary<string, string> { [UserVisibleTextAnalyzer.EnabledProperty] = value };

        Assert.Empty(await TestCompilation.AnalyzeAsync(new UserVisibleTextAnalyzer(), properties, Code));
    }
}
