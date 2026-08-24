using Microsoft.AspNetCore.Mvc;
using Vaxel;

namespace Vaxel.AspNetCore.Tests.Fixtures.ComposerHost.Controllers;

public sealed class ComposeController : Controller
{
    private readonly IFragmentComposer _fragments;

    public ComposeController(IFragmentComposer fragments) => _fragments = fragments;

    [HttpGet("/compose/partial")]
    public async Task<IActionResult> Partial()
    {
        var html = await _fragments.PartialAsync("_Hello", "world");
        using var writer = new StringWriter();
        html.WriteTo(writer, System.Text.Encodings.Web.HtmlEncoder.Default);
        return Content(writer.ToString(), "text/html");
    }

    [HttpGet("/patch/controller")]
    public IActionResult PatchAction()
    {
        return Patch.Ok()
            .Replace("#controller-pane", new Microsoft.AspNetCore.Html.HtmlString("<div>controller content</div>"))
            .PushUrl("/apps/test");
    }

    [HttpGet("/page-or-patch/controller")]
    public IActionResult PageOrPatchAction()
    {
        return Vaxel.PageOrPatch(HttpContext,
            page: () => Content("<html>controller page</html>", "text/html"),
            patch: () => Patch.Ok().Replace("#pane", new Microsoft.AspNetCore.Html.HtmlString("<div>controller patch</div>")));
    }

    [HttpGet("/signals/controller")]
    public IActionResult SignalsAction([FromSignals] ShellSignals signals)
    {
        return Json(new { tab = signals.Tab, railOpen = signals.RailOpen, count = signals.Count });
    }

    [HttpGet("/signals/reader")]
    public IActionResult SignalReaderAction([FromServices] ISignalReader reader)
    {
        var hasTab = reader.TryGet<string>("tab", out var tab);
        return Json(new { hasTab, tab, count = reader.Get<int>("count", -1) });
    }

    [HttpGet("/signals/cache-override")]
    public IActionResult CacheOverrideAction([FromSignals] ShellSignals signals)
    {
        Response.Headers.CacheControl = "public, max-age=3600";
        return Json(new { tab = signals.Tab });
    }
}
