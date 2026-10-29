using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Vaxel;

/// <summary>
/// Default implementation of <see cref="IPushChannel"/> publishing messages to <see cref="IPushBackplane"/>
/// and executing per-recipient <see cref="IPushPipeline{T}"/> streams.
/// </summary>
public sealed class PushChannel : IPushChannel
{
    private readonly IPushBackplane _backplane;
    private readonly PushConnectionRegistry? _registry;
    private readonly IFragmentComposerFactory? _composerFactory;
    private readonly VaxelOptions _options;
    private readonly IVaxelScheduler _scheduler;

    public PushChannel(IPushBackplane backplane)
        : this(backplane, null, null, null, null)
    {
    }

    [ActivatorUtilitiesConstructor]
    public PushChannel(
        IPushBackplane backplane,
        PushConnectionRegistry? registry = null,
        IFragmentComposerFactory? composerFactory = null,
        IOptions<VaxelOptions>? options = null,
        IVaxelScheduler? scheduler = null)
    {
        ArgumentNullException.ThrowIfNull(backplane);
        _backplane = backplane;
        _registry = registry;
        _composerFactory = composerFactory;
        _options = options?.Value ?? new VaxelOptions();
        _scheduler = scheduler ?? SystemScheduler.Instance;
    }

    public IPushPipeline<T> From<T>(IAsyncEnumerable<T> source, IVaxelScheduler? scheduler = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new PushPipeline<T>(
            source,
            scheduler ?? _scheduler,
            _backplane,
            _registry,
            _composerFactory,
            _options);
    }

    public Task PushAsync(PushScope scope, PatchBuilder patch, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(patch);

        var doc = patch.ToHtml();
        return _backplane.PublishAsync(scope, "vx-patch", doc, ct);
    }

    public Task PushAsync(PushScope scope, string rawPatchHtml, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(rawPatchHtml);

        return _backplane.PublishAsync(scope, "vx-patch", rawPatchHtml, ct);
    }

    public Task PushReloadAsync(PushScope scope, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return _backplane.PublishAsync(scope, "vx-reload", "1", ct);
    }
}
