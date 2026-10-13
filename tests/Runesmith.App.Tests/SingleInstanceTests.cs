using System.IO.Pipes;
using System.Text;

namespace Runesmith.App.Tests;

public sealed class SingleInstanceTests
{
    private static readonly SingleInstance.Message Handover = new(["/home/me/notes.md"], "/home/me");

    private readonly string pipe = "runesmith-test-" + Guid.NewGuid().ToString("N")[..12];

    [Fact]
    public void AStartWithNoRunesmithRunningContinues() =>
        Assert.Equal(HandOverResult.NoneRunning, SingleInstance.TryHandOver(pipe, Handover, TimeSpan.FromSeconds(1)));

    [Fact]
    public async Task TheRunningRunesmithAnswersOnceItHasTakenTheHandover()
    {
        var taken = new TaskCompletionSource<SingleInstance.Message>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var instance = new SingleInstance(pipe);
        instance.Listen(message =>
        {
            taken.SetResult(message);
            return Task.CompletedTask;
        });

        var result = await Task.Run(() => SingleInstance.TryHandOver(pipe, Handover, TimeSpan.FromSeconds(5)), TestContext.Current.CancellationToken);

        Assert.Equal(HandOverResult.HandedOver, result);
        Assert.Equal(Handover.Paths, (await taken.Task).Paths);
    }

    [Fact]
    public async Task ARunningRunesmithThatDoesNotTakeTheHandoverInTimeIsNotResponding()
    {
        var never = new TaskCompletionSource();
        using var instance = new SingleInstance(pipe);
        instance.Listen(_ => never.Task);

        var result = await Task.Run(() => SingleInstance.TryHandOver(pipe, Handover, TimeSpan.FromMilliseconds(300)), TestContext.Current.CancellationToken);

        Assert.Equal(HandOverResult.NotResponding, result);
        never.SetResult();
    }

    [Fact]
    public async Task AStartThatClosesThePipeWithoutWaitingIsStillUnderstood()
    {
        var taken = new TaskCompletionSource<SingleInstance.Message>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var instance = new SingleInstance(pipe);
        instance.Listen(message =>
        {
            taken.SetResult(message);
            return Task.CompletedTask;
        });

        await using (var client = new NamedPipeClientStream(".", pipe, PipeDirection.Out))
        {
            await client.ConnectAsync(5000, TestContext.Current.CancellationToken);
            await client.WriteAsync(Encoding.UTF8.GetBytes("""{"Paths":["/home/me/notes.md"],"WorkingDirectory":"/home/me"}"""), TestContext.Current.CancellationToken);
        }

        var message = await taken.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal("/home/me", message.WorkingDirectory);
    }

    [Fact]
    public async Task AHandoverTheWindowFailsToTakeDoesNotStopTheListening()
    {
        var calls = 0;
        using var instance = new SingleInstance(pipe);
        instance.Listen(_ => Interlocked.Increment(ref calls) == 1 ? throw new InvalidOperationException("The window is closing.") : Task.CompletedTask);

        var first = await Task.Run(() => SingleInstance.TryHandOver(pipe, Handover, TimeSpan.FromSeconds(5)), TestContext.Current.CancellationToken);
        var second = await Task.Run(() => SingleInstance.TryHandOver(pipe, Handover, TimeSpan.FromSeconds(5)), TestContext.Current.CancellationToken);

        Assert.Equal(HandOverResult.NotResponding, first);
        Assert.Equal(HandOverResult.HandedOver, second);
    }
}
