using System.Text.Json;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Vaxel.TagHelpers;

// US-6 (T14): Tag Helpers for the passthrough attributes a view previously had to write raw.
// They are the seam docs/05 promises — Razor never types a bare vx-* string without a helper
// claiming it. They validate what is cheaply validated and otherwise leave the attribute intact.

/// <summary>
/// Seeds the signal store at first paint with a JSON object: <c>vx-signals='{"tab":"overview"}'</c>.
/// Validates that the value is a JSON object so a malformed seed fails at build/render, not silently.
/// </summary>
[HtmlTargetElement("*", Attributes = "vx-signals")]
public sealed class VxSignalsTagHelper : TagHelper
{
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(output);
        VxSignalJson.Validate(output, "vx-signals");
    }
}

/// <summary>
/// Same as <c>vx-signals</c>, but does not overwrite a key that already exists.
/// </summary>
[HtmlTargetElement("*", Attributes = "vx-signals-if-missing")]
public sealed class VxSignalsIfMissingTagHelper : TagHelper
{
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(output);
        VxSignalJson.Validate(output, "vx-signals-if-missing");
    }
}

/// <summary>
/// Mirrors the named signals to <c>localStorage</c>. Explicit name list, never a wildcard.
/// </summary>
[HtmlTargetElement("*", Attributes = "vx-persist")]
public sealed class VxPersistTagHelper : TagHelper
{
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(output);
        VxSignalJson.RejectWildcard(output, "vx-persist");
    }
}

/// <summary>
/// Mirrors the named signals to <c>sessionStorage</c>. Explicit name list, never a wildcard.
/// </summary>
[HtmlTargetElement("*", Attributes = "vx-persist-session")]
public sealed class VxPersistSessionTagHelper : TagHelper
{
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(output);
        VxSignalJson.RejectWildcard(output, "vx-persist-session");
    }
}

/// <summary>
/// Native confirm before sending. Plain text only — markup here never becomes HTML.
/// </summary>
[HtmlTargetElement("*", Attributes = "vx-confirm")]
public sealed class VxConfirmTagHelper : TagHelper
{
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(output);
        // Passthrough: the attribute is already a literal; nothing to validate.
    }
}

/// <summary>
/// Marks a subtree the morph must never touch — an island owns it.
/// </summary>
[HtmlTargetElement("*", Attributes = "vx-preserve")]
public sealed class VxPreserveTagHelper : TagHelper
{
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(output);
        // Passthrough marker.
    }
}

/// <summary>
/// Opens the document's single Server-Sent Events channel at the given URL.
/// </summary>
[HtmlTargetElement("*", Attributes = "vx-sse")]
public sealed class VxSseTagHelper : TagHelper
{
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(output);

        var value = output.Attributes["vx-sse"].Value?.ToString();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new VaxelTagHelperException(
                $"Element <{output.TagName}> with 'vx-sse' must name a channel URL.");
        }
    }
}

internal static class VxSignalJson
{
    public static void Validate(TagHelperOutput output, string attribute)
    {
        var value = output.Attributes[attribute].Value?.ToString();
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(value);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new VaxelTagHelperException(
                    $"'{attribute}' must be a JSON object seeding signal names to values, not a {doc.RootElement.ValueKind}.");
            }
        }
        catch (JsonException ex)
        {
            throw new VaxelTagHelperException(
                $"'{attribute}' must be a valid JSON object: {ex.Message}");
        }
    }

    public static void RejectWildcard(TagHelperOutput output, string attribute)
    {
        var value = output.Attributes[attribute].Value?.ToString();
        if (value is not null && value.Contains('*'))
        {
            throw new VaxelTagHelperException(
                $"'{attribute}' takes an explicit, space-separated list of signal names, never a wildcard. " +
                "Persisting everything is how a session identifier ends up in storage.");
        }
    }
}
