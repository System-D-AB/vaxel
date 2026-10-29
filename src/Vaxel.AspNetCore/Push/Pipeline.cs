using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Vaxel;

/// <summary>
/// Exception thrown (and reported via <see cref="VaxelPushOptions.OnPipelineError"/>) when a push pipeline's source faults.
/// Isolates the fault to the single pipeline without affecting other pipelines or HTTP requests (US-2 AC-3).
/// </summary>
public sealed class VaxelPushPipelineException : Exception
{
    public PushScope Scope { get; }

    public VaxelPushPipelineException(PushScope scope, string message, Exception innerException)
        : base(message, innerException)
    {
        Scope = scope;
    }
}

/// <summary>
/// Fluent push pipeline over <see cref="IAsyncEnumerable{T}"/> ending in per-recipient <see cref="PatchBuilder"/> pushes.
/// </summary>
public interface IPushPipeline<T>
{
    IAsyncEnumerable<T> Source { get; }
    IVaxelScheduler Scheduler { get; }

    IPushPipeline<T> Debounce(TimeSpan window);
    IPushPipeline<T> Throttle(TimeSpan window);
    IPushPipeline<T> Delay(TimeSpan duration);
    IPushPipeline<T> Once();
    IPushPipeline<T> Sample(TimeSpan interval);
    IPushPipeline<IReadOnlyList<T>> Buffer(TimeSpan window);
    IPushPipeline<T> DistinctUntilChanged();
    IPushPipeline<T> DistinctUntilChanged<TKey>(Func<T, TKey> keySelector, IEqualityComparer<TKey>? comparer = null);
    IPushPipeline<T> Where(Func<T, bool> predicate);
    IPushPipeline<T> Merge(params IAsyncEnumerable<T>[] others);
    IPushPipeline<T> TakeUntil<TSignal>(IAsyncEnumerable<TSignal> stopSignal);
    IPushPipeline<T> TakeUntil(CancellationToken stopToken);
    IPushPipeline<TOut> Select<TOut>(Func<T, TOut> selector);
    IPushPipeline<TOut> Select<TOut>(Func<T, CancellationToken, ValueTask<TOut>> selector);
    IPushPipeline<PatchBuilder> Select(Func<T, IFragmentComposer, CancellationToken, Task<PatchBuilder>> perRecipientSelector);
    IPushPipeline<PatchBuilder> Select(Func<T, PushConnection, IFragmentComposer, CancellationToken, Task<PatchBuilder>> perRecipientSelector);
    Task To(PushScope scope, CancellationToken ct = default);
}

internal sealed class PushPipeline<T>(
    IAsyncEnumerable<T> source,
    IVaxelScheduler scheduler,
    IPushBackplane? backplane = null,
    PushConnectionRegistry? registry = null,
    IFragmentComposerFactory? composerFactory = null,
    VaxelOptions? options = null,
    Func<PushScope, CancellationToken, Task>? perRecipientRunner = null) : IPushPipeline<T>
{
    private readonly VaxelOptions _options = options ?? new VaxelOptions();

    public IAsyncEnumerable<T> Source { get; } = source;
    public IVaxelScheduler Scheduler { get; } = scheduler;

    private PushPipeline<TNext> With<TNext>(
        IAsyncEnumerable<TNext> nextSource,
        Func<PushScope, CancellationToken, Task>? customRunner = null) =>
        new(nextSource, Scheduler, backplane, registry, composerFactory, _options, customRunner);

    public IPushPipeline<T> Debounce(TimeSpan window)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(window, TimeSpan.Zero);
        return With(DebounceCore(Source, window, Scheduler));
    }

    public IPushPipeline<T> Throttle(TimeSpan window)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(window, TimeSpan.Zero);
        return With(ThrottleCore(Source, window, Scheduler));
    }

    public IPushPipeline<T> Delay(TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);
        return With(DelayCore(Source, duration, Scheduler));
    }

    public IPushPipeline<T> Once() =>
        With(OnceCore(Source));

    public IPushPipeline<T> Sample(TimeSpan interval)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
        return With(SampleCore(Source, interval, Scheduler));
    }

    public IPushPipeline<IReadOnlyList<T>> Buffer(TimeSpan window)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);
        return With(BufferCore(Source, window, Scheduler));
    }

    public IPushPipeline<T> DistinctUntilChanged() =>
        DistinctUntilChanged(static x => x, EqualityComparer<T>.Default);

    public IPushPipeline<T> DistinctUntilChanged<TKey>(Func<T, TKey> keySelector, IEqualityComparer<TKey>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        return With(DistinctUntilChangedCore(Source, keySelector, comparer ?? EqualityComparer<TKey>.Default));
    }

    public IPushPipeline<T> Where(Func<T, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return With(WhereCore(Source, predicate));
    }

    public IPushPipeline<T> Merge(params IAsyncEnumerable<T>[] others)
    {
        ArgumentNullException.ThrowIfNull(others);
        return With(MergeCore([Source, .. others]));
    }

    public IPushPipeline<T> TakeUntil<TSignal>(IAsyncEnumerable<TSignal> stopSignal)
    {
        ArgumentNullException.ThrowIfNull(stopSignal);
        return With(TakeUntilCore(Source, stopSignal));
    }

    public IPushPipeline<T> TakeUntil(CancellationToken stopToken) =>
        With(TakeUntilTokenCore(Source, stopToken));

    public IPushPipeline<TOut> Select<TOut>(Func<T, TOut> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);

        if (typeof(TOut) == typeof(PatchBuilder))
        {
            var typedSelector = (Func<T, PatchBuilder>)(object)selector;
            var mapped = SelectCore(Source, (item, _) => ValueTask.FromResult(typedSelector(item)));
            var runner = CreatePerRecipientRunner((item, _, _, _) => Task.FromResult(typedSelector(item)));
            return (IPushPipeline<TOut>)(object)With(mapped, runner);
        }

        return With(SelectCore(Source, (item, _) => ValueTask.FromResult(selector(item))));
    }

    public IPushPipeline<TOut> Select<TOut>(Func<T, CancellationToken, ValueTask<TOut>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);

        if (typeof(TOut) == typeof(PatchBuilder))
        {
            var typedSelector = (Func<T, CancellationToken, ValueTask<PatchBuilder>>)(object)selector;
            var mapped = SelectCore(Source, typedSelector);
            var runner = CreatePerRecipientRunner((item, _, _, token) => typedSelector(item, token).AsTask());
            return (IPushPipeline<TOut>)(object)With(mapped, runner);
        }

        return With(SelectCore(Source, selector));
    }

    public IPushPipeline<PatchBuilder> Select(Func<T, IFragmentComposer, CancellationToken, Task<PatchBuilder>> perRecipientSelector)
    {
        ArgumentNullException.ThrowIfNull(perRecipientSelector);
        return Select((item, _, composer, token) => perRecipientSelector(item, composer, token));
    }

    public IPushPipeline<PatchBuilder> Select(Func<T, PushConnection, IFragmentComposer, CancellationToken, Task<PatchBuilder>> perRecipientSelector)
    {
        ArgumentNullException.ThrowIfNull(perRecipientSelector);

        var fallbackStream = SelectCore(Source, async (item, token) =>
        {
            var bgComposer = composerFactory?.CreateBackground() ?? NullBackgroundComposer.Instance;
            using var _ = bgComposer as IDisposable;
            var placeholderConn = new PushConnection("bg", null, new HashSet<string>(), Channel.CreateUnbounded<PushMessage>());
            return await perRecipientSelector(item, placeholderConn, bgComposer, token).ConfigureAwait(false);
        });

        var runner = CreatePerRecipientRunner(perRecipientSelector);
        return With(fallbackStream, runner);
    }

    public async Task To(PushScope scope, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        if (perRecipientRunner is not null)
        {
            await perRecipientRunner(scope, ct).ConfigureAwait(false);
            return;
        }

        if (typeof(T) != typeof(PatchBuilder))
        {
            throw new InvalidOperationException(
                $"Push pipeline must project items to '{nameof(PatchBuilder)}' via .Select(...) before calling .To(scope, ct).");
        }

        var patchSource = (IAsyncEnumerable<PatchBuilder>)(object)Source;
        var directRunner = CreateDirectPatchRunner(patchSource);
        await directRunner(scope, ct).ConfigureAwait(false);
    }

    private Func<PushScope, CancellationToken, Task> CreatePerRecipientRunner(
        Func<T, PushConnection, IFragmentComposer, CancellationToken, Task<PatchBuilder>> renderForRecipient)
    {
        return async (scope, outerCt) =>
        {
            await ExecuteWithLifetimeAsync(scope, outerCt, async token =>
            {
                await foreach (var item in Source.WithCancellation(token).ConfigureAwait(false))
                {
                    if (registry is not null)
                    {
                        var recipients = registry.GetConnections(scope);
                        foreach (var recipient in recipients)
                        {
                            var bgComposer = composerFactory?.CreateBackground() ?? NullBackgroundComposer.Instance;
                            using var _ = bgComposer as IDisposable;
                            var patch = await renderForRecipient(item, recipient, bgComposer, token).ConfigureAwait(false);
                            var html = patch.ToHtml();
                            recipient.Channel.Writer.TryWrite(new PushMessage("vx-patch", html));
                        }

                        if (recipients.Count == 0 && backplane is not null and not InProcessPushBackplane)
                        {
                            var bgComposer = composerFactory?.CreateBackground() ?? NullBackgroundComposer.Instance;
                            using var _ = bgComposer as IDisposable;
                            var dummyConn = new PushConnection("backplane", null, new HashSet<string>(), Channel.CreateUnbounded<PushMessage>());
                            var patch = await renderForRecipient(item, dummyConn, bgComposer, token).ConfigureAwait(false);
                            await backplane.PublishAsync(scope, "vx-patch", patch.ToHtml(), token).ConfigureAwait(false);
                        }
                    }
                    else if (backplane is not null)
                    {
                        var bgComposer = composerFactory?.CreateBackground() ?? NullBackgroundComposer.Instance;
                        using var _ = bgComposer as IDisposable;
                        var dummyConn = new PushConnection("backplane", null, new HashSet<string>(), Channel.CreateUnbounded<PushMessage>());
                        var patch = await renderForRecipient(item, dummyConn, bgComposer, token).ConfigureAwait(false);
                        await backplane.PublishAsync(scope, "vx-patch", patch.ToHtml(), token).ConfigureAwait(false);
                    }
                }
            }).ConfigureAwait(false);
        };
    }

    private Func<PushScope, CancellationToken, Task> CreateDirectPatchRunner(IAsyncEnumerable<PatchBuilder> patchSource)
    {
        return async (scope, outerCt) =>
        {
            await ExecuteWithLifetimeAsync(scope, outerCt, async token =>
            {
                await foreach (var patch in patchSource.WithCancellation(token).ConfigureAwait(false))
                {
                    var html = patch.ToHtml();
                    if (registry is not null)
                    {
                        var recipients = registry.GetConnections(scope);
                        foreach (var recipient in recipients)
                        {
                            recipient.Channel.Writer.TryWrite(new PushMessage("vx-patch", html));
                        }
                    }
                    else if (backplane is not null)
                    {
                        await backplane.PublishAsync(scope, "vx-patch", html, token).ConfigureAwait(false);
                    }
                }
            }).ConfigureAwait(false);
        };
    }

    private async Task ExecuteWithLifetimeAsync(
        PushScope scope,
        CancellationToken outerCt,
        Func<CancellationToken, Task> body)
    {
        using var lifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(outerCt);
        var lockObj = new Lock();
        var hadConnection = registry?.HasConnections(scope) == true;
        CancellationTokenSource? idleCts = null;
        var stoppedByLifetime = false;

        void EvaluateConnections()
        {
            if (registry is null)
            {
                return;
            }

            lock (lockObj)
            {
                if (lifetimeCts.IsCancellationRequested)
                {
                    return;
                }

                var hasNow = registry.HasConnections(scope);
                if (hasNow)
                {
                    hadConnection = true;
                    if (idleCts is not null)
                    {
                        try { idleCts.Cancel(); } catch { }
                        idleCts.Dispose();
                        idleCts = null;
                    }
                }
                else
                {
                    // US-2 AC-1: When a connected single-user recipient closes their SSE connection, stop immediately.
                    if (hadConnection && scope is PushScope.UserScope)
                    {
                        stoppedByLifetime = true;
                        try { lifetimeCts.Cancel(); } catch { }
                        return;
                    }

                    // US-2 AC-2: When a scope has no connections for IdleStopPeriod, stop the pipeline.
                    if (idleCts is null)
                    {
                        var localIdleCts = CancellationTokenSource.CreateLinkedTokenSource(lifetimeCts.Token);
                        idleCts = localIdleCts;
                        var idlePeriod = _options.Push.IdleStopPeriod;
                        _ = WaitIdleAndStopAsync(idlePeriod, localIdleCts.Token);
                    }
                }
            }
        }

        async Task WaitIdleAndStopAsync(TimeSpan period, CancellationToken idleToken)
        {
            try
            {
                await Scheduler.Delay(period, idleToken).ConfigureAwait(false);
                lock (lockObj)
                {
                    if (!idleToken.IsCancellationRequested && registry is not null && !registry.HasConnections(scope))
                    {
                        stoppedByLifetime = true;
                        try { lifetimeCts.Cancel(); } catch { }
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        if (registry is not null)
        {
            registry.ConnectionsChanged += EvaluateConnections;
            EvaluateConnections();
        }

        try
        {
            await body(lifetimeCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppedByLifetime || outerCt.IsCancellationRequested)
        {
            // Clean shutdown triggered by connection close, idle-stop, or caller cancellation token.
        }
        catch (Exception ex) when (ex is not VaxelFragmentContextException)
        {
            var wrapped = new VaxelPushPipelineException(
                scope,
                $"Push pipeline for scope '{scope}' faulted: {ex.Message}",
                ex);
            _options.Push.OnPipelineError?.Invoke(scope, wrapped);
            throw wrapped;
        }
        finally
        {
            if (registry is not null)
            {
                registry.ConnectionsChanged -= EvaluateConnections;
            }

            lock (lockObj)
            {
                if (idleCts is not null)
                {
                    try { idleCts.Cancel(); } catch { }
                    idleCts.Dispose();
                    idleCts = null;
                }
            }
        }
    }

    // --- Stream Operators over IAsyncEnumerable<T> + IVaxelScheduler ---

    private static async IAsyncEnumerable<T> DistinctUntilChangedCore<TKey>(
        IAsyncEnumerable<T> source,
        Func<T, TKey> keySelector,
        IEqualityComparer<TKey> comparer,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var hasPrev = false;
        TKey? prevKey = default;

        await foreach (var item in source.WithCancellation(ct).ConfigureAwait(false))
        {
            var key = keySelector(item);
            if (!hasPrev || !comparer.Equals(prevKey!, key))
            {
                hasPrev = true;
                prevKey = key;
                yield return item;
            }
        }
    }

    private static async IAsyncEnumerable<T> WhereCore(
        IAsyncEnumerable<T> source,
        Func<T, bool> predicate,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var item in source.WithCancellation(ct).ConfigureAwait(false))
        {
            if (predicate(item))
            {
                yield return item;
            }
        }
    }

    private static async IAsyncEnumerable<TOut> SelectCore<TOut>(
        IAsyncEnumerable<T> source,
        Func<T, CancellationToken, ValueTask<TOut>> selector,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var item in source.WithCancellation(ct).ConfigureAwait(false))
        {
            yield return await selector(item, ct).ConfigureAwait(false);
        }
    }

    private static async IAsyncEnumerable<T> OnceCore(
        IAsyncEnumerable<T> source,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var emitted = false;
        await foreach (var item in source.WithCancellation(ct).ConfigureAwait(false))
        {
            if (!emitted)
            {
                emitted = true;
                yield return item;
            }
        }
    }

    private static async IAsyncEnumerable<T> DebounceCore(
        IAsyncEnumerable<T> source,
        TimeSpan window,
        IVaxelScheduler scheduler,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var channel = CreateOperatorChannel<T>();
        using var pumpCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var pumpTask = PumpDebounceAsync(source, window, scheduler, channel.Writer, pumpCts.Token);

        await foreach (var item in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            yield return item;
        }

        await pumpTask.ConfigureAwait(false);
    }

    private static async Task PumpDebounceAsync(
        IAsyncEnumerable<T> source,
        TimeSpan window,
        IVaxelScheduler scheduler,
        ChannelWriter<T> writer,
        CancellationToken ct)
    {
        CancellationTokenSource? timerCts = null;
        Task? activeTimer = null;

        try
        {
            await foreach (var item in source.WithCancellation(ct).ConfigureAwait(false))
            {
                if (timerCts is not null)
                {
                    timerCts.Cancel();
                    timerCts.Dispose();
                }

                var currentTimerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timerCts = currentTimerCts;
                var captured = item;

                activeTimer = RunDebounceTimerAsync(captured, window, scheduler, writer, currentTimerCts.Token);
            }

            if (activeTimer is not null)
            {
                await activeTimer.ConfigureAwait(false);
            }

            writer.TryComplete();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            writer.TryComplete();
        }
        catch (Exception ex)
        {
            writer.TryComplete(ex);
        }
        finally
        {
            timerCts?.Dispose();
        }
    }

    private static async Task RunDebounceTimerAsync(
        T value,
        TimeSpan window,
        IVaxelScheduler scheduler,
        ChannelWriter<T> writer,
        CancellationToken timerCt)
    {
        try
        {
            await scheduler.Delay(window, timerCt).ConfigureAwait(false);
            if (!timerCt.IsCancellationRequested)
            {
                writer.TryWrite(value);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static async IAsyncEnumerable<T> ThrottleCore(
        IAsyncEnumerable<T> source,
        TimeSpan window,
        IVaxelScheduler scheduler,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var channel = CreateOperatorChannel<T>();
        using var pumpCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var pumpTask = PumpThrottleAsync(source, window, scheduler, channel.Writer, pumpCts.Token);

        await foreach (var item in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            yield return item;
        }

        await pumpTask.ConfigureAwait(false);
    }

    private static async Task PumpThrottleAsync(
        IAsyncEnumerable<T> source,
        TimeSpan window,
        IVaxelScheduler scheduler,
        ChannelWriter<T> writer,
        CancellationToken ct)
    {
        var gate = new Lock();
        var locked = false;

        async Task UnlockAfterWindowAsync()
        {
            try
            {
                await scheduler.Delay(window, ct).ConfigureAwait(false);
                lock (gate)
                {
                    locked = false;
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        try
        {
            await foreach (var item in source.WithCancellation(ct).ConfigureAwait(false))
            {
                var shouldEmit = false;
                lock (gate)
                {
                    if (!locked)
                    {
                        locked = true;
                        shouldEmit = true;
                    }
                }

                if (shouldEmit)
                {
                    _ = UnlockAfterWindowAsync();
                    writer.TryWrite(item);
                }
            }

            writer.TryComplete();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            writer.TryComplete();
        }
        catch (Exception ex)
        {
            writer.TryComplete(ex);
        }
    }

    private static async IAsyncEnumerable<T> DelayCore(
        IAsyncEnumerable<T> source,
        TimeSpan duration,
        IVaxelScheduler scheduler,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var channel = CreateOperatorChannel<T>();
        using var pumpCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var pumpTask = PumpDelayAsync(source, duration, scheduler, channel.Writer, pumpCts.Token);

        await foreach (var item in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            yield return item;
        }

        await pumpTask.ConfigureAwait(false);
    }

    private static async Task PumpDelayAsync(
        IAsyncEnumerable<T> source,
        TimeSpan duration,
        IVaxelScheduler scheduler,
        ChannelWriter<T> writer,
        CancellationToken ct)
    {
        var pending = new List<Task>();

        async Task DelayOneAsync(T val)
        {
            try
            {
                await scheduler.Delay(duration, ct).ConfigureAwait(false);
                if (!ct.IsCancellationRequested)
                {
                    writer.TryWrite(val);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        try
        {
            await foreach (var item in source.WithCancellation(ct).ConfigureAwait(false))
            {
                pending.Add(DelayOneAsync(item));
            }

            await Task.WhenAll(pending).ConfigureAwait(false);
            writer.TryComplete();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            writer.TryComplete();
        }
        catch (Exception ex)
        {
            writer.TryComplete(ex);
        }
    }

    private static async IAsyncEnumerable<T> SampleCore(
        IAsyncEnumerable<T> source,
        TimeSpan interval,
        IVaxelScheduler scheduler,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var channel = CreateOperatorChannel<T>();
        using var pumpCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var pumpTask = PumpSampleAsync(source, interval, scheduler, channel.Writer, pumpCts.Token);

        await foreach (var item in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            yield return item;
        }

        await pumpTask.ConfigureAwait(false);
    }

    private static async Task PumpSampleAsync(
        IAsyncEnumerable<T> source,
        TimeSpan interval,
        IVaxelScheduler scheduler,
        ChannelWriter<T> writer,
        CancellationToken ct)
    {
        var gate = new Lock();
        var hasLatest = false;
        T? latest = default;
        using var loopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        async Task SampleLoopAsync()
        {
            try
            {
                while (!loopCts.Token.IsCancellationRequested)
                {
                    await scheduler.Delay(interval, loopCts.Token).ConfigureAwait(false);
                    var emit = false;
                    T? val = default;

                    lock (gate)
                    {
                        if (hasLatest)
                        {
                            emit = true;
                            val = latest;
                            hasLatest = false;
                        }
                    }

                    if (emit)
                    {
                        writer.TryWrite(val!);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        var samplerTask = SampleLoopAsync();

        try
        {
            await foreach (var item in source.WithCancellation(ct).ConfigureAwait(false))
            {
                lock (gate)
                {
                    latest = item;
                    hasLatest = true;
                }
            }

            loopCts.Cancel();
            await samplerTask.ConfigureAwait(false);
            writer.TryComplete();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            loopCts.Cancel();
            writer.TryComplete();
        }
        catch (Exception ex)
        {
            loopCts.Cancel();
            writer.TryComplete(ex);
        }
    }

    private static async IAsyncEnumerable<IReadOnlyList<T>> BufferCore(
        IAsyncEnumerable<T> source,
        TimeSpan window,
        IVaxelScheduler scheduler,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var channel = CreateOperatorChannel<IReadOnlyList<T>>();
        using var pumpCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var pumpTask = PumpBufferAsync(source, window, scheduler, channel.Writer, pumpCts.Token);

        await foreach (var batch in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            yield return batch;
        }

        await pumpTask.ConfigureAwait(false);
    }

    private static async Task PumpBufferAsync(
        IAsyncEnumerable<T> source,
        TimeSpan window,
        IVaxelScheduler scheduler,
        ChannelWriter<IReadOnlyList<T>> writer,
        CancellationToken ct)
    {
        var gate = new Lock();
        var buffer = new List<T>();
        using var loopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        async Task FlushLoopAsync()
        {
            try
            {
                while (!loopCts.Token.IsCancellationRequested)
                {
                    await scheduler.Delay(window, loopCts.Token).ConfigureAwait(false);
                    T[]? batch = null;

                    lock (gate)
                    {
                        if (buffer.Count > 0)
                        {
                            batch = [.. buffer];
                            buffer.Clear();
                        }
                    }

                    if (batch is not null)
                    {
                        writer.TryWrite(batch);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        var flushTask = FlushLoopAsync();

        try
        {
            await foreach (var item in source.WithCancellation(ct).ConfigureAwait(false))
            {
                lock (gate)
                {
                    buffer.Add(item);
                }
            }

            loopCts.Cancel();
            await flushTask.ConfigureAwait(false);
            writer.TryComplete();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            loopCts.Cancel();
            writer.TryComplete();
        }
        catch (Exception ex)
        {
            loopCts.Cancel();
            writer.TryComplete(ex);
        }
    }

    private static async IAsyncEnumerable<T> MergeCore(
        IReadOnlyList<IAsyncEnumerable<T>> sources,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var channel = CreateOperatorChannel<T>();
        using var mergeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        async Task ReadOneAsync(IAsyncEnumerable<T> src)
        {
            await foreach (var item in src.WithCancellation(mergeCts.Token).ConfigureAwait(false))
            {
                channel.Writer.TryWrite(item);
            }
        }

        async Task PumpAllAsync()
        {
            try
            {
                var tasks = sources.Select(ReadOneAsync).ToArray();
                await Task.WhenAll(tasks).ConfigureAwait(false);
                channel.Writer.TryComplete();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                channel.Writer.TryComplete();
            }
            catch (Exception ex)
            {
                mergeCts.Cancel();
                channel.Writer.TryComplete(ex);
            }
        }

        var pumpTask = PumpAllAsync();

        await foreach (var item in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            yield return item;
        }

        await pumpTask.ConfigureAwait(false);
    }

    private static async IAsyncEnumerable<T> TakeUntilCore<TSignal>(
        IAsyncEnumerable<T> source,
        IAsyncEnumerable<TSignal> stopSignal,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        async Task WatchStopAsync()
        {
            try
            {
                await foreach (var _ in stopSignal.WithCancellation(linkedCts.Token).ConfigureAwait(false))
                {
                    linkedCts.Cancel();
                    break;
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        var watchTask = WatchStopAsync();

        var enumerator = source.GetAsyncEnumerator(linkedCts.Token);
        try
        {
            while (true)
            {
                bool moved;
                try
                {
                    moved = await enumerator.MoveNextAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (linkedCts.IsCancellationRequested && !ct.IsCancellationRequested)
                {
                    break;
                }

                if (!moved)
                {
                    break;
                }

                yield return enumerator.Current;
            }
        }
        finally
        {
            linkedCts.Cancel();
            await watchTask.ConfigureAwait(false);
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async IAsyncEnumerable<T> TakeUntilTokenCore(
        IAsyncEnumerable<T> source,
        CancellationToken stopToken,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, stopToken);
        var enumerator = source.GetAsyncEnumerator(linkedCts.Token);
        try
        {
            while (true)
            {
                bool moved;
                try
                {
                    moved = await enumerator.MoveNextAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stopToken.IsCancellationRequested && !ct.IsCancellationRequested)
                {
                    break;
                }

                if (!moved)
                {
                    break;
                }

                yield return enumerator.Current;
            }
        }
        finally
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static Channel<TItem> CreateOperatorChannel<TItem>() =>
        Channel.CreateUnbounded<TItem>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = true,
        });

    private sealed class NullBackgroundComposer : IFragmentComposer, IDisposable
    {
        public static NullBackgroundComposer Instance { get; } = new();

        public Task<Microsoft.AspNetCore.Html.IHtmlContent> PartialAsync(string name, object? model = null) =>
            throw new InvalidOperationException("IFragmentComposerFactory is not configured on this PushChannel instance.");

        public Task<Microsoft.AspNetCore.Html.IHtmlContent> ViewAsync(string name, object? model = null) =>
            throw new InvalidOperationException("IFragmentComposerFactory is not configured on this PushChannel instance.");

        public Task<Microsoft.AspNetCore.Html.IHtmlContent> ComponentAsync<TViewComponent>(object? arguments = null) =>
            throw new InvalidOperationException("IFragmentComposerFactory is not configured on this PushChannel instance.");

        public Task<Microsoft.AspNetCore.Html.IHtmlContent> ComponentAsync(string name, object? arguments = null) =>
            throw new InvalidOperationException("IFragmentComposerFactory is not configured on this PushChannel instance.");

        public Task<Microsoft.AspNetCore.Html.IHtmlContent> RazorComponentAsync<TComponent>(object? parameters = null)
            where TComponent : Microsoft.AspNetCore.Components.IComponent =>
            throw new InvalidOperationException("IFragmentComposerFactory is not configured on this PushChannel instance.");

        public Task<Microsoft.AspNetCore.Html.IHtmlContent> PageAsync(string pagePath, object? model = null) =>
            throw new InvalidOperationException("IFragmentComposerFactory is not configured on this PushChannel instance.");

        public void Dispose()
        {
        }
    }
}
