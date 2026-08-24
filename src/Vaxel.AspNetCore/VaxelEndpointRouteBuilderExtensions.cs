using System.Security.Claims;
using System.Text;
using System.Threading.Channels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Vaxel;

public static class VaxelEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps the vaxel SSE stream endpoint for real-time server push updates.
    /// </summary>
    public static IEndpointConventionBuilder MapVaxelStream(
        this IEndpointRouteBuilder endpoints,
        string pattern = "/_vaxel/stream")
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        return endpoints.MapGet(pattern, async (HttpContext httpContext) =>
        {
            var options = httpContext.RequestServices.GetRequiredService<IOptions<VaxelOptions>>().Value;
            var registry = httpContext.RequestServices.GetRequiredService<PushConnectionRegistry>();

            var user = httpContext.User;
            var isAuthenticated = user.Identity?.IsAuthenticated == true;

            if (!isAuthenticated && !options.Push.AllowAnonymous)
            {
                httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            var userId = isAuthenticated
                ? user.FindFirst(options.Push.UserIdClaimType)?.Value ?? user.Identity?.Name
                : null;

            var groups = isAuthenticated
                ? user.FindAll(options.Push.GroupIdClaimType).Select(c => c.Value).ToHashSet(StringComparer.Ordinal)
                : (IReadOnlySet<string>)new HashSet<string>();

            var connectionId = Guid.NewGuid().ToString("N");
            var channel = Channel.CreateUnbounded<PushMessage>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });

            var connection = new PushConnection(connectionId, userId, groups, channel);

            if (!registry.TryAddConnection(connection, out var error))
            {
                httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await httpContext.Response.WriteAsync(error ?? "Connection limit exceeded.", httpContext.RequestAborted);
                return;
            }

            try
            {
                var response = httpContext.Response;
                response.ContentType = "text/event-stream; charset=utf-8";
                response.Headers.CacheControl = "no-cache, no-transform";
                response.Headers.Connection = "keep-alive";
                response.Headers["X-Accel-Buffering"] = "no";

                var cancellationToken = httpContext.RequestAborted;
                var heartbeatInterval = TimeSpan.FromSeconds(Math.Max(1, options.Push.HeartbeatSeconds));

                // Send initial connection comment
                await response.WriteAsync($": connected {connectionId}\n\n", Encoding.UTF8, cancellationToken);
                await response.Body.FlushAsync(cancellationToken);

                using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                while (!cancellationToken.IsCancellationRequested)
                {
                    var readTask = channel.Reader.ReadAsync(cancellationToken).AsTask();
                    var delayTask = Task.Delay(heartbeatInterval, cancellationToken);

                    var completed = await Task.WhenAny(readTask, delayTask);

                    if (completed == readTask)
                    {
                        var message = await readTask;
                        var sseFrame = FormatSseMessage(message.EventName, message.Data);
                        await response.WriteAsync(sseFrame, Encoding.UTF8, cancellationToken);
                        await response.Body.FlushAsync(cancellationToken);
                    }
                    else
                    {
                        // Heartbeat comment
                        await response.WriteAsync(": heartbeat\n\n", Encoding.UTF8, cancellationToken);
                        await response.Body.FlushAsync(cancellationToken);
                    }
                }
            }
            finally
            {
                registry.RemoveConnection(connectionId);
            }
        });
    }

    private static string FormatSseMessage(string eventName, string data)
    {
        var sb = new StringBuilder();
        sb.Append("event: ").Append(eventName).Append('\n');

        var lines = data.Split('\n');
        foreach (var line in lines)
        {
            sb.Append("data: ").Append(line.TrimEnd('\r')).Append('\n');
        }

        sb.Append('\n');
        return sb.ToString();
    }
}
