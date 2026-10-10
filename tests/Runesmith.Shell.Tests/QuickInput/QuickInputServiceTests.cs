using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HammerUI.Controls;
using HammerUI.Services;
using Runesmith.Composition;
using Runesmith.Sdk.Shell;
using Runesmith.Shell.QuickInput;
using Runesmith.Shell.Tests.Plugins;

namespace Runesmith.Shell.Tests.QuickInput;

public sealed class QuickInputServiceTests
{
    [Fact]
    public Task APickReturnsTheItemPickedWithEnterAndShowsWhoAsked() => Run(async (service, log) =>
    {
        var picking = service.PickAsync<int>([new("One", 1), new("Two", 2), new("Three", 3)], new QuickPickOptions { Title = "Numbers" });
        var view = await ShownAsync(service);

        Assert.Contains(view.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "Asked by acme.todo");
        Type(view, "thr");
        Press(view, Key.Enter);

        Assert.Equal(3, (await picking)?.Value);
        Assert.Null(service.View);
    });

    [Fact]
    public Task EscCancelsAndANewQuestionReplacesTheOneShowing() => Run(async (service, _) =>
    {
        var first = service.InputAsync(new InputBoxOptions { Prompt = "Name" });
        await ShownAsync(service);
        var second = service.PickAsync<string>([new("a", "a")]);

        Assert.Null(await first);
        var view = await ShownAsync(service);
        Press(view, Key.Escape);
        Assert.Null(await second);
    });

    [Fact]
    public Task APickOfManyReturnsTheCheckedItems() => Run(async (service, _) =>
    {
        var picking = service.PickManyAsync<string>([new("red", "red") { IsSelected = true }, new("green", "green"), new("blue", "blue")]);
        var view = await ShownAsync(service);

        Press(view, Key.Down);
        Press(view, Key.Space, KeyModifiers.Control);
        Press(view, Key.Enter);

        Assert.Equal(["red", "green"], (await picking)!.Select(i => i.Value));
    });

    [Fact]
    public Task AnInputBoxShowsTheProblemAndAcceptsOnlyValidText() => Run(async (service, _) =>
    {
        var asking = service.InputAsync(new InputBoxOptions
        {
            Value = "old-name",
            IsPassword = false,
            Validate = (text, _) => Task.FromResult(text.Contains(' ', StringComparison.Ordinal) ? "No spaces." : null),
        });
        var view = await ShownAsync(service);
        Assert.Equal("old-name", view.Query.Text);

        view.Query.Text = "new name";
        await view.AcceptAsync();
        Assert.False(asking.IsCompleted);
        Assert.Contains(view.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "No spaces." && t.IsVisible);

        view.Query.Text = "new-name";
        await view.AcceptAsync();
        Assert.Equal("new-name", await asking);
    });

    [Fact]
    public Task StepsGoBackToTheStepBeforeAndKeepTheBoxOpenBetweenThem() => Run(async (service, _) =>
    {
        string? name = null;
        string? kind = null;
        var visits = new List<int>();
        var running = service.RunStepsAsync("New component",
        [
            async step =>
            {
                visits.Add(step.Number);
                return (name = await step.InputAsync(new InputBoxOptions { Value = name })) is not null;
            },
            async step =>
            {
                visits.Add(step.Number);
                return (kind = (await step.PickAsync<string>([new("Class", "class"), new("Record", "record")]))?.Value) is not null;
            },
        ]);

        var view = await ShownAsync(service);
        Assert.Contains(view.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "Step 1 of 2");
        view.Query.Text = "Widget";
        await view.AcceptAsync();
        await WaitAsync(() => visits.Count == 2);
        Assert.Same(view, service.View);
        Assert.Contains(view.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "Step 2 of 2");

        Press(view, Key.Left, KeyModifiers.Alt);
        await WaitAsync(() => visits.Count == 3);
        Assert.Equal("Widget", view.Query.Text);
        view.Query.Text = "Gadget";
        await view.AcceptAsync();
        await WaitAsync(() => visits.Count == 4);
        Press(view, Key.Down);
        Press(view, Key.Enter);

        Assert.True(await running);
        Assert.Equal([1, 2, 1, 2], visits);
        Assert.Equal(("Gadget", "record"), (name, kind));
        Assert.Null(service.View);
    });

    [Fact]
    public Task ASourceThatFailsIsReportedUnderThePluginsName() => Run(async (service, log) =>
    {
        var picking = service.PickAsync<string>((_, _) => throw new InvalidOperationException("no network"));
        var view = await ShownAsync(service);
        await WaitAsync(() => log.Count > 0);

        Assert.Contains("acme.todo", log[0], StringComparison.Ordinal);
        Assert.Contains("no network", log[0], StringComparison.Ordinal);
        Press(view, Key.Escape);
        Assert.Null(await picking);
    });

    [Fact]
    public Task TheCallersCancellationClosesTheQuestion() => Run(async (service, _) =>
    {
        using var cancel = new CancellationTokenSource();
        var asking = service.InputAsync(cancellationToken: cancel.Token);
        await ShownAsync(service);

        await cancel.CancelAsync();

        Assert.Null(await asking);
        await WaitAsync(() => service.View is null);
    });

    private static async Task<QuickInputView> ShownAsync(QuickInputService service)
    {
        await WaitAsync(() => service.View?.IsAttachedToVisualTree() == true);
        Dispatcher.UIThread.RunJobs();
        return service.View!;
    }

    private static void Type(QuickInputView view, string text) => view.Query.Text = text;

    private static void Press(QuickInputView view, Key key, KeyModifiers modifiers = KeyModifiers.None) =>
        view.Query.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers, Source = view.Query });

    private static async Task WaitAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The condition was not met in time.");
            await Task.Delay(10);
        }
    }

    private static Task<bool> Run(Func<QuickInputService, List<string>, Task> test) =>
        HeadlessSession.Value.Dispatch(async () =>
        {
            var host = new WindowHost();
            var dialogs = new DialogHost();
            var window = new Window { Width = 900, Height = 700, Content = dialogs };
            window.Show();
            host.Attach(window, dialogs, new ToastHost());
            var log = new List<string>();
            PluginInfo plugin = TestCallers.Plugin("acme.todo");
            var service = new QuickInputService(new DialogService(host), null, () => plugin, log.Add);
            await test(service, log);
            window.Close();
            return true;
        }, TestContext.Current.CancellationToken);
}
