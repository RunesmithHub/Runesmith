namespace Runesmith.Hub.Links;

/// <summary>Keeps a flood of links from taking over Runesmith: past three links in a minute, each new one replaces the page and says how many
/// came before it.</summary>
public sealed class LinkThrottle(TimeProvider time)
{
    public const int Allowed = 3;

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly Queue<DateTimeOffset> arrivals = new();

    /// <summary>Records a link and says how many earlier links of the last minute it replaces; zero while within the limit.</summary>
    public int Arrive()
    {
        var now = time.GetUtcNow();
        while (arrivals.Count > 0 && now - arrivals.Peek() >= Window)
            arrivals.Dequeue();

        arrivals.Enqueue(now);
        return arrivals.Count > Allowed ? arrivals.Count - 1 : 0;
    }
}
