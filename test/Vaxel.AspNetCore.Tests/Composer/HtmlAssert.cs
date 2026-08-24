using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Html;

namespace Vaxel.AspNetCore.Tests;

internal static class HtmlAssert
{
    public static string ToHtml(IHtmlContent content)
    {
        using var writer = new StringWriter();
        content.WriteTo(writer, HtmlEncoder.Default);
        return writer.ToString();
    }

    public static string Normalize(string html)
    {
        html = Regex.Replace(html, @">\s+<", "><");
        html = Regex.Replace(
            html,
            @"<([a-zA-Z][\w:-]*)((?:\s+[\w:-]+=""[^""]*"")*)\s*(/?)>",
            MatchTag);
        return html.Trim();
    }

    public static string InnerById(string html, string id)
    {
        var pattern = @"<(?<tag>[\w-]+)[^>]*\sid=""" + Regex.Escape(id) + @"""[^>]*>(?<inner>[\s\S]*?)</\k<tag>>";
        var match = Regex.Match(html, pattern);
        if (!match.Success)
        {
            throw new InvalidOperationException($"Element id='{id}' not found in:{Environment.NewLine}{html}");
        }

        return match.Groups["inner"].Value;
    }

    private static string MatchTag(Match match)
    {
        var tag = match.Groups[1].Value;
        var attrs = Regex.Matches(match.Groups[2].Value, @"([\w:-]+)=""([^""]*)""")
            .Select(a => a.Groups[1].Value + "=\"" + a.Groups[2].Value + "\"")
            .OrderBy(a => a, StringComparer.Ordinal);
        var closing = match.Groups[3].Value == "/" ? " />" : ">";
        var attrStr = attrs.Any() ? " " + string.Join(" ", attrs) : "";
        return "<" + tag + attrStr + closing;
    }
}
