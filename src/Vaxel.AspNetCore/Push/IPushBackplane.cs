namespace Vaxel;

/// <summary>
/// Abstraction for multi-node server push fanout.
/// </summary>
public interface IPushBackplane
{
    /// <summary>
    /// Publishes an event to the backplane for fanout to connected nodes.
    /// </summary>
    Task PublishAsync(PushScope scope, string eventName, string data, CancellationToken ct = default);

    /// <summary>
    /// Subscribes the local node to backplane messages.
    /// </summary>
    void Subscribe(Func<PushScope, string, string, Task> onMessage);
}
