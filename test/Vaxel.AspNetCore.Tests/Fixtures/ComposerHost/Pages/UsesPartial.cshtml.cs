using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Vaxel.AspNetCore.Tests.Fixtures.ComposerHost.Pages;

public sealed class UsesPartialModel : PageModel
{
    public string Message { get; set; } = "world";

    public void OnGet()
    {
    }
}
