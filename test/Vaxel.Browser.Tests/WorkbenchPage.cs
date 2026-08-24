using Microsoft.Playwright;

namespace Vaxel.Browser.Tests;

internal static class WorkbenchPage
{
    public static async Task GotoAsync(IPage page, Uri baseAddress, string path = "/")
    {
        var url = new Uri(baseAddress, path).ToString();
        await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => globalThis.Vaxel && typeof globalThis.Vaxel.processDocument === 'function'");
    }

    public static ILocator Nav(IPage page, string href) =>
        page.Locator($"nav a[href='{href}']");

    public static Task ClickTabAsync(IPage page, string tab) =>
        Nav(page, $"/?tab={tab}").ClickAsync();

    public static async Task SignInAsync(IPage page, Uri baseAddress, string who)
    {
        await GotoAsync(page, baseAddress, "/login");
        var label = who.Equals("Alice", StringComparison.OrdinalIgnoreCase)
            ? "Sign in as Alice"
            : "Sign in as Bob";
        await page.GetByRole(AriaRole.Button, new() { Name = label }).ClickAsync();
        await page.WaitForURLAsync(url => new Uri(url).AbsolutePath == "/");
        await page.WaitForFunctionAsync("() => globalThis.Vaxel && typeof globalThis.Vaxel.processDocument === 'function'");
    }

    public static bool IsPatch(IResponse response)
    {
        if (!response.Headers.TryGetValue("content-type", out var contentType))
        {
            return false;
        }

        return contentType.Contains("vnd.vaxel-patch", StringComparison.OrdinalIgnoreCase);
    }
}
