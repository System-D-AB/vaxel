using Microsoft.AspNetCore.Razor.TagHelpers;
using Vaxel.Expressions;

namespace Vaxel.TagHelpers;

/// <summary>
/// Tag Helper for reactive signal bindings (vx-text, vx-show, vx-class:*, vx-attr:*).
/// Enforces Rule R2 (amended in v2.0): attribute values are data, or pure expressions over signals, never code.
/// Validates expression syntax, caps, function whitelist, forbidden attributes, and (when registered) the signal schema.
/// </summary>
[HtmlTargetElement("*", Attributes = "vx-text")]
[HtmlTargetElement("*", Attributes = "vx-show")]
[HtmlTargetElement("*", Attributes = "vx-class:*")]
[HtmlTargetElement("*", Attributes = "vx-attr:*")]
public sealed class VxBindTagHelper : TagHelper
{
    private const string ClassPrefix = "vx-class:";
    private const string AttrPrefix = "vx-attr:";

    private static readonly HashSet<string> ForbiddenBoundAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "href",
        "src",
        "action",
        "formaction",
        "style",
    };

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
            ValidateExpression(VxText, "vx-text", allowTemplate: true, requireBoolean: false);
            output.Attributes.SetAttribute("vx-text", VxText);
        }

        if (VxShow is not null)
        {
            ValidateExpression(VxShow, "vx-show", allowTemplate: false, requireBoolean: true);
            output.Attributes.SetAttribute("vx-show", VxShow);
        }

        foreach (var (className, expr) in Classes)
        {
            var fullAttr = $"{ClassPrefix}{className}";
            ValidateExpression(expr, fullAttr, allowTemplate: false, requireBoolean: true);
            output.Attributes.SetAttribute(fullAttr, expr);
        }

        foreach (var (attrName, expr) in Attributes)
        {
            var fullAttr = $"{AttrPrefix}{attrName}";
            ValidateBoundAttributeName(attrName, fullAttr);
            ValidateExpression(expr, fullAttr, allowTemplate: false, requireBoolean: false);
            output.Attributes.SetAttribute(fullAttr, expr);
        }
    }

    private static void ValidateBoundAttributeName(string attrName, string fullAttr)
    {
        if (string.IsNullOrWhiteSpace(attrName) ||
            ForbiddenBoundAttributes.Contains(attrName) ||
            attrName.StartsWith("on", StringComparison.OrdinalIgnoreCase))
        {
            throw new VaxelTagHelperException(
                $"Binding '{fullAttr}' is refused at render time per Rule R2 and docs/06-security. " +
                $"Attributes 'href', 'src', 'action', 'formaction', 'style', and 'on*' cannot be bound to client expressions.");
        }
    }

    private void ValidateExpression(string value, string attributeName, bool allowTemplate, bool requireBoolean)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new VaxelTagHelperException(
                $"Attribute '{attributeName}' has an empty expression (span 0..0). " +
                "Provide a signal name or closed expression over signals.");
        }

        var parseRes = ExpressionParser.ParseBinding(value, allowTemplate);
        if (!parseRes.Ok || parseRes.Value is null)
        {
            throw new VaxelTagHelperException(
                $"Attribute '{attributeName}' has invalid expression '{value}' at span {parseRes.Span} ({parseRes.Code}): {parseRes.Reason}. " +
                "Per Rule R2, attribute values must be data or valid closed expressions over signals.");
        }

        var checkRes = ExpressionTypeChecker.Check(parseRes.Value, _options?.SignalSchema);
        if (!checkRes.Ok)
        {
            throw new VaxelTagHelperException(
                $"Attribute '{attributeName}' expression '{value}' failed validation at span {checkRes.Span} ({checkRes.Code}): {checkRes.Reason}.");
        }

        if (requireBoolean && checkRes.Value is not (ExprValueType.Bool or ExprValueType.Any))
        {
            throw new VaxelTagHelperException(
                $"Attribute '{attributeName}' requires a boolean expression, but '{value}' has static type '{checkRes.Value}' at span {parseRes.Value.Span} (expressions.type).");
        }
    }
}
