using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Vaxel;

namespace Vaxel.AspNetCore.Tests.Fixtures.ComposerHost.Pages;

public sealed class PageOrPatchPageModel : PageModel
{
    public IActionResult OnGet()
    {
        return Vaxel.PageOrPatch(HttpContext,
            page: () => Page(),
            patch: () => Patch.Ok().Replace("#pane", new HtmlString("<div id=\"pane\">page patch</div>")));
    }
}
