namespace Vaxel;

/// <summary>
/// Clock and delay abstraction for server-side push pipelines (P3: v2.0-push-pipelines).
/// Allows time operators (<c>Debounce</c>, <c>Throttle</c>, <c>Delay</c>, <c>Sample</c>, <c>Buffer</c>)
/// to be tested deterministically against the shared marble corpus without wall-clock sleeps.
/// </summary>
public interface IVaxelScheduler
{
    /// <summary>
    /// Current scheduler timestamp.
    /// </summary>
    DateTimeOffset Now { get; }

    /// <summary>
    /// Waits for <paramref name="dueTime"/> on this scheduler's clock.
    /// </summary>
    Task Delay(TimeSpan dueTime, CancellationToken cancellationToken = default);
}

/// <summary>
/// Production wall-clock scheduler backed by <see cref="DateTimeOffset.UtcNow"/> and <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.
/// </summary>
public sealed class SystemScheduler : IVaxelScheduler
{
    public static SystemScheduler Instance { get; } = new();

    public DateTimeOffset Now => DateTimeOffset.UtcNow;

    public Task Delay(TimeSpan dueTime, CancellationToken cancellationToken = default)
        => Task.Delay(dueTime, cancellationToken);
}

/// <summary>
/// Deterministic virtual-time scheduler for marble conformance and unit testing.
/// </summary>
public sealed class VirtualScheduler : IVaxelScheduler
{
    private readonly Lock _lock = new();
    private readonly List<ScheduledTimer> _timers = [];
    private long _nextSeq;
    private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public DateTimeOffset Now
    {
        get
        {
            lock (_lock)
            {
                return _now;
            }
        }
    }

    public int ActiveTimerCount
    {
        get
        {
            lock (_lock)
            {
                return _timers.Count;
            }
        }
    }

    public Task Delay(TimeSpan dueTime, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        if (dueTime <= TimeSpan.Zero)
        {
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource();
        ScheduledTimer timer;

        lock (_lock)
        {
            timer = new ScheduledTimer(_now + dueTime, ++_nextSeq, tcs);
            _timers.Add(timer);
        }

        if (cancellationToken.CanBeCanceled)
        {
            timer.Registration = cancellationToken.Register(() =>
            {
                bool removed;
                lock (_lock)
                {
                    removed = _timers.Remove(timer);
                }

                if (removed)
                {
                    tcs.TrySetCanceled(cancellationToken);
                }
            });
        }

        return tcs.Task;
    }

    /// <summary>
    /// Advances virtual time by <paramref name="delta"/>, firing all timers due at or before the target time in chronological order.
    /// </summary>
    public async Task AdvanceByAsync(TimeSpan delta)
    {
        await SettleAsync();

        DateTimeOffset target;
        lock (_lock)
        {
            target = _now + delta;
        }

        while (true)
        {
            await SettleAsync();

            ScheduledTimer? next = null;
            lock (_lock)
            {
                for (var i = 0; i < _timers.Count; i++)
                {
                    var candidate = _timers[i];
                    if (candidate.DueAt <= target &&
                        (next is null || candidate.DueAt < next.DueAt || (candidate.DueAt == next.DueAt && candidate.Seq < next.Seq)))
                    {
                        next = candidate;
                    }
                }

                if (next is not null)
                {
                    _timers.Remove(next);
                    _now = next.DueAt;
                }
                else
                {
                    _now = target;
                }
            }

            if (next is null)
            {
                await SettleAsync();
                break;
            }

            next.Registration.Dispose();
            next.Tcs.TrySetResult();
            await SettleAsync();
        }
    }

    /// <summary>
    /// Yields enough turns for background async state machines to reach their next await point.
    /// </summary>
    public static async Task SettleAsync(int turns = 8)
    {
        for (var i = 0; i < turns; i++)
        {
            await Task.Yield();
        }
    }

    private sealed class ScheduledTimer(DateTimeOffset dueAt, long seq, TaskCompletionSource tcs)
    {
        public DateTimeOffset DueAt { get; } = dueAt;
        public long Seq { get; } = seq;
        public TaskCompletionSource Tcs { get; } = tcs;
        public CancellationTokenRegistration Registration { get; set; }
    }
}
