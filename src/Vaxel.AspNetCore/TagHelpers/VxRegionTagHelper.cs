using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Vaxel.TagHelpers;

/// <summary>
/// Tag Helper for declaring a patchable region with vx-region. Requires an id on the element.
/// </summary>
[HtmlTargetElement("*", Attributes = "vx-region")]
public sealed class VxRegionTagHelper : TagHelper
{
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(output);

        if (!output.Attributes.ContainsName("id") ||
            string.IsNullOrWhiteSpace(output.Attributes["id"].Value?.ToString()))
        {
            throw new VaxelTagHelperException(
                $"Element <{output.TagName}> with 'vx-region' must have a non-empty 'id' attribute. " +
                "Regions are patch targets, and all patch targets must be ids.");
        }
    }
}
