using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Html;

namespace Vaxel;

internal static class PatchDocumentWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.Default
    };

    public static string Render(
        IReadOnlyList<PatchEntry> patches,
        object? signals,
        DirectiveBag? directives,
        HtmlEncoder? htmlEncoder = null)
    {
        using var writer = new StringWriter();
        WriteAsync(writer, patches, signals, directives, htmlEncoder ?? HtmlEncoder.Default).GetAwaiter().GetResult();
        return writer.ToString();
    }

    public static async Task WriteAsync(
        TextWriter writer,
        IReadOnlyList<PatchEntry> patches,
        object? signals,
        DirectiveBag? directives,
        HtmlEncoder htmlEncoder,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(writer);
        htmlEncoder ??= HtmlEncoder.Default;

        // 1. Patches
        for (var i = 0; i < patches.Count; i++)
        {
            var patch = patches[i];
            await writer.WriteAsync("<vx-patch target=\"").ConfigureAwait(false);
            await writer.WriteAsync(htmlEncoder.Encode(patch.Target)).ConfigureAwait(false);
            await writer.WriteAsync("\" mode=\"").ConfigureAwait(false);
            await writer.WriteAsync(GetModeString(patch.Mode)).ConfigureAwait(false);
            await writer.WriteAsync("\"").ConfigureAwait(false);

            if (patch.Namespace is not VaxelNamespace.Html)
            {
                await writer.WriteAsync(" namespace=\"").ConfigureAwait(false);
                await writer.WriteAsync(GetNamespaceString(patch.Namespace)).ConfigureAwait(false);
                await writer.WriteAsync("\"").ConfigureAwait(false);
            }

            if (!string.IsNullOrEmpty(patch.Transition))
            {
                await writer.WriteAsync(" transition=\"").ConfigureAwait(false);
                await writer.WriteAsync(htmlEncoder.Encode(patch.Transition)).ConfigureAwait(false);
                await writer.WriteAsync("\"").ConfigureAwait(false);
            }

            await writer.WriteAsync(">").ConfigureAwait(false);

            if (patch.Mode is not SwapMode.Remove && patch.Content is not null)
            {
                patch.Content.WriteTo(writer, htmlEncoder);
            }

            await writer.WriteAsync("</vx-patch>\n").ConfigureAwait(false);
        }

        // 2. Signals
        if (signals is not null)
        {
            var json = JsonSerializer.Serialize(signals, signals.GetType(), JsonOptions);
            await writer.WriteAsync("<vx-signals>").ConfigureAwait(false);
            await writer.WriteAsync(json).ConfigureAwait(false);
            await writer.WriteAsync("</vx-signals>\n").ConfigureAwait(false);
        }

        // 3. Directive
        if (directives is not null && directives.HasDirectives)
        {
            await writer.WriteAsync("<vx-directive").ConfigureAwait(false);

            if (!string.IsNullOrEmpty(directives.PushUrl))
            {
                await writer.WriteAsync(" push-url=\"").ConfigureAwait(false);
                await writer.WriteAsync(htmlEncoder.Encode(directives.PushUrl)).ConfigureAwait(false);
                await writer.WriteAsync("\"").ConfigureAwait(false);
            }

            if (!string.IsNullOrEmpty(directives.ReplaceUrl))
            {
                await writer.WriteAsync(" replace-url=\"").ConfigureAwait(false);
                await writer.WriteAsync(htmlEncoder.Encode(directives.ReplaceUrl)).ConfigureAwait(false);
                await writer.WriteAsync("\"").ConfigureAwait(false);
            }

            if (!string.IsNullOrEmpty(directives.Focus))
            {
                await writer.WriteAsync(" focus=\"").ConfigureAwait(false);
                await writer.WriteAsync(htmlEncoder.Encode(directives.Focus)).ConfigureAwait(false);
                await writer.WriteAsync("\"").ConfigureAwait(false);
            }

            if (!string.IsNullOrEmpty(directives.Scroll))
            {
                await writer.WriteAsync(" scroll=\"").ConfigureAwait(false);
                await writer.WriteAsync(htmlEncoder.Encode(directives.Scroll)).ConfigureAwait(false);
                await writer.WriteAsync("\"").ConfigureAwait(false);

                if (!string.IsNullOrEmpty(directives.ScrollBehavior))
                {
                    await writer.WriteAsync(" scroll-behavior=\"").ConfigureAwait(false);
                    await writer.WriteAsync(htmlEncoder.Encode(directives.ScrollBehavior)).ConfigureAwait(false);
                    await writer.WriteAsync("\"").ConfigureAwait(false);
                }

                if (!string.IsNullOrEmpty(directives.ScrollBlock))
                {
                    await writer.WriteAsync(" scroll-block=\"").ConfigureAwait(false);
                    await writer.WriteAsync(htmlEncoder.Encode(directives.ScrollBlock)).ConfigureAwait(false);
                    await writer.WriteAsync("\"").ConfigureAwait(false);
                }

                if (!string.IsNullOrEmpty(directives.ScrollInline))
                {
                    await writer.WriteAsync(" scroll-inline=\"").ConfigureAwait(false);
                    await writer.WriteAsync(htmlEncoder.Encode(directives.ScrollInline)).ConfigureAwait(false);
                    await writer.WriteAsync("\"").ConfigureAwait(false);
                }

                if (directives.ScrollFocus)
                {
                    await writer.WriteAsync(" scroll-focus=\"1\"").ConfigureAwait(false);
                }
            }

            if (!string.IsNullOrEmpty(directives.Title))
            {
                await writer.WriteAsync(" title=\"").ConfigureAwait(false);
                await writer.WriteAsync(htmlEncoder.Encode(directives.Title)).ConfigureAwait(false);
                await writer.WriteAsync("\"").ConfigureAwait(false);
            }

            if (!string.IsNullOrEmpty(directives.Announce))
            {
                await writer.WriteAsync(" announce=\"").ConfigureAwait(false);
                await writer.WriteAsync(htmlEncoder.Encode(directives.Announce)).ConfigureAwait(false);
                await writer.WriteAsync("\"").ConfigureAwait(false);
            }

            if (!string.IsNullOrEmpty(directives.Redirect))
            {
                await writer.WriteAsync(" redirect=\"").ConfigureAwait(false);
                await writer.WriteAsync(htmlEncoder.Encode(directives.Redirect)).ConfigureAwait(false);
                await writer.WriteAsync("\"").ConfigureAwait(false);
            }

            if (directives.Reload)
            {
                await writer.WriteAsync(" reload=\"1\"").ConfigureAwait(false);
            }

            await writer.WriteAsync(" />\n").ConfigureAwait(false);
        }
    }

    private static string GetModeString(SwapMode mode) => mode switch
    {
        SwapMode.Morph => "morph",
        SwapMode.Outer => "outer",
        SwapMode.Replace => "replace",
        SwapMode.Inner => "inner",
        SwapMode.Append => "append",
        SwapMode.Prepend => "prepend",
        SwapMode.Before => "before",
        SwapMode.After => "after",
        SwapMode.Remove => "remove",
        _ => "morph"
    };

    private static string GetNamespaceString(VaxelNamespace ns) => ns switch
    {
        VaxelNamespace.Svg => "svg",
        VaxelNamespace.MathMl => "mathml",
        _ => "html"
    };
}

internal sealed class DirectiveBag
{
    public string? PushUrl { get; set; }
    public string? ReplaceUrl { get; set; }
    public string? Focus { get; set; }
    public string? Scroll { get; set; }
    public string? ScrollBehavior { get; set; }
    public string? ScrollBlock { get; set; }
    public string? ScrollInline { get; set; }
    public bool ScrollFocus { get; set; }
    public string? Title { get; set; }
    public string? Announce { get; set; }
    public string? Redirect { get; set; }
    public bool Reload { get; set; }

    public bool HasDirectives =>
        !string.IsNullOrEmpty(PushUrl) ||
        !string.IsNullOrEmpty(ReplaceUrl) ||
        !string.IsNullOrEmpty(Focus) ||
        !string.IsNullOrEmpty(Scroll) ||
        !string.IsNullOrEmpty(Title) ||
        !string.IsNullOrEmpty(Announce) ||
        !string.IsNullOrEmpty(Redirect) ||
        Reload;
}
