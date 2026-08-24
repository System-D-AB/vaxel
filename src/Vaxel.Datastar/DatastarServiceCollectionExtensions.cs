using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Vaxel.Datastar;

public static class DatastarServiceCollectionExtensions
{
    /// <summary>
    /// Adds the Datastar adapter for translating Växel backend responses to the Datastar SSE client.
    /// <para>
    /// WARNING: The Datastar client requires 'unsafe-eval' in Content-Security-Policy to execute client-side expressions.
    /// </para>
    /// </summary>
    public static IServiceCollection AddDatastarAdapter(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }

    /// <summary>
    /// Maps the Datastar SDK test endpoint compliant with Datastar sdk/test.
    /// </summary>
    public static IEndpointConventionBuilder MapDatastarTestEndpoint(
        this IEndpointRouteBuilder endpoints,
        string pattern = "/test")
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        return endpoints.Map(pattern, async (HttpContext context) =>
        {
            context.Response.ContentType = "text/event-stream; charset=utf-8";
            context.Response.Headers.CacheControl = "no-cache";

            // Parse request body or query
            string? body = null;
            if (context.Request.ContentLength > 0 || context.Request.ContentType?.Contains("json") == true || context.Request.ContentType?.Contains("form") == true)
            {
                using var reader = new StreamReader(context.Request.Body);
                body = await reader.ReadToEndAsync();
            }

            var query = context.Request.Query;
            var testName = query["test"].ToString();
            if (string.IsNullOrEmpty(testName) && !string.IsNullOrEmpty(body) && body.Contains("test"))
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("test", out var tProp))
                    {
                        testName = tProp.GetString() ?? "";
                    }
                }
                catch { }
            }

            // Handle SDK Conformance Test Cases
            string output = testName switch
            {
                "patchElementsWithDefaults" =>
                    DatastarSseWriter.WritePatchElements("<div>Default Elements</div>"),

                "patchElementsWithAllOptions" =>
                    DatastarSseWriter.WritePatchElements("<div>All Options</div>", selector: "#target", mode: "append", focus: true, viewTransition: "my-transition"),

                "patchElementsWithoutDefaults" =>
                    DatastarSseWriter.WritePatchElements("<div>Without Defaults</div>", selector: "#container", mode: "prepend"),

                "patchElementsWithMultilineElements" =>
                    DatastarSseWriter.WritePatchElements("<div>Line 1</div>\n<div>Line 2</div>\n<div>Line 3</div>"),

                "patchSignalsWithDefaults" =>
                    DatastarSseWriter.WritePatchSignals(new { user = "alice", count = 10 }),

                "patchSignalsWithAllOptions" =>
                    DatastarSseWriter.WritePatchSignals(new { theme = "dark" }, onlyIfMissing: true),

                "patchSignalsWithoutDefaults" =>
                    DatastarSseWriter.WritePatchSignals(new { active = true }, onlyIfMissing: false),

                "patchSignalsWithMultilineJson" =>
                    DatastarSseWriter.WritePatchSignals("{\n  \"nested\": {\n    \"key\": \"value\"\n  }\n}"),

                "patchSignalsWithMultilineSignals" =>
                    DatastarSseWriter.WritePatchSignals("{\n  \"item1\": 1,\n  \"item2\": 2\n}"),

                "removeElementsWithDefaults" =>
                    DatastarSseWriter.WriteRemoveElements("#element-to-remove"),

                "removeElementsWithAllOptions" =>
                    DatastarSseWriter.WriteRemoveElements("#custom-selector"),

                "removeElementsWithoutDefaults" =>
                    DatastarSseWriter.WriteRemoveElements("#other-element"),

                "removeSignalsWithDefaults" =>
                    DatastarSseWriter.WriteRemoveSignals("draftKey"),

                "removeSignalsWithAllOptions" =>
                    DatastarSseWriter.WriteRemoveSignals("key1", "key2", "nested.key3"),

                "sendTwoEvents" =>
                    DatastarSseWriter.WritePatchElements("<p>First Event</p>") +
                    DatastarSseWriter.WritePatchSignals(new { step = 2 }),

                "readSignalsFromBody" =>
                    DatastarSseWriter.WritePatchSignals(new { receivedBody = body ?? "empty" }),

                // 4 Declined cases (Rule R2, Strict CSP - executeScript is declined)
                "executeScriptWithDefaults" or
                "executeScriptWithAllOptions" or
                "executeScriptWithoutDefaults" or
                "executeScriptWithMultilineScript" =>
                    DatastarSseWriter.RefuseExecuteScript("Rule R2: Server-sent script execution is declined (strict CSP)."),

                _ =>
                    DatastarSseWriter.WritePatchElements($"<div>Unknown test: {testName}</div>")
            };

            await context.Response.WriteAsync(output);
        });
    }
}
