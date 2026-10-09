namespace Runesmith.Editor;

/// <summary>Helpers for the one request at a time that popups such as completion keep running.</summary>
internal static class Cancellation
{
    /// <summary>Cancels and disposes the running request's source and returns the token of a new one.</summary>
    public static CancellationToken Renew(ref CancellationTokenSource? source)
    {
        Cancel(ref source);
        source = new CancellationTokenSource();
        return source.Token;
    }

    /// <summary>Cancels and disposes the running request's source.</summary>
    public static void Cancel(ref CancellationTokenSource? source)
    {
        source?.Cancel();
        source?.Dispose();
        source = null;
    }
}
