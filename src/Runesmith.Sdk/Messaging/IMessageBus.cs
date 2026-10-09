namespace Runesmith.Sdk.Messaging;

/// <summary>Passes messages between parts of Runesmith and plugins that do not know each other.</summary>
/// <remarks>A message is any class, such as a record; subscribers receive messages of their type and its subtypes. Messages are delivered on the
/// thread that publishes them, in the order the subscribers subscribed; a subscriber that throws does not stop the others.</remarks>
public interface IMessageBus
{
    void Publish<TMessage>(TMessage message)
        where TMessage : class;

    /// <summary>Calls <paramref name="handler"/> for every message of the type until the returned subscription is disposed.</summary>
    IDisposable Subscribe<TMessage>(Action<TMessage> handler)
        where TMessage : class;
}
