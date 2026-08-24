using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Vaxel.TagHelpers;

/// <summary>
/// Tag Helper for vaxel HTTP triggers (vx-get, vx-post, vx-put, vx-patch, vx-delete).
/// Enforces degradation on valid interactive HTML elements (a, form, button).
/// </summary>
[HtmlTargetElement("*", Attributes = "vx-get")]
[HtmlTargetElement("*", Attributes = "vx-post")]
[HtmlTargetElement("*", Attributes = "vx-put")]
[HtmlTargetElement("*", Attributes = "vx-patch")]
[HtmlTargetElement("*", Attributes = "vx-delete")]
public sealed class VxTriggerTagHelper : TagHelper
{
    private static readonly HashSet<string> AllowedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "form", "button", "input"
    };

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(output);

        // Degradation check: triggers must only sit on degradable elements
        if (!AllowedTags.Contains(output.TagName))
        {
            throw new VaxelTagHelperException(
                $"Trigger attributes (vx-get, vx-post, etc.) cannot be placed on <{output.TagName}>. " +
                "Per Rule R3 (Progressive Enhancement), triggers must only be placed on degradable elements (<a>, <form>, <button>).");
        }

        // Security check: no inline onclick or javascript: URLs
        if (output.Attributes.ContainsName("onclick"))
        {
            throw new VaxelTagHelperException("Rule R2 violation: inline onclick handler is prohibited in vaxel.");
        }

        if (output.Attributes.TryGetAttribute("href", out var hrefAttr) &&
            hrefAttr.Value?.ToString()?.TrimStart().StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) == true)
        {
            throw new VaxelTagHelperException("Rule R2 violation: javascript: URL is prohibited.");
        }
    }
}
