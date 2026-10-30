using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.AspNetCore.Builder;

public static class VaxelApplicationBuilderExtensions
{
    /// <summary>
    /// Adds vaxel middleware for protocol response marking (Vary: VX-Request) and negotiation.
    /// Does not short-circuit non-vaxel requests.
    /// </summary>
    public static IApplicationBuilder UseVaxel(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(async (context, next) =>
        {
            // 1. Serve embedded client assets (/_vaxel/vaxel.js, /_vaxel/vaxel.dev.js, /_vaxel/vaxel-htmx.js)
            if (context.Request.Path.StartsWithSegments("/_vaxel", out var remaining))
            {
                var fileName = remaining.Value?.TrimStart('/');
                if (!string.IsNullOrEmpty(fileName) && (fileName == "vaxel.js" || fileName == "vaxel.dev.js" || fileName == "vaxel-htmx.js"))
                {
                    var assembly = typeof(VaxelApplicationBuilderExtensions).Assembly;
                    var resourceName = $"Vaxel.AspNetCore.wwwroot._vaxel.{fileName}";
                    using var stream = assembly.GetManifestResourceStream(resourceName);
                    if (stream is not null)
                    {
                        context.Response.ContentType = "application/javascript; charset=utf-8";
                        context.Response.Headers.CacheControl = "no-cache";
                        await stream.CopyToAsync(context.Response.Body, context.RequestAborted).ConfigureAwait(false);
                        return;
                    }
                }
            }

            var options = context.RequestServices.GetService<Microsoft.Extensions.Options.IOptions<Vaxel.VaxelOptions>>()?.Value;
            if (Vaxel.Vaxel.IsVaxelRequest(context) && options is not null && ShouldForceFullPage(context, options))
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
                context.Response.ContentType = Vaxel.VaxelProtocol.PatchContentType;
                context.Response.Headers.CacheControl = "private, no-store";
                context.Response.Headers.Vary = "VX-Request";
                await context.Response.WriteAsync("<vx-directive reload=\"1\" />", context.RequestAborted).ConfigureAwait(false);
                return;
            }

            context.Response.OnStarting(() =>
            {
                var response = context.Response;
                var request = context.Request;
                var contentType = response.ContentType;
                var isPatch = contentType?.StartsWith("text/vnd.vaxel-patch+html", StringComparison.OrdinalIgnoreCase) == true;
                var isHtml = contentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true;
                var hasVxRequest = request.Headers.ContainsKey("VX-Request");

                if (isPatch || isHtml || hasVxRequest)
                {
                    var vary = response.Headers.Vary.ToString();
                    if (string.IsNullOrEmpty(vary))
                    {
                        response.Headers.Vary = "VX-Request";
                    }
                    else if (!vary.Contains("VX-Request", StringComparison.OrdinalIgnoreCase))
                    {
                        response.Headers.Append("Vary", "VX-Request");
                    }
                }

                if (isPatch || (options is not null && options.NoStoreWhenSignalsRead && context.Items.ContainsKey(Vaxel.SignalReader.SignalsReadItemKey)))
                {
                    var existingCacheControl = response.Headers.CacheControl.ToString();
                    if (string.IsNullOrEmpty(existingCacheControl))
                    {
                        response.Headers.CacheControl = "private, no-store";
                    }
                }

                return Task.CompletedTask;
            });

            await next(context).ConfigureAwait(false);
        });
    }

    private static bool ShouldForceFullPage(HttpContext context, Vaxel.VaxelOptions options)
    {
        if (options.ForceFullPage?.Invoke(context) == true)
        {
            return true;
        }

        if (options.FullPagePaths.Count == 0)
        {
            return false;
        }

        var path = context.Request.Path;
        foreach (var rawPrefix in options.FullPagePaths)
        {
            if (string.IsNullOrWhiteSpace(rawPrefix))
            {
                continue;
            }

            var prefix = rawPrefix.Trim();
            if (!prefix.StartsWith('/'))
            {
                prefix = "/" + prefix;
            }

            if (prefix.Length > 1 && prefix.EndsWith('/'))
            {
                prefix = prefix.TrimEnd('/');
            }

            if (path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
