using System.IO;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Http;

namespace Vaxel;

/// <summary>
/// Scoped service representing the current HTTP request's Vaxel protocol state and shell patch queue.
/// </summary>
public interface IVaxelRequest
{
    /// <summary>
    /// True when the request is a Vaxel patch request (<c>VX-Request: 1</c> with a compatible <c>VX-Protocol</c>).
    /// </summary>
    bool IsPatch { get; }

    /// <summary>
    /// True when the request was initiated by browser Back/Forward history restoration (<c>VX-History: restore</c>).
    /// </summary>
    bool IsHistoryRestore { get; }

    /// <summary>
    /// The advisory target selector sent in the <c>VX-Target</c> request header, or <c>null</c>.
    /// </summary>
    string? Target { get; }

    /// <summary>
    /// Secondary <c>&lt;vx-patch&gt;</c> elements queued during the request to be emitted alongside a layout patch.
    /// </summary>
    IReadOnlyList<IHtmlContent> ShellPatches { get; }

    /// <summary>
    /// Queues a secondary <c>&lt;vx-patch&gt;</c> fragment (e.g. header badge, breadcrumbs, notices) to be rendered by the layout.
    /// </summary>
    void AddShellPatch(string target, string html, SwapMode mode = SwapMode.Morph);

    /// <summary>
    /// Queues a secondary <c>&lt;vx-patch&gt;</c> fragment from an <see cref="IHtmlContent"/> to be rendered by the layout.
    /// </summary>
    void AddShellPatch(string target, IHtmlContent html, SwapMode mode = SwapMode.Morph);
}

internal sealed class VaxelRequest(IHttpContextAccessor httpContextAccessor) : IVaxelRequest
{
    private readonly List<IHtmlContent> _shellPatches = [];

    private HttpContext? HttpContext => httpContextAccessor.HttpContext;

    public bool IsPatch => HttpContext is { } ctx && Vaxel.IsVaxelRequest(ctx);

    public bool IsHistoryRestore =>
        HttpContext is { } ctx &&
        ctx.Request.Headers.TryGetValue(VaxelProtocol.HistoryHeader, out var val) &&
        string.Equals(val.ToString().Trim(), "restore", StringComparison.OrdinalIgnoreCase);

    public string? Target =>
        HttpContext is { } ctx &&
        ctx.Request.Headers.TryGetValue(VaxelProtocol.TargetHeader, out var val) &&
        !string.IsNullOrWhiteSpace(val.ToString())
            ? val.ToString().Trim()
            : null;

    public IReadOnlyList<IHtmlContent> ShellPatches => _shellPatches;

    public void AddShellPatch(string target, string html, SwapMode mode = SwapMode.Morph)
    {
        ArgumentNullException.ThrowIfNull(html);
        VaxelTargetException.ThrowIfInvalid(target);
        _shellPatches.Add(new ShellPatchHtmlContent(target, new HtmlString(html), mode));
    }

    public void AddShellPatch(string target, IHtmlContent html, SwapMode mode = SwapMode.Morph)
    {
        ArgumentNullException.ThrowIfNull(html);
        VaxelTargetException.ThrowIfInvalid(target);
        _shellPatches.Add(new ShellPatchHtmlContent(target, html, mode));
    }

    private sealed class ShellPatchHtmlContent(string target, IHtmlContent inner, SwapMode mode) : IHtmlContent
    {
        public void WriteTo(TextWriter writer, HtmlEncoder encoder)
        {
            var modeStr = mode.ToString().ToLowerInvariant();
            writer.Write("<vx-patch target=\"");
            encoder.Encode(writer, target);
            writer.Write("\" mode=\"");
            encoder.Encode(writer, modeStr);
            writer.Write("\">");
            inner.WriteTo(writer, encoder);
            writer.Write("</vx-patch>");
        }
    }
}
