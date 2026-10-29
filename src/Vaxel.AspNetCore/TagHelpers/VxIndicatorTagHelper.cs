using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Vaxel.TagHelpers;

/// <summary>
/// Tag Helper for declaring request loading indicators (<c>vx-indicator</c>),
/// double-submit disable targets (<c>vx-disable</c>), and top progress bar behavior (<c>vx-progress</c>).
/// </summary>
[HtmlTargetElement("*", Attributes = "vx-indicator")]
[HtmlTargetElement("*", Attributes = "vx-disable")]
[HtmlTargetElement("*", Attributes = "vx-progress")]
public sealed partial class VxIndicatorTagHelper : TagHelper
{
    [GeneratedRegex(@"^[a-zA-Z_][a-zA-Z0-9_]*$")]
    private static partial Regex SimpleIdentifierRegex();

    private readonly VaxelOptions? _options;

    public VxIndicatorTagHelper() : this(null)
    {
    }

    [Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructor]
    public VxIndicatorTagHelper(Microsoft.Extensions.Options.IOptions<VaxelOptions>? options)
    {
        _options = options?.Value;
    }

    [HtmlAttributeName("vx-indicator")]
    public string? Indicator { get; set; }

    [HtmlAttributeNotBound]
    public string? Disable { get; set; }

    [HtmlAttributeNotBound]
    public string? Progress { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(output);

        var indicatorVal = Indicator ?? (output.Attributes.TryGetAttribute("vx-indicator", out var indAttr) ? indAttr.Value?.ToString() : null);
        if (indicatorVal is not null)
        {
            var trimmed = indicatorVal.Trim();
            if (trimmed.Length == 0)
            {
                throw new VaxelTagHelperException("vx-indicator cannot be empty. Specify a signal name or a '#id' selector.");
            }

            if (trimmed.StartsWith('#'))
            {
                try
                {
                    VaxelTargetException.ThrowIfInvalid(trimmed);
                }
                catch (VaxelTargetException ex)
                {
                    throw new VaxelTagHelperException(ex.Message, ex);
                }
            }
            else if (SimpleIdentifierRegex().IsMatch(trimmed))
            {
                if (_options?.SignalSchema is { } schema && !schema.IsAllowed(trimmed))
                {
                    throw new VaxelTagHelperException(
                        $"vx-indicator signal '{trimmed}' is not declared in the registered signal schema ({schema.TypeName}).");
                }
            }
            else
            {
                throw new VaxelTagHelperException(
                    $"Invalid vx-indicator value '{trimmed}'. Expected a signal identifier or a single '#id' selector.");
            }

            output.Attributes.SetAttribute("vx-indicator", trimmed);
        }

        var hasDisableAttr = output.Attributes.TryGetAttribute("vx-disable", out var disableAttr);
        var disableVal = Disable ?? (hasDisableAttr ? disableAttr?.Value?.ToString() : null);
        if (Disable is not null || hasDisableAttr)
        {
            var trimmed = (disableVal ?? string.Empty).Trim();
            if (trimmed.Length > 0)
            {
                try
                {
                    VaxelTargetException.ThrowIfInvalid(trimmed);
                }
                catch (VaxelTargetException ex)
                {
                    throw new VaxelTagHelperException(
                        $"Invalid vx-disable target '{trimmed}'. Expected an empty attribute or a single '#id' selector. {ex.Message}",
                        ex);
                }
                output.Attributes.SetAttribute("vx-disable", trimmed);
            }
        }

        var hasProgressAttr = output.Attributes.TryGetAttribute("vx-progress", out var progressAttr);
        var progressVal = Progress ?? (hasProgressAttr ? progressAttr?.Value?.ToString() : null);
        if (Progress is not null || hasProgressAttr)
        {
            var trimmed = (progressVal ?? string.Empty).Trim();
            if (trimmed.Length > 0 &&
                !string.Equals(trimmed, "off", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(trimmed, "on", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(trimmed, "immediate", StringComparison.OrdinalIgnoreCase) &&
                (!int.TryParse(trimmed, System.Globalization.CultureInfo.InvariantCulture, out var delayMs) || delayMs < 0))
            {
                throw new VaxelTagHelperException(
                    $"Invalid vx-progress value '{trimmed}'. Expected an empty attribute, 'off', 'immediate', or a non-negative delay in milliseconds.");
            }

            if (Progress is not null)
            {
                output.Attributes.SetAttribute("vx-progress", trimmed);
            }
        }
    }
}

/// <summary>
/// Tag Helper for View Transitions and region settle transitions (<c>vx-transition</c> and <c>vx-transition-name</c>).
/// </summary>
[HtmlTargetElement("*", Attributes = "vx-transition")]
[HtmlTargetElement("*", Attributes = "vx-transition-name")]
public sealed partial class VxTransitionTagHelper : TagHelper
{
    [GeneratedRegex(@"^[a-zA-Z_][a-zA-Z0-9_-]*$")]
    private static partial Regex CustomIdentRegex();

    [HtmlAttributeName("vx-transition-name")]
    public string? TransitionName { get; set; }

    [HtmlAttributeNotBound]
    public string? Transition { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(output);

        var nameVal = TransitionName ?? (output.Attributes.TryGetAttribute("vx-transition-name", out var nameAttr) ? nameAttr.Value?.ToString() : null);
        if (nameVal is not null)
        {
            var trimmed = nameVal.Trim();
            if (trimmed.Length == 0 ||
                !CustomIdentRegex().IsMatch(trimmed) ||
                string.Equals(trimmed, "none", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "auto", StringComparison.OrdinalIgnoreCase))
            {
                throw new VaxelTagHelperException(
                    $"Invalid vx-transition-name '{trimmed}'. Expected a valid CSS <custom-ident> (e.g. 'main-pane', 'order_card').");
            }

            output.Attributes.SetAttribute("vx-transition-name", trimmed);
        }

        var hasTransAttr = output.Attributes.TryGetAttribute("vx-transition", out var transAttr);
        var transVal = Transition ?? (hasTransAttr ? transAttr?.Value?.ToString() : null);
        if (Transition is not null || hasTransAttr)
        {
            var trimmed = (transVal ?? string.Empty).Trim();
            if (trimmed.Length > 0 &&
                !string.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(trimmed, "false", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(trimmed, "on", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(trimmed, "off", StringComparison.OrdinalIgnoreCase))
            {
                throw new VaxelTagHelperException(
                    $"Invalid vx-transition value '{trimmed}'. Expected an empty attribute, 'true', 'false', 'on', or 'off'.");
            }

            if (Transition is not null)
            {
                output.Attributes.SetAttribute("vx-transition", trimmed);
            }
        }
    }
}
