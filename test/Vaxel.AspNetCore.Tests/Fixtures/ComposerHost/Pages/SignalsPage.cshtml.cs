using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Vaxel;

namespace Vaxel.AspNetCore.Tests.Fixtures.ComposerHost.Pages;

[IgnoreAntiforgeryToken]
public sealed class SignalsPageModel : PageModel
{
    [BindProperty]
    public string? Name { get; set; }

    [FromSignals]
    public ShellSignals? Signals { get; set; }

    public IActionResult OnGet([FromSignals] ShellSignals ui)
    {
        Signals = ui;
        return Page();
    }

    public IActionResult OnPost([FromSignals] ShellSignals ui)
    {
        Signals = ui;
        return Content($"Name:{Name},Tab:{Signals?.Tab}", "text/plain");
    }
}
