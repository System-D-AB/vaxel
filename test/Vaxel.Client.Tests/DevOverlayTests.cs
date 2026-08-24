using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace Vaxel.Client.Tests;

public sealed class DevOverlayTests
{
    [Fact]
    public void ProductionBundle_NoOverlay()
    {
        var prodBundlePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Vaxel.Client", "wwwroot", "_vaxel", "vaxel.js"));
        var prodCode = File.ReadAllText(prodBundlePath, Encoding.UTF8);

        // Production bundle must not contain dev overlay inspector
        Assert.DoesNotContain("vaxel-dev-overlay", prodCode);
        Assert.DoesNotContain("vaxel Dev Inspector", prodCode);
    }

    [Fact]
    public void DevOverlay_ContainsExpectedFeatures()
    {
        var devBundlePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Vaxel.Client", "wwwroot", "_vaxel", "vaxel.dev.js"));
        Assert.True(File.Exists(devBundlePath), "vaxel.dev.js must exist.");

        var devCode = File.ReadAllText(devBundlePath, Encoding.UTF8);
        Assert.Contains("vaxel-dev-overlay", devCode);
        Assert.Contains("vaxel Dev Inspector", devCode);
        Assert.Contains("vx:after-apply", devCode);
        Assert.Contains("vx:error", devCode);
        Assert.Contains("vx:signals-changed", devCode);

        // Strict CSP: Zero eval in dev bundle as well
        var evalMatch = Regex.Match(devCode, @"\beval\s*\(", RegexOptions.IgnoreCase);
        var fnMatch = Regex.Match(devCode, @"new\s+Function\s*\(", RegexOptions.IgnoreCase);
        Assert.False(evalMatch.Success, "vaxel.dev.js must not contain eval.");
        Assert.False(fnMatch.Success, "vaxel.dev.js must not contain new Function.");
    }
}
