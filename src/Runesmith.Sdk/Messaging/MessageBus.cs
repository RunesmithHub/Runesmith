using System.Composition;

namespace Runesmith.Sdk.Messaging;

/// <summary>The message bus Runesmith shares with every part.</summary>
[Export(typeof(IMessageBus))]
[Shared]
public sealed class MessageBus : IMessageBus
{
    private readonly Lock gate = new();
    // Publishing reads the array once, so subscribing or unsubscribing inside a handler is safe.
    private volatile Subscriber[] subscribers = [];

    /// <summary>Raised when a subscriber throws, with the exception; the message still reaches the other subscribers.</summary>
    public event Action<Exception>? SubscriberFailed;

    public void Publish<TMessage>(TMessage message)
        where TMessage : class
    {
        ArgumentNullException.ThrowIfNull(message);
        foreach (var subscriber in subscribers)
        {
            if (!subscriber.MessageType.IsInstanceOfType(message))
                continue;

            try
            {
                subscriber.Handler(message);
            }
            catch (Exception exception)
            {
                SubscriberFailed?.Invoke(exception);
            }
        }
    }

    public IDisposable Subscribe<TMessage>(Action<TMessage> handler)
        where TMessage : class
    {
        ArgumentNullException.ThrowIfNull(handler);
        var subscriber = new Subscriber(typeof(TMessage), message => handler((TMessage)message));
        lock (gate)
            subscribers = [.. subscribers, subscriber];
        return new Subscription(this, subscriber);
    }

    private void Unsubscribe(Subscriber subscriber)
    {
        lock (gate)
            subscribers = [.. subscribers.Where(s => s != subscriber)];
    }

    private sealed record Subscriber(Type MessageType, Action<object> Handler);

    private sealed class Subscription(MessageBus bus, Subscriber subscriber) : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
                bus.Unsubscribe(subscriber);
        }
    }
}
