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

            context.Response.OnStarting(() =>
            {
                var response = context.Response;
                var request = context.Request;
                var isPatch = response.ContentType?.StartsWith("text/vnd.vaxel-patch+html", StringComparison.OrdinalIgnoreCase) == true;
                var hasVxRequest = request.Headers.ContainsKey("VX-Request");

                if (isPatch || hasVxRequest)
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

                var options = context.RequestServices.GetService<Microsoft.Extensions.Options.IOptions<Vaxel.VaxelOptions>>()?.Value;
                if (options is not null && options.NoStoreWhenSignalsRead && context.Items.ContainsKey(Vaxel.SignalReader.SignalsReadItemKey))
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
}
