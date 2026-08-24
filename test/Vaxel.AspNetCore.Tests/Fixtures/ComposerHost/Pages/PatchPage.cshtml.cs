using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Vaxel;

namespace Vaxel.AspNetCore.Tests.Fixtures.ComposerHost.Pages;

public sealed class PatchPageModel : PageModel
{
    public IActionResult OnGet()
    {
        return Page();
    }

    public IActionResult OnGetPatch()
    {
        return Patch.Ok()
            .Replace("#page-pane", new HtmlString("<div id=\"page-pane\">page patch</div>"))
            .Announce("Page patch loaded");
    }
}
