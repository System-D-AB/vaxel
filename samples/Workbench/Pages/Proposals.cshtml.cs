using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Vaxel;

namespace Workbench.Pages;

public sealed class ProposalsModel : PageModel
{
    private readonly IFragmentComposer _composer;

    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    public ProposalsModel(IFragmentComposer composer)
    {
        _composer = composer;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var model = (Id: Id, Title: "Realtime Agent Architecture (v2)");

        return await global::Vaxel.Vaxel.PageOrPatch(HttpContext,
            page: () => Task.FromResult<IActionResult>(Page()),
            patch: async () =>
            {
                var editFragment = await _composer.PartialAsync("_ProposalEdit", model);
                return Patch.Ok().Replace($"#prop-{Id}", editFragment);
            });
    }
}
