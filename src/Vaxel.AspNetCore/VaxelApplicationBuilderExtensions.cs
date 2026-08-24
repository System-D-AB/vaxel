using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.AspNetCore.Builder;

public static class VaxelApplicationBuilderExtensions
{
    /// <summary>
    /// Adds Växel middleware for protocol response marking (Vary: VX-Request) and negotiation.
    /// Does not short-circuit non-Växel requests.
    /// </summary>
    public static IApplicationBuilder UseVaxel(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(async (context, next) =>
        {
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
