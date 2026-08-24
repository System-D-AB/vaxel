using System.Net;
using System.Text.RegularExpressions;

namespace Vaxel.Testing;

public sealed class VaxelParityException : Exception
{
    public VaxelParityException(string message) : base(message) { }
}

public static class VaxelParity
{
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex CommentRegex = new(@"<!--[\s\S]*?-->", RegexOptions.Compiled);
    private static readonly Regex AntiforgeryRegex = new(
        @"(<input[^>]*name=[""']__RequestVerificationToken[""'][^>]*value=[""'])[^""']*([""'][^>]*>)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Asserts that the inner HTML of the named region produced by a full page GET matches
    /// the inner HTML produced by a patch GET (Rule R3).
    /// </summary>
    public static async Task AssertAsync(
        HttpClient client,
        string pageUrl,
        string patchUrl,
        string regionId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(pageUrl);
        ArgumentNullException.ThrowIfNull(patchUrl);
        ArgumentNullException.ThrowIfNull(regionId);

        var targetId = regionId.TrimStart('#');
        var targetSelector = $"#{targetId}";

        // 1. Fetch full page (No-JS / direct browser GET)
        var pageResponse = await client.GetAsync(pageUrl, ct);
        if (!pageResponse.IsSuccessStatusCode)
        {
            throw new VaxelParityException($"Full page GET '{pageUrl}' failed with status {pageResponse.StatusCode}.");
        }
        var pageHtml = await pageResponse.Content.ReadAsStringAsync(ct);

        // 2. Fetch patch document (Agent GET with VX-Request: 1)
        var patchRequest = new HttpRequestMessage(HttpMethod.Get, patchUrl);
        patchRequest.Headers.Add("VX-Request", "1");
        var patchResponse = await client.SendAsync(patchRequest, ct);
        if (!patchResponse.IsSuccessStatusCode)
        {
            throw new VaxelParityException($"Patch GET '{patchUrl}' failed with status {patchResponse.StatusCode}.");
        }
        var patchHtml = await patchResponse.Content.ReadAsStringAsync(ct);

        // 3. Extract region from full page
        var pageRegionContent = ExtractRegionFromPage(pageHtml, targetId);
        if (pageRegionContent is null)
        {
            throw new VaxelParityException($"Region id '{targetId}' was not found in full page HTML from '{pageUrl}'.");
        }

        // 4. Extract region from patch document
        var patchRegionContent = ExtractRegionFromPatch(patchHtml, targetSelector);
        if (patchRegionContent is null)
        {
            throw new VaxelParityException($"Target selector '{targetSelector}' was not found in patch document from '{patchUrl}'.");
        }

        // 5. Compare normalized HTML
        var normPage = Normalize(pageRegionContent);
        var normPatch = Normalize(patchRegionContent);

        if (!string.Equals(normPage, normPatch, StringComparison.Ordinal))
        {
            throw new VaxelParityException(
                $"Page-to-Patch parity mismatch for region '{targetSelector}'.\n\n" +
                $"--- Expected (Full Page): ---\n{normPage}\n\n" +
                $"--- Actual (Patch Document): ---\n{normPatch}");
        }
    }

    private static string? ExtractRegionFromPage(string html, string regionId)
    {
        var open = Regex.Match(
            html,
            $@"<(?<tag>[a-zA-Z0-9]+)[^>]*\bid=[""']{Regex.Escape(regionId)}[""'][^>]*>",
            RegexOptions.IgnoreCase);
        if (!open.Success)
        {
            return null;
        }

        var tag = open.Groups["tag"].Value;
        var start = open.Index + open.Length;
        var openRx = new Regex($@"<{Regex.Escape(tag)}\b", RegexOptions.IgnoreCase);
        var closeRx = new Regex($@"</{Regex.Escape(tag)}>", RegexOptions.IgnoreCase);
        var depth = 1;
        var i = start;
        while (i < html.Length && depth > 0)
        {
            var nextOpen = openRx.Match(html, i);
            var nextClose = closeRx.Match(html, i);
            if (!nextClose.Success)
            {
                return null;
            }

            if (nextOpen.Success && nextOpen.Index < nextClose.Index)
            {
                depth++;
                i = nextOpen.Index + nextOpen.Length;
            }
            else
            {
                depth--;
                if (depth == 0)
                {
                    return html[start..nextClose.Index];
                }

                i = nextClose.Index + nextClose.Length;
            }
        }

        return null;
    }

    private static string? ExtractRegionFromPatch(string html, string targetSelector)
    {
        var pattern = $@"<vx-patch[^>]*\btarget=[""']{Regex.Escape(targetSelector)}[""'][^>]*>(?<inner>[\s\S]*?)</vx-patch>";
        var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["inner"].Value : null;
    }

    private static string Normalize(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        var stripped = CommentRegex.Replace(html, "");
        var masked = AntiforgeryRegex.Replace(stripped, "$1__MASKED__$2");
        var collapsed = WhitespaceRegex.Replace(masked.Trim(), " ");
        return collapsed;
    }
}
