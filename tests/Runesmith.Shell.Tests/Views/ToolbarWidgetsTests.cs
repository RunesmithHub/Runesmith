using Avalonia.Controls;
using Runesmith.Sdk.Shell;
using Runesmith.Shell.Tests.Running;
using Runesmith.Shell.Views;

namespace Runesmith.Shell.Tests.Views;

public sealed class ToolbarWidgetsTests
{
    [Fact]
    public Task CreatesEachWidgetOnceInItsSlotInOrderAndLeavesOutOnesThatFail() => HeadlessSession.Value.Dispatch(() =>
    {
        var output = new FakeOutput();
        var late = new Provider(ToolbarSlot.Leading, 20, "late");
        Lazy<IToolbarWidgetProvider>[] providers =
        [
            new(() => late),
            new(() => new Provider(ToolbarSlot.Trailing, 0, "trailing")),
            new(() => throw new InvalidOperationException("broken plugin")),
            new(() => new Provider(ToolbarSlot.Leading, 10, "early")),
        ];

        var widgets = new ToolbarWidgets(providers, output).Create();

        Assert.Equal(["early", "late"], widgets[ToolbarSlot.Leading].Select(w => w.Name));
        Assert.Equal(["trailing"], widgets[ToolbarSlot.Trailing].Select(w => w.Name));
        Assert.Equal(1, late.Created);
        Assert.Contains(output.Lines, line => line.Contains("broken plugin", StringComparison.Ordinal));
    }, TestContext.Current.CancellationToken);

    private sealed class Provider(ToolbarSlot slot, int order, string name) : IToolbarWidgetProvider
    {
        public int Created { get; private set; }

        public ToolbarSlot Slot => slot;

        public int Order => order;

        public Control CreateWidget()
        {
            Created++;
            return new Button { Name = name };
        }
    }
}
