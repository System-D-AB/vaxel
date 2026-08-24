namespace Vaxel;

/// <summary>
/// Server push channel for sending real-time patch documents to active client streams.
/// </summary>
public interface IPushChannel
{
    /// <summary>
    /// Pushes a patch document to the specified recipient scope.
    /// </summary>
    Task PushAsync(PushScope scope, PatchBuilder patch, CancellationToken ct = default);

    /// <summary>
    /// Pushes raw patch document HTML to the specified recipient scope.
    /// </summary>
    Task PushAsync(PushScope scope, string rawPatchHtml, CancellationToken ct = default);

    /// <summary>
    /// Emits a reload directive instructing connected clients to reload the full page.
    /// </summary>
    Task PushReloadAsync(PushScope scope, CancellationToken ct = default);
}
