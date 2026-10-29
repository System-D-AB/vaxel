using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.Extensions.DependencyInjection;
using Vaxel;
using Vaxel.Datastar;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages(options =>
{
    options.RootDirectory = "/Fixtures/ComposerHost/Pages";
});
builder.Services.AddControllersWithViews();
builder.Services.AddRazorComponents();
builder.Services.Configure<RazorViewEngineOptions>(options =>
{
    options.ViewLocationFormats.Insert(0, "/Fixtures/ComposerHost/Views/{1}/{0}.cshtml");
    options.ViewLocationFormats.Insert(0, "/Fixtures/ComposerHost/Views/Shared/{0}.cshtml");
    options.ViewLocationFormats.Insert(0, "/Fixtures/ComposerHost/Views/{0}.cshtml");
    options.PageViewLocationFormats.Insert(0, "/Fixtures/ComposerHost/Pages/{1}/{0}.cshtml");
    options.PageViewLocationFormats.Insert(0, "/Fixtures/ComposerHost/Pages/Shared/{0}.cshtml");
    options.PageViewLocationFormats.Insert(0, "/Fixtures/ComposerHost/Pages/{0}.cshtml");
    options.PageViewLocationFormats.Insert(0, "/Fixtures/ComposerHost/Views/Shared/{0}.cshtml");
    options.PageViewLocationFormats.Insert(0, "/Fixtures/ComposerHost/Views/{0}.cshtml");
});
builder.Services.AddAuthentication("TestAuth")
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, global::Vaxel.AspNetCore.Tests.Fixtures.ComposerHost.TestAuthHandler>("TestAuth", null);

builder.Services.AddVaxel(options =>
{
    options.Push.HeartbeatSeconds = 1; // 1s heartbeat for faster test execution
});
builder.Services.AddVaxelBus<string>();

var app = builder.Build();

app.UseDeveloperExceptionPage();
app.UseAuthentication();
app.UseVaxel();
app.MapRazorPages();
app.MapControllers();
app.MapVaxelStream("/_vaxel/stream");
app.MapDatastarTestEndpoint("/test");

app.MapGet("/compose-partial", async (IFragmentComposer fragments) =>
{
    var html = await fragments.PartialAsync("_Hello", "world");
    return Results.Content(HtmlContent.ToString(html), "text/html");
});

app.MapGet("/patch/minimal", () =>
{
    return Patch.Ok()
        .Replace("#pane", new Microsoft.AspNetCore.Html.HtmlString("<section id=\"pane\">minimal</section>"))
        .Signals(new { draftSeq = 149 })
        .Focus("#filter");
});

// US-2 fixture: a fragment large enough to overflow HttpResponseStreamWriter's internal
// buffer, forcing a flush to the response body mid-write. On a default Kestrel host
// (AllowSynchronousIO = false) a synchronous write throws here; an async writer completes.
app.MapGet("/patch/large", () =>
{
    var big = "<section id=\"pane\">" + string.Concat(Enumerable.Repeat("<p>row</p>", 8000)) + "</section>";
    return Patch.Ok().Replace("#pane", new Microsoft.AspNetCore.Html.HtmlString(big));
});

app.MapPost("/patch/status-422", () =>
{
    return Patch.Status(422)
        .Replace("#errors", new Microsoft.AspNetCore.Html.HtmlString("<div id=\"errors\">Invalid input</div>"));
});

app.MapGet("/tabs", async (HttpContext http, IFragmentComposer fragments) =>
    await global::Vaxel.Vaxel.PageOrPatch(http,
        page: () => Task.FromResult<IResult>(Results.Content("<html><body><section id=\"pane\">page body</section></body></html>", "text/html")),
        patch: async () => Patch.Ok().Replace("#pane", await fragments.PartialAsync("_Hello", "world"))));

app.MapPost("/cookbook/contact", (HttpContext http) =>
    global::Vaxel.Vaxel.PageOrPatch(http,
        page: () => Results.Redirect("/thank-you"),
        patch: () => Patch.Ok()
            .Replace("#contact", new Microsoft.AspNetCore.Html.HtmlString("<p>Thank you!</p>"))
            .PushUrl("/thank-you")));

var readyOrders = new System.Collections.Concurrent.ConcurrentDictionary<string, bool>();

static (string PreparingHtml, string ReadyHtml) RenderOrderBoardRegions(System.Collections.Concurrent.ConcurrentDictionary<string, bool> ready)
{
    var is104Ready = ready.ContainsKey("104");
    var prepItems = is104Ready
        ? "<li id=\"order_105\">#105 · Ramen</li>"
        : "<li id=\"order_104\">#104 · Gyoza</li><li id=\"order_105\">#105 · Ramen</li>";
    var readyItems = is104Ready
        ? "<li id=\"order_104\">#104 · Gyoza</li>"
        : "";
    return (
        $"<ul class=\"order-list\">{prepItems}</ul>",
        $"<ul class=\"order-list\">{readyItems}</ul>");
}

app.MapGet("/cookbook/orders", (HttpContext http) =>
{
    var (prepHtml, readyHtml) = RenderOrderBoardRegions(readyOrders);
    var pageHtml = $"<html><body vx-sse=\"/_vaxel/stream\"><section id=\"orders-preparing\" vx-region>{prepHtml}</section><section id=\"orders-ready\" vx-region>{readyHtml}</section></body></html>";
    return global::Vaxel.Vaxel.PageOrPatch(http,
        page: () => (IResult)Results.Content(pageHtml, "text/html"),
        patch: () => Patch.Ok()
            .Replace("#orders-preparing", new Microsoft.AspNetCore.Html.HtmlString(prepHtml))
            .Replace("#orders-ready", new Microsoft.AspNetCore.Html.HtmlString(readyHtml)));
});

app.MapPost("/cookbook/orders/ready", (HttpContext http, string id, IBus<string> bus) =>
{
    readyOrders[id] = true;
    bus.Publish(id);
    var (prepHtml, readyHtml) = RenderOrderBoardRegions(readyOrders);
    return global::Vaxel.Vaxel.PageOrPatch(http,
        page: () => (IResult)Results.Redirect("/cookbook/orders"),
        patch: () => Patch.Ok()
            .Replace("#orders-preparing", new Microsoft.AspNetCore.Html.HtmlString(prepHtml))
            .Replace("#orders-ready", new Microsoft.AspNetCore.Html.HtmlString(readyHtml))
            .Announce($"Order {id} is ready"));
});

app.MapGet("/plain-route", () => Results.Text("plain text", "text/plain"));

app.Run();

namespace Vaxel.AspNetCore.Tests.Fixtures.ComposerHost
{
    public partial class Program;
}

internal static class HtmlContent
{
    public static string ToString(Microsoft.AspNetCore.Html.IHtmlContent content)
    {
        using var writer = new StringWriter();
        content.WriteTo(writer, System.Text.Encodings.Web.HtmlEncoder.Default);
        return writer.ToString();
    }
}
