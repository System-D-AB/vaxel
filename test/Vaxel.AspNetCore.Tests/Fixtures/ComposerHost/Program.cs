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
