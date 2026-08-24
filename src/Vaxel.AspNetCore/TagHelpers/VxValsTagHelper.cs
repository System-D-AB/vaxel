using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Vaxel.TagHelpers;

/// <summary>
/// Tag Helper for passing values to triggers via vx-vals-*. Values are treated as literal data, never expressions.
/// </summary>
[HtmlTargetElement("*", Attributes = "vx-vals-*")]
public sealed class VxValsTagHelper : TagHelper
{
    private const string ValsPrefix = "vx-vals-";

    [HtmlAttributeName(DictionaryAttributePrefix = ValsPrefix)]
    public IDictionary<string, string> Values { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(output);

        foreach (var (key, value) in Values)
        {
            output.Attributes.SetAttribute($"{ValsPrefix}{key.ToLowerInvariant()}", value);
        }
    }
}
