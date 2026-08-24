namespace Vaxel;

/// <summary>
/// Default implementation of <see cref="IPushChannel"/> publishing messages to <see cref="IPushBackplane"/>.
/// </summary>
public sealed class PushChannel : IPushChannel
{
    private readonly IPushBackplane _backplane;

    public PushChannel(IPushBackplane backplane)
    {
        ArgumentNullException.ThrowIfNull(backplane);
        _backplane = backplane;
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
