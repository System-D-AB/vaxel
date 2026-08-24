using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Options;

namespace Vaxel.TagHelpers;

/// <summary>
/// Tag Helper that renders the antiforgery meta tag read by the client agent.
/// </summary>
[HtmlTargetElement("vaxel-antiforgery", TagStructure = TagStructure.WithoutEndTag)]
[HtmlTargetElement("meta", Attributes = "vaxel-antiforgery")]
public sealed class VaxelAntiforgeryTagHelper : TagHelper
{
    private readonly IAntiforgery _antiforgery;
    private readonly VaxelOptions _options;

    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext? ViewContext { get; set; }

    public VaxelAntiforgeryTagHelper(IAntiforgery antiforgery, IOptions<VaxelOptions> options)
    {
        ArgumentNullException.ThrowIfNull(antiforgery);
        ArgumentNullException.ThrowIfNull(options);
        _antiforgery = antiforgery;
        _options = options.Value;
    }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(output);

        output.TagName = "meta";
        output.TagMode = TagMode.SelfClosing;

        var httpContext = ViewContext?.HttpContext;
        string? token = null;
        if (httpContext is not null)
        {
            try
            {
                var tokenSet = _antiforgery.GetTokens(httpContext);
                token = tokenSet.RequestToken ?? _antiforgery.GetAndStoreTokens(httpContext).RequestToken;
            }
            catch
            {
                try
                {
                    token = _antiforgery.GetAndStoreTokens(httpContext).RequestToken;
                }
                catch
                {
                    token = string.Empty;
                }
            }
        }

        output.Attributes.RemoveAll("vaxel-antiforgery");
        output.Attributes.SetAttribute("name", "vx-csrf");
        output.Attributes.SetAttribute("content", token ?? string.Empty);
    }
}
