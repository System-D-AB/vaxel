using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Vaxel.TagHelpers;

/// <summary>
/// Tag Helper for reactive signal bindings (vx-text, vx-show, vx-class:*, vx-attr:*).
/// Enforces Rule R2: attribute values must be signal names, never client expressions.
/// </summary>
[HtmlTargetElement("*", Attributes = "vx-text")]
[HtmlTargetElement("*", Attributes = "vx-show")]
[HtmlTargetElement("*", Attributes = "vx-class:*")]
[HtmlTargetElement("*", Attributes = "vx-attr:*")]
public sealed partial class VxBindTagHelper : TagHelper
{
    private const string ClassPrefix = "vx-class:";
    private const string AttrPrefix = "vx-attr:";

    private static readonly Regex ValidIdentifierRegex = CreateValidIdentifierRegex();

    [HtmlAttributeName("vx-text")]
    public string? VxText { get; set; }

    [HtmlAttributeName("vx-show")]
    public string? VxShow { get; set; }

    [HtmlAttributeName(DictionaryAttributePrefix = ClassPrefix)]
    public IDictionary<string, string> Classes { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    [HtmlAttributeName(DictionaryAttributePrefix = AttrPrefix)]
    public IDictionary<string, string> Attributes { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private readonly VaxelOptions? _options;

    public VxBindTagHelper() : this(null)
    {
    }

    [Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructor]
    public VxBindTagHelper(Microsoft.Extensions.Options.IOptions<VaxelOptions>? options)
    {
        _options = options?.Value;
    }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(output);

        if (VxText is not null)
        {
            ValidateSignalName(VxText, "vx-text");
            output.Attributes.SetAttribute("vx-text", VxText);
        }

        if (VxShow is not null)
        {
            ValidateSignalName(VxShow, "vx-show");
            output.Attributes.SetAttribute("vx-show", VxShow);
        }

        foreach (var (className, signalName) in Classes)
        {
            ValidateSignalName(signalName, $"{ClassPrefix}{className}");
            output.Attributes.SetAttribute($"{ClassPrefix}{className}", signalName);
        }

        foreach (var (attrName, signalName) in Attributes)
        {
            ValidateSignalName(signalName, $"{AttrPrefix}{attrName}");
            output.Attributes.SetAttribute($"{AttrPrefix}{attrName}", signalName);
        }
    }

    private void ValidateSignalName(string value, string attributeName)
    {
        if (string.IsNullOrWhiteSpace(value) || !ValidIdentifierRegex.IsMatch(value.Trim()))
        {
            throw new VaxelTagHelperException(
                $"Attribute '{attributeName}' has invalid value '{value}'. " +
                "Per Rule R2 (Attribute values are data, never code), values must be simple signal identifiers, not client expressions.");
        }

        if (_options?.SignalSchema is not null && !_options.SignalSchema.IsAllowed(value))
        {
            throw new VaxelTagHelperException(
                $"Signal '{value}' bound in attribute '{attributeName}' is not defined in registered schema '{_options.SignalSchema.TypeName}'. " +
                $"Allowed schema signals: {string.Join(", ", _options.SignalSchema.AllowedSignals)}");
        }
    }

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex CreateValidIdentifierRegex();
}
