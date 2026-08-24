using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;

namespace Vaxel;

/// <summary>
/// An <see cref="IResult"/> and <see cref="IActionResult"/> that renders a vaxel patch document.
/// </summary>
public sealed class PatchResult : IResult, IActionResult
{
    public const string ContentType = "text/vnd.vaxel-patch+html; charset=utf-8";
    public const string ProtocolHeaderName = "VX-Protocol";
    public const string ProtocolVersion = "1";

    public int StatusCode { get; }
    public IReadOnlyList<PatchEntry> Patches { get; }
    public object? Signals { get; }
    internal DirectiveBag Directives { get; }
    public Refusal? Refusal { get; }

    public PatchResult(PatchBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        StatusCode = builder.StatusCode;
        Patches = builder.Patches;
        Signals = builder.SignalValues;
        Directives = builder.Directives;
        Refusal = builder.Refusal;
    }

    public async Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var response = httpContext.Response;
        response.StatusCode = StatusCode;
        response.ContentType = ContentType;
        response.Headers[ProtocolHeaderName] = ProtocolVersion;

        var htmlEncoder = httpContext.RequestServices.GetService<HtmlEncoder>() ?? HtmlEncoder.Default;

        await using var writer = new HttpResponseStreamWriter(response.Body, System.Text.Encoding.UTF8);
        await PatchDocumentWriter.WriteAsync(writer, Patches, Signals, Directives, htmlEncoder, httpContext.RequestAborted).ConfigureAwait(false);
        await writer.FlushAsync().ConfigureAwait(false);
    }

    public Task ExecuteResultAsync(ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ExecuteAsync(context.HttpContext);
    }

    /// <summary>
    /// Writes the patch document to a string for testing or inspection.
    /// </summary>
    public async Task<string> ToHtmlAsync(HtmlEncoder? htmlEncoder = null)
    {
        using var writer = new StringWriter();
        await PatchDocumentWriter.WriteAsync(writer, Patches, Signals, Directives, htmlEncoder ?? HtmlEncoder.Default).ConfigureAwait(false);
        return writer.ToString();
    }
}
