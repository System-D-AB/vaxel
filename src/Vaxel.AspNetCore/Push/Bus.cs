using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;

namespace Vaxel;

/// <summary>
/// Overflow drop policy for bounded <see cref="Bus{T}"/> subscriber channels.
/// </summary>
public enum BusDropPolicy
{
    /// <summary>
    /// Drops the oldest buffered item in the subscriber's queue so the subscriber sees the newest state (default).
    /// </summary>
    DropOldest = 0,

    /// <summary>
    /// Drops the newest buffered item in the subscriber's queue.
    /// </summary>
    DropNewest = 1,

    /// <summary>
    /// Drops the incoming item being written when the subscriber's queue is full.
    /// </summary>
    DropWrite = 2,
}

/// <summary>
/// Server-wide typed message subject with bounded per-subscriber fan-out and optional multi-node <see cref="IPushBackplane"/> transport.
/// Publishers never hold references to subscribers; when zero subscribers are listening, published events are dropped immediately.
/// </summary>
public interface IBus<T>
{
    /// <summary>
    /// Publishes <paramref name="item"/> to all active subscribers (and across <see cref="IPushBackplane"/> when configured).
    /// </summary>
    void Publish(T item);

    /// <summary>
    /// Asynchronously publishes <paramref name="item"/> to all active subscribers and awaits <see cref="IPushBackplane"/> propagation.
    /// </summary>
    Task PublishAsync(T item, CancellationToken ct = default);

    /// <summary>
    /// Subscribes to future events published on this bus until the enumeration is disposed or cancelled.
    /// </summary>
    IAsyncEnumerable<T> Subscribe(CancellationToken ct = default);

    /// <summary>
    /// Number of currently active subscriber streams on this node.
    /// </summary>
    int SubscriberCount { get; }
}

/// <summary>
/// Default bounded-<see cref="Channel{T}"/> implementation of <see cref="IBus{T}"/>.
/// </summary>
public sealed class Bus<T> : IBus<T>
{
    private readonly ConcurrentDictionary<long, Channel<T>> _subscribers = new();
    private readonly IPushBackplane? _backplane;
    private readonly int _capacity;
    private readonly BusDropPolicy _dropPolicy;
    private readonly string _nodeId = Guid.NewGuid().ToString("N");
    private readonly string _backplaneEventName = $"vx-bus:{typeof(T).FullName ?? typeof(T).Name}";
    private long _nextSubscriberId;

    public Bus(IPushBackplane? backplane = null, int capacity = 64, BusDropPolicy dropPolicy = BusDropPolicy.DropOldest)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(capacity, 0);
        _backplane = backplane;
        _capacity = capacity;
        _dropPolicy = dropPolicy;

        _backplane?.Subscribe(OnBackplaneMessageAsync);
    }

    public int SubscriberCount => _subscribers.Count;

    public void Publish(T item)
    {
        FanOutLocal(item);

        if (_backplane is not null and not InProcessPushBackplane)
        {
            _ = PublishToBackplaneAsync(item, CancellationToken.None);
        }
        else if (_backplane is InProcessPushBackplane)
        {
            // Support multi-instance Bus<T> tests sharing an InProcessPushBackplane
            _ = PublishToBackplaneAsync(item, CancellationToken.None);
        }
    }

    public async Task PublishAsync(T item, CancellationToken ct = default)
    {
        FanOutLocal(item);

        if (_backplane is not null)
        {
            await PublishToBackplaneAsync(item, ct);
        }
    }

    public async IAsyncEnumerable<T> Subscribe([EnumeratorCancellation] CancellationToken ct = default)
    {
        var fullMode = _dropPolicy switch
        {
            BusDropPolicy.DropOldest => BoundedChannelFullMode.DropOldest,
            BusDropPolicy.DropNewest => BoundedChannelFullMode.DropNewest,
            BusDropPolicy.DropWrite => BoundedChannelFullMode.DropWrite,
            _ => BoundedChannelFullMode.DropOldest,
        };

        var channel = Channel.CreateBounded<T>(new BoundedChannelOptions(_capacity)
        {
            FullMode = fullMode,
            SingleReader = true,
            SingleWriter = false,
        });

        var id = Interlocked.Increment(ref _nextSubscriberId);
        _subscribers[id] = channel;

        try
        {
            await foreach (var item in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                yield return item;
            }
        }
        finally
        {
            _subscribers.TryRemove(id, out _);
            channel.Writer.TryComplete();
        }
    }

    private void FanOutLocal(T item)
    {
        if (_subscribers.IsEmpty)
        {
            // US-3 AC-3: When no pipeline subscribes, drop published events without buffering.
            return;
        }

        foreach (var subscriber in _subscribers.Values)
        {
            subscriber.Writer.TryWrite(item);
        }
    }

    private Task PublishToBackplaneAsync(T item, CancellationToken ct)
    {
        if (_backplane is null)
        {
            return Task.CompletedTask;
        }

        try
        {
            var envelope = new BackplaneEnvelope(_nodeId, item);
            var json = JsonSerializer.Serialize(envelope);
            return _backplane.PublishAsync(PushScope.Broadcast(), _backplaneEventName, json, ct);
        }
        catch
        {
            return Task.CompletedTask;
        }
    }

    private Task OnBackplaneMessageAsync(PushScope scope, string eventName, string data)
    {
        if (!string.Equals(eventName, _backplaneEventName, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        try
        {
            var envelope = JsonSerializer.Deserialize<BackplaneEnvelope>(data);
            if (envelope is not null && !string.Equals(envelope.NodeId, _nodeId, StringComparison.Ordinal) && envelope.Payload is not null)
            {
                FanOutLocal(envelope.Payload);
            }
        }
        catch
        {
            // Ignore malformed backplane payloads
        }

        return Task.CompletedTask;
    }

    private sealed record BackplaneEnvelope(string NodeId, T? Payload);
}
