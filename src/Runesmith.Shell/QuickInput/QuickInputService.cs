using System.Composition;
using Avalonia.Threading;
using HammerUI.Services;
using Runesmith.Composition;
using Runesmith.Sdk.Shell;
using Runesmith.Shell.Appearance;
using Runesmith.Shell.Services;

namespace Runesmith.Shell.QuickInput;

/// <summary>Asks plugins' questions in the quick input box: quick picks, input boxes and questions in steps. Each question shows which plugin
/// asked it, and a failure of the plugin's search or check is written to the Plugins output under the plugin's name.</summary>
[Export(typeof(IQuickInputService))]
[Export]
[Shared]
public sealed class QuickInputService : IQuickInputService
{
    private readonly DialogService dialogs;
    private readonly Lazy<FileIcons>? icons;
    private readonly Func<PluginInfo?> caller;
    private readonly Action<string> log;
    private QuickInputView? view;
    private Action<QuickInputOutcome>? pending;
    private int flows;

    [ImportingConstructor]
    public QuickInputService(NotificationService notifications, Lazy<FileIcons> icons, IOutputService output)
        : this(notifications.Dialogs, icons, PluginCallers.Current, line => output.GetChannel(PluginAccess.ChannelName).AppendLine(line))
    {
    }

    internal QuickInputService(DialogService dialogs, Lazy<FileIcons>? icons, Func<PluginInfo?> caller, Action<string> log)
    {
        this.dialogs = dialogs;
        this.icons = icons;
        this.caller = caller;
        this.log = log;
    }

    /// <summary>Gets the box while it shows, for the tests.</summary>
    internal QuickInputView? View => view;

    public Task<PickItem<T>?> PickAsync<T>(IReadOnlyList<PickItem<T>> items, QuickPickOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        var asker = caller();
        return First(PickCoreAsync<T>(asker, () => FromItems(items, options, many: false), null, cancellationToken));
    }

    public Task<PickItem<T>?> PickAsync<T>(PickItemSource<T> source, QuickPickOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        var asker = caller();
        return First(PickCoreAsync<T>(asker, () => FromSource(source, options, many: false), null, cancellationToken));
    }

    public Task<IReadOnlyList<PickItem<T>>?> PickManyAsync<T>(IReadOnlyList<PickItem<T>> items, QuickPickOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        var asker = caller();
        return PickCoreAsync<T>(asker, () => FromItems(items, options, many: true), null, cancellationToken);
    }

    public Task<IReadOnlyList<PickItem<T>>?> PickManyAsync<T>(PickItemSource<T> source, QuickPickOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        var asker = caller();
        return PickCoreAsync<T>(asker, () => FromSource(source, options, many: true), null, cancellationToken);
    }

    public Task<string?> InputAsync(InputBoxOptions? options = null, CancellationToken cancellationToken = default) =>
        InputCoreAsync(caller(), options ?? new InputBoxOptions(), null, cancellationToken);

    public Task<bool> RunStepsAsync(string title, IReadOnlyList<Func<IQuickInputStep, Task<bool>>> steps, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(steps);
        var asker = caller();
        return OnUiThread(() => RunStepsCoreAsync(asker, title, steps, cancellationToken));
    }

    private async Task<bool> RunStepsCoreAsync(PluginInfo? asker, string title, IReadOnlyList<Func<IQuickInputStep, Task<bool>>> steps, CancellationToken cancellationToken)
    {
        flows++;
        try
        {
            var index = 0;
            var asked = new Stack<int>();
            while (index < steps.Count)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var step = new Step(this, asker, title, index + 1, steps.Count, asked.Count > 0, cancellationToken);
                bool next;
                try
                {
                    next = await steps[index](step);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    Report(asker, $"step {index + 1} of \"{title}\" failed", exception);
                    return false;
                }

                if (next)
                {
                    if (step.HasAsked)
                        asked.Push(index);
                    index++;
                }
                else if (step.WentBack && asked.Count > 0)
                {
                    index = asked.Pop();
                }
                else
                {
                    return false;
                }
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        finally
        {
            flows--;
            if (flows == 0 && pending is null)
                CloseView();
        }
    }

    private Task<IReadOnlyList<PickItem<T>>?> PickCoreAsync<T>(PluginInfo? asker, Func<PickModel> create, StepFrame? step, CancellationToken cancellationToken) =>
        OnUiThread(async () =>
        {
            var model = create();
            model.Failed += (_, exception) => Report(asker, "the quick pick's search failed", exception);
            var outcome = await AskAsync(asker, step, model.Options.Title, (shown, frame, onDone) => shown.ShowPick(model, frame, onDone), cancellationToken);
            if (step is not null)
                step.Answered(outcome);
            if (outcome != QuickInputOutcome.Accepted || model.Accept() is not { } rows)
                return null;

            return (IReadOnlyList<PickItem<T>>?)[.. rows.Select(r => (PickItem<T>)r.Item)];
        });

    private Task<string?> InputCoreAsync(PluginInfo? asker, InputBoxOptions options, StepFrame? step, CancellationToken cancellationToken) =>
        OnUiThread(async () =>
        {
            var model = new InputModel(options);
            model.Failed += (_, exception) => Report(asker, "the input box's check failed", exception);
            var text = (string?)null;
            var outcome = await AskAsync(asker, step, options.Title, (shown, frame, onDone) => shown.ShowInput(model, frame, result =>
            {
                text = model.Text;
                onDone(result);
            }), cancellationToken);
            step?.Answered(outcome);
            return outcome == QuickInputOutcome.Accepted ? text : null;
        });

    private async Task<QuickInputOutcome> AskAsync(PluginInfo? asker, StepFrame? step, string? title, Action<QuickInputView, QuickInputFrame, Action<QuickInputOutcome>> show,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return QuickInputOutcome.Cancelled;

        pending?.Invoke(QuickInputOutcome.Cancelled);
        var completion = new TaskCompletionSource<QuickInputOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Complete(QuickInputOutcome outcome)
        {
            if (!completion.TrySetResult(outcome))
                return;

            pending = null;
            if (outcome == QuickInputOutcome.Cancelled || flows == 0)
                CloseView();
            else
                view?.ShowWaiting();
        }

        pending = Complete;
        var shown = EnsureView();
        var frame = new QuickInputFrame(title ?? step?.Title, step?.Number, step?.Count, step?.CanGoBack == true, asker?.Manifest.Name);
        show(shown, frame, Complete);
        await using var registration = cancellationToken.Register(() => Dispatcher.UIThread.Post(() => Complete(QuickInputOutcome.Cancelled)));
        return await completion.Task;
    }

    private QuickInputView EnsureView()
    {
        if (view is not null)
            return view;

        var created = new QuickInputView(icons);
        view = created;
        _ = WatchAsync(created);
        return created;
    }

    private async Task WatchAsync(QuickInputView shown)
    {
        await dialogs.ShowAsync(shown);
        if (!ReferenceEquals(view, shown))
            return;

        view = null;
        pending?.Invoke(QuickInputOutcome.Cancelled);
    }

    private void CloseView()
    {
        if (view is not { } shown)
            return;

        view = null;
        dialogs.Close(shown, null);
    }

    private void Report(PluginInfo? asker, string what, Exception exception) =>
        log(asker is null ? $"Runesmith: {what}: {exception.Message}" : $"{asker.Manifest.Name} ({asker.Manifest.Id}): {what}: {exception}");

    private static PickModel FromItems<T>(IReadOnlyList<PickItem<T>> items, QuickPickOptions? options, bool many) =>
        new([.. items.Select(PickRow.From)], options ?? new QuickPickOptions(), many);

    private static PickModel FromSource<T>(PickItemSource<T> source, QuickPickOptions? options, bool many) =>
        new(async (query, token) => [.. (await source(query, token) ?? []).Select(PickRow.From)], options ?? new QuickPickOptions(), many);

    private static async Task<PickItem<T>?> First<T>(Task<IReadOnlyList<PickItem<T>>?> picked) => await picked is { Count: > 0 } rows ? rows[0] : null;

    private static Task<T> OnUiThread<T>(Func<Task<T>> work) =>
        Dispatcher.UIThread.CheckAccess() ? work() : Dispatcher.UIThread.InvokeAsync(work);

    /// <summary>Where a question asked by a step stands.</summary>
    private sealed record StepFrame(string Title, int Number, int Count, bool CanGoBack, Step Owner)
    {
        public void Answered(QuickInputOutcome outcome) => Owner.Answered(outcome);
    }

    private sealed class Step(QuickInputService service, PluginInfo? asker, string title, int number, int count, bool canGoBack, CancellationToken flowCancellation)
        : IQuickInputStep
    {
        public int Number => number;

        public int Count => count;

        public bool HasAsked { get; private set; }

        public bool WentBack { get; private set; }

        public void Answered(QuickInputOutcome outcome)
        {
            HasAsked = true;
            WentBack = outcome == QuickInputOutcome.Back;
        }

        public Task<PickItem<T>?> PickAsync<T>(IReadOnlyList<PickItem<T>> items, QuickPickOptions? options = null, CancellationToken cancellationToken = default) =>
            First(service.PickCoreAsync<T>(asker, () => FromItems(items, options, many: false), Frame(), Link(cancellationToken)));

        public Task<PickItem<T>?> PickAsync<T>(PickItemSource<T> source, QuickPickOptions? options = null, CancellationToken cancellationToken = default) =>
            First(service.PickCoreAsync<T>(asker, () => FromSource(source, options, many: false), Frame(), Link(cancellationToken)));

        public Task<IReadOnlyList<PickItem<T>>?> PickManyAsync<T>(IReadOnlyList<PickItem<T>> items, QuickPickOptions? options = null, CancellationToken cancellationToken = default) =>
            service.PickCoreAsync<T>(asker, () => FromItems(items, options, many: true), Frame(), Link(cancellationToken));

        public Task<IReadOnlyList<PickItem<T>>?> PickManyAsync<T>(PickItemSource<T> source, QuickPickOptions? options = null, CancellationToken cancellationToken = default) =>
            service.PickCoreAsync<T>(asker, () => FromSource(source, options, many: true), Frame(), Link(cancellationToken));

        public Task<string?> InputAsync(InputBoxOptions? options = null, CancellationToken cancellationToken = default) =>
            service.InputCoreAsync(asker, options ?? new InputBoxOptions(), Frame(), Link(cancellationToken));

        private StepFrame Frame() => new(title, number, count, canGoBack, this);

        private CancellationToken Link(CancellationToken token) =>
            token.CanBeCanceled ? CancellationTokenSource.CreateLinkedTokenSource(token, flowCancellation).Token : flowCancellation;
    }
}
