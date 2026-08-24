using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Vaxel;
using Workbench.Signals;

namespace Workbench.Pages;

public sealed class IndexModel : PageModel
{
    private readonly IFragmentComposer _composer;

    [BindProperty(SupportsGet = true)]
    public string Tab { get; set; } = "overview";

    public IndexModel(IFragmentComposer composer)
    {
        _composer = composer;
    }

    public async Task<IActionResult> OnGetAsync([FromSignals] WorkbenchSignals signals)
    {
        if (Request.Query.TryGetValue("tab", out var tabVal) && !string.IsNullOrWhiteSpace(tabVal))
        {
            Tab = tabVal.ToString();
        }
        else if (!string.IsNullOrWhiteSpace(signals.Tab))
        {
            Tab = signals.Tab;
        }
        else
        {
            Tab = "overview";
        }

        var partialName = Tab switch
        {
            "submissions" => "_Submissions",
            "proposals" => "_Proposals",
            "settings" => "_Settings",
            _ => "_Overview"
        };

        return await global::Vaxel.Vaxel.PageOrPatch(HttpContext,
            page: () => Task.FromResult<IActionResult>(Page()),
            patch: async () =>
            {
                var fragment = await _composer.PartialAsync(partialName, null);
                return Patch.Ok()
                    .Replace("#pane", fragment)
                    .Signals(new { tab = Tab, draftSeq = signals.DraftSeq + 1 })
                    .PushUrl(Tab == "overview" ? "/" : $"/?tab={Tab}");
            });
    }
}
