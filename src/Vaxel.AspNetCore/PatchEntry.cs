using Microsoft.AspNetCore.Html;

namespace Vaxel;

/// <summary>
/// Represents an individual patch operation within a patch document.
/// </summary>
public sealed class PatchEntry
{
    public required string Target { get; set; }
    public SwapMode Mode { get; set; }
    public IHtmlContent? Content { get; set; }
    public VaxelNamespace Namespace { get; set; } = VaxelNamespace.Html;
    public string? Transition { get; set; }
}
