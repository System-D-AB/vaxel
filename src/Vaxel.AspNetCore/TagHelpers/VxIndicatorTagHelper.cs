using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Vaxel.TagHelpers;

/// <summary>
/// Tag Helper for declaring request loading indicators and double-submit disable rules.
/// </summary>
[HtmlTargetElement("*", Attributes = "vx-indicator")]
[HtmlTargetElement("*", Attributes = "vx-disable")]
public sealed class VxIndicatorTagHelper : TagHelper
{
    [HtmlAttributeName("vx-indicator")]
    public string? Indicator { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(output);

        if (!string.IsNullOrEmpty(Indicator))
        {
            output.Attributes.SetAttribute("vx-indicator", Indicator);
        }
    }
}
