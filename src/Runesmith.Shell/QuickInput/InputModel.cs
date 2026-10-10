using Runesmith.Sdk.Shell;

namespace Runesmith.Shell.QuickInput;

/// <summary>The state of an input box: its text and what the plugin's check says about it, checked again after typing pauses.</summary>
/// <remarks>Use it on the UI thread.</remarks>
internal sealed class InputModel(InputBoxOptions options, Func<TimeSpan, CancellationToken, Task>? delay = null) : IDisposable
{
    public static readonly TimeSpan ValidationDelay = TimeSpan.FromMilliseconds(150);

    private readonly Func<TimeSpan, CancellationToken, Task> delay = delay ?? Task.Delay;
    private CancellationTokenSource? validation;
    private string? checkedText;

    public InputBoxOptions Options { get; } = options;

    public string Text { get; private set; } = options.Value ?? "";

    /// <summary>Gets what is wrong with the text, or null.</summary>
    public string? Error { get; private set; }

    /// <summary>Gets whether the check is running.</summary>
    public bool IsValidating { get; private set; }

    /// <summary>Raised when the error or the checking state changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised with the exception when the plugin's check fails.</summary>
    public event EventHandler<Exception>? Failed;

    /// <summary>Takes the typed text and checks it after the pause.</summary>
    public Task SetTextAsync(string text, bool immediate = false)
    {
        Text = text;
        return ValidateAsync(text, immediate);
    }

    /// <summary>Checks the text now, unless it was checked already; returns whether it can be accepted.</summary>
    public async Task<bool> TryAcceptAsync()
    {
        if (Options.Validate is null)
            return true;
        if (checkedText != Text || IsValidating)
            await ValidateAsync(Text, immediate: true);
        return checkedText == Text && Error is null;
    }

    public void Dispose()
    {
        validation?.Cancel();
        validation?.Dispose();
        validation = null;
    }

    private async Task ValidateAsync(string text, bool immediate)
    {
        if (Options.Validate is not { } validate)
            return;

        validation?.Cancel();
        validation?.Dispose();
        validation = new CancellationTokenSource();
        var token = validation.Token;
        checkedText = null;
        try
        {
            if (!immediate)
                await delay(ValidationDelay, token);
            token.ThrowIfCancellationRequested();
            IsValidating = true;
            Changed?.Invoke(this, EventArgs.Empty);
            var problem = await validate(text, token);
            if (token.IsCancellationRequested)
                return;

            Error = string.IsNullOrWhiteSpace(problem) ? null : problem;
            checkedText = text;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            Error = "This value could not be checked.";
            checkedText = text;
            Failed?.Invoke(this, exception);
        }

        IsValidating = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
