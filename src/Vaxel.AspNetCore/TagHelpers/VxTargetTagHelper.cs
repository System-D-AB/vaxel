using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Vaxel.TagHelpers;

/// <summary>
/// Tag Helper for the vx-target attribute. Validates that the target is a single valid #id selector.
/// </summary>
[HtmlTargetElement("*", Attributes = "vx-target")]
public sealed class VxTargetTagHelper : TagHelper
{
    [HtmlAttributeName("vx-target")]
    public string? Target { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(output);

        if (string.IsNullOrWhiteSpace(Target))
        {
            throw new VaxelTagHelperException("vx-target attribute cannot be empty. Target must be a single '#id' selector.");
        }

        try
        {
            VaxelTargetException.ThrowIfInvalid(Target);
            output.Attributes.SetAttribute("vx-target", Target);
        }
        catch (VaxelTargetException ex)
        {
            throw new VaxelTagHelperException(ex.Message, ex);
        }
    }
}
