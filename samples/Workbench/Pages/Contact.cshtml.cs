using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Vaxel;

namespace Workbench.Pages;

public sealed class ContactModel : PageModel
{
    private readonly IFragmentComposer _composer;

    [BindProperty]
    public string Name { get; set; } = "";

    [BindProperty]
    public string Message { get; set; } = "";

    public string? ErrorMessage { get; set; }

    public bool Submitted { get; set; }

    public ContactModel(IFragmentComposer composer)
    {
        _composer = composer;
    }

    public async Task<IActionResult> OnGetAsync([FromQuery] string? status)
    {
        if (status == "success")
        {
            Submitted = true;
            if (string.IsNullOrEmpty(Name)) Name = "Valued User";
        }

        return await global::Vaxel.Vaxel.PageOrPatch(HttpContext,
            page: () => Task.FromResult<IActionResult>(Page()),
            patch: async () =>
            {
                var paneFragment = await _composer.PartialAsync("_ContactPane", this);
                return Patch.Ok()
                    .Replace("#pane", paneFragment)
                    .PushUrl(status == "success" ? "/contact?status=success" : "/contact");
            });
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(Message))
        {
            ErrorMessage = "Name and message are required.";
            Response.StatusCode = StatusCodes.Status422UnprocessableEntity;

            return await global::Vaxel.Vaxel.PageOrPatch(HttpContext,
                page: () => Task.FromResult<IActionResult>(Page()),
                patch: async () =>
                {
                    var errorFragment = await _composer.PartialAsync("_ContactContent", this);
                    return Patch.Status(422).Replace("#contact", errorFragment);
                });
        }

        Submitted = true;

        return await global::Vaxel.Vaxel.PageOrPatch(HttpContext,
            page: () => Task.FromResult<IActionResult>(Redirect("/contact?status=success")),
            patch: async () =>
            {
                var successFragment = await _composer.PartialAsync("_ContactContent", this);
                var noticeFragment = await _composer.PartialAsync("_Notice", ("Contact message submitted successfully.", false));
                return Patch.Ok()
                    .Replace("#contact", successFragment)
                    .Into("#notices", noticeFragment)
                    .PushUrl("/contact?status=success");
            });
    }
}
