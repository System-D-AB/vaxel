using Microsoft.AspNetCore.Mvc.RazorPages;
using Vaxel;

namespace Vaxel.AspNetCore.Tests.Fixtures.ComposerHost.Pages;

public sealed class ComposeFromPageModel : PageModel
{
    private readonly IFragmentComposer _fragments;

    public ComposeFromPageModel(IFragmentComposer fragments) => _fragments = fragments;

    public string Html { get; private set; } = "";

    public async Task OnGetAsync()
    {
        var content = await _fragments.PartialAsync("_Hello", "world");
        using var writer = new StringWriter();
        content.WriteTo(writer, System.Text.Encodings.Web.HtmlEncoder.Default);
        Html = writer.ToString();
    }
}
