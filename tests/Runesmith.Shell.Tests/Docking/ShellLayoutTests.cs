using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HammerUI.Docking;
using Runesmith.Shell.Docking;

namespace Runesmith.Shell.Tests.Docking;

public sealed class ShellLayoutTests
{
    [Fact]
    public Task ADocumentClosedAndOpenedAgainShowsItsNewEditor() => HeadlessSession.Value.Dispatch(() =>
    {
        var layout = new ShellLayout();
        var host = new DockHost();
        layout.Attach(host);
        var window = new Window { Width = 600, Height = 400, Content = host };
        window.Show();
        var id = ShellLayout.DocumentPanelId("/tmp/a.cs");
        var first = new TestPanel(id);
        layout.Register(first);
        layout.AddToEditors(id);
        Dispatcher.UIThread.RunJobs();

        layout.Layout.ClosePanel(id);
        layout.Unregister(id);
        var second = new TestPanel(id);
        layout.Register(second);
        layout.AddToEditors(id);
        Dispatcher.UIThread.RunJobs();

        Assert.Same(second.Content, host.GetContent(id));
        Assert.Contains(second.Content, host.GetVisualDescendants());
        Assert.DoesNotContain(first.Content, host.GetVisualDescendants());
        window.Close();
    }, CancellationToken.None);

    private sealed class TestPanel(string id) : IDockPanel
    {
        public string Id => id;

        public string Title => id;

        public Geometry? Icon => null;

        public bool CanClose => true;

        public Control Content { get; } = new Border();
    }
}
