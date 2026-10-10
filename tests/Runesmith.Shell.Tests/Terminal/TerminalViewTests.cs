using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Runesmith.Sdk.Terminals;
using Runesmith.Shell.Running;
using Runesmith.Shell.Terminal;
using Runesmith.Shell.Tests.Running;

namespace Runesmith.Shell.Tests.Terminal;

public sealed class TerminalViewTests
{
    private const string Esc = "\u001b";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public Task CtrlClickingALinkTheProgramWroteOpensAWebPageOrAFileButNoOtherKindOfLink() => HeadlessSession.Value.Dispatch(() =>
    {
        if (OperatingSystem.IsWindows())
            return true;

        var file = Path.Combine(Directory.CreateTempSubdirectory("runesmith-terminal-link-").FullName, "App.cs");
        File.WriteAllText(file, "class App {}\n");
        var service = new TerminalService(new FakeWorkspace(Path.GetTempPath()), null, () => null, _ => { });
        var session = (TerminalSession)service.CreateTerminal("Links", new TerminalOptions { Command = "/bin/sh", Arguments = ["-c", "sleep 30"], IsHidden = true });
        var opened = new List<string>();
        var palette = new TerminalPalette(new TerminalPalette.ThemeInput(true, Colors.Black, Colors.White, Colors.Gray, Colors.Gray, Colors.DodgerBlue, Colors.Green,
            Colors.Yellow, Colors.Red, Colors.Cyan), null);
        var view = new TerminalView(session, palette, _ => false, new ConsoleLinkResolver(() => [], () => null), (path, line, _) => opened.Add($"{path}:{line}"),
            opened.Add);
        var window = new Window { Width = 600, Height = 200, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        lock (session.Gate)
        {
            session.Screen.Feed($"{Esc}]8;;https://hub.runesmith.dev{Esc}\\hub{Esc}]8;;{Esc}\\ {Esc}]8;;javascript:alert(1){Esc}\\bad{Esc}]8;;{Esc}\\ "
                + $"{Esc}]8;;{new Uri(file).AbsoluteUri}#3{Esc}\\app{Esc}]8;;{Esc}\\ {Esc}]8;;file:///nowhere/Missing.cs{Esc}\\gone{Esc}]8;;{Esc}\\");
        }

        int columns;
        lock (session.Gate)
            columns = session.Screen.Columns;
        var cellWidth = (view.Bounds.Width - 24) / columns;
        Point Cell(int column) => view.TranslatePoint(new Point(6 + ((column + 0.5) * cellWidth), 6 + 4), window)!.Value;
        void CtrlClick(int column)
        {
            window.MouseMove(Cell(column), RawInputModifiers.Control);
            window.MouseDown(Cell(column), MouseButton.Left, RawInputModifiers.Control);
            window.MouseUp(Cell(column), MouseButton.Left, RawInputModifiers.Control);
        }

        window.MouseMove(Cell(2), RawInputModifiers.None);
        Assert.Equal("https://hub.runesmith.dev/\nCtrl+click to open", ToolTip.GetTip(view));

        CtrlClick(2);
        CtrlClick(5);
        CtrlClick(9);
        CtrlClick(15);

        Assert.Equal(["https://hub.runesmith.dev/", $"{file}:3"], opened);
        window.MouseMove(Cell(5), RawInputModifiers.None);
        Assert.Null(ToolTip.GetTip(view));
        window.Close();
        session.Close();
        Directory.Delete(Path.GetDirectoryName(file)!, recursive: true);
        return true;
    }, Token);
}
