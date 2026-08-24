namespace Vaxel;

/// <summary>
/// Default in-process implementation of <see cref="IPushBackplane"/>.
/// </summary>
public sealed class InProcessPushBackplane : IPushBackplane
{
    private readonly List<Func<PushScope, string, string, Task>> _handlers = [];
    private readonly Lock _lock = new();

    public Task PublishAsync(PushScope scope, string eventName, string data, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(eventName);

        List<Func<PushScope, string, string, Task>> handlers;
        lock (_lock)
        {
            handlers = [.. _handlers];
        }

        if (handlers.Count == 0) return Task.CompletedTask;

        var tasks = handlers.Select(h => h(scope, eventName, data));
        return Task.WhenAll(tasks);
    }

    public void Subscribe(Func<PushScope, string, string, Task> onMessage)
    {
        ArgumentNullException.ThrowIfNull(onMessage);
        lock (_lock)
        {
            _handlers.Add(onMessage);
        }
    }
}
