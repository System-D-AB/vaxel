using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Vaxel;

/// <summary>
/// Core helpers for vaxel request routing and response handling.
/// </summary>
public static class Vaxel
{
    /// <summary>
    /// Determines whether the given HTTP context represents an active vaxel agent request.
    /// </summary>
    public static bool IsVaxelRequest(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        var headers = httpContext.Request.Headers;

        if (headers.TryGetValue("VX-Protocol", out var protocolValues))
        {
            var protocolStr = protocolValues.ToString().Trim();
            if (!string.IsNullOrEmpty(protocolStr))
            {
                var dotIndex = protocolStr.IndexOf('.');
                var majorStr = dotIndex >= 0 ? protocolStr[..dotIndex] : protocolStr;
                if (!int.TryParse(majorStr, out var major) || major != 1)
                {
                    return false;
                }
            }
        }

        if (headers.TryGetValue("VX-Request", out var requestValues))
        {
            return string.Equals(requestValues.ToString().Trim(), "1", StringComparison.Ordinal);
        }

        return false;
    }

    /// <summary>
    /// Executes the patch branch if the request is a vaxel agent request; otherwise executes the page branch.
    /// </summary>
    public static IActionResult PageOrPatch(
        HttpContext httpContext,
        Func<IActionResult> page,
        Func<IActionResult> patch)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(patch);

        return IsVaxelRequest(httpContext) ? patch() : page();
    }

    /// <summary>
    /// Executes the patch branch if the request is a vaxel agent request; otherwise executes the page branch.
    /// </summary>
    public static async Task<IActionResult> PageOrPatch(
        HttpContext httpContext,
        Func<Task<IActionResult>> page,
        Func<Task<IActionResult>> patch)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(patch);

        return IsVaxelRequest(httpContext)
            ? await patch().ConfigureAwait(false)
            : await page().ConfigureAwait(false);
    }

    /// <summary>
    /// Executes the patch branch if the request is a vaxel agent request; otherwise executes the page branch.
    /// </summary>
    public static Task<IActionResult> PageOrPatch(
        HttpContext httpContext,
        Func<IActionResult> page,
        Func<Task<IActionResult>> patch) =>
        PageOrPatch(httpContext, () => Task.FromResult(page()), patch);

    /// <summary>
    /// Executes the patch branch if the request is a vaxel agent request; otherwise executes the page branch.
    /// </summary>
    public static Task<IActionResult> PageOrPatch(
        HttpContext httpContext,
        Func<Task<IActionResult>> page,
        Func<IActionResult> patch) =>
        PageOrPatch(httpContext, page, () => Task.FromResult(patch()));

    /// <summary>
    /// Executes the patch branch if the request is a vaxel agent request; otherwise executes the page branch.
    /// </summary>
    public static IResult PageOrPatch(
        HttpContext httpContext,
        Func<IResult> page,
        Func<IResult> patch)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(patch);

        return IsVaxelRequest(httpContext) ? patch() : page();
    }

    /// <summary>
    /// Executes the patch branch if the request is a vaxel agent request; otherwise executes the page branch.
    /// </summary>
    public static async Task<IResult> PageOrPatch(
        HttpContext httpContext,
        Func<Task<IResult>> page,
        Func<Task<IResult>> patch)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(patch);

        return IsVaxelRequest(httpContext)
            ? await patch().ConfigureAwait(false)
            : await page().ConfigureAwait(false);
    }

    /// <summary>
    /// Executes the patch branch if the request is a vaxel agent request; otherwise executes the page branch.
    /// </summary>
    public static Task<IResult> PageOrPatch(
        HttpContext httpContext,
        Func<IResult> page,
        Func<Task<IResult>> patch) =>
        PageOrPatch(httpContext, () => Task.FromResult(page()), patch);

    /// <summary>
    /// Executes the patch branch if the request is a vaxel agent request; otherwise executes the page branch.
    /// </summary>
    public static Task<IResult> PageOrPatch(
        HttpContext httpContext,
        Func<Task<IResult>> page,
        Func<IResult> patch) =>
        PageOrPatch(httpContext, page, () => Task.FromResult(patch()));
}
