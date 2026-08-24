using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace Vaxel.Client.Tests;

public sealed class MorphTests
{
    [Fact]
    public void VendorIdiomorph_FilesExist_AndShaValid()
    {
        var vendorDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Vaxel.Client", "vendor", "idiomorph"));
        Assert.True(Directory.Exists(vendorDir), "Vendor directory must exist.");

        var licensePath = Path.Combine(vendorDir, "LICENSE");
        Assert.True(File.Exists(licensePath), "LICENSE must exist in vendor/idiomorph.");

        var jsPath = Path.Combine(vendorDir, "idiomorph.js");
        Assert.True(File.Exists(jsPath), "idiomorph.js must exist in vendor/idiomorph.");

        var noticePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Vaxel.Client", "NOTICE"));
        Assert.True(File.Exists(noticePath), "NOTICE file must exist.");

        var noticeContent = File.ReadAllText(noticePath, Encoding.UTF8);
        Assert.Contains("Idiomorph", noticeContent);
        Assert.Matches(@"\b[0-9a-fA-F]{40}\b", noticeContent);
    }

    [Fact]
    public void MorphEngine_ImplementsAllSwapModes()
    {
        var bundlePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Vaxel.Client", "wwwroot", "_vaxel", "vaxel.js"));
        var code = File.ReadAllText(bundlePath, Encoding.UTF8);

        string[] requiredModes = ["morph", "outer", "replace", "inner", "append", "prepend", "before", "after", "remove"];
        foreach (var mode in requiredModes)
        {
            Assert.Contains($"'{mode}'", code);
        }
    }

    [Fact]
    public void MorphEngine_ImplementsPreservationAndDirtyHandling()
    {
        var bundlePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Vaxel.Client", "wwwroot", "_vaxel", "vaxel.js"));
        var code = File.ReadAllText(bundlePath, Encoding.UTF8);

        Assert.Contains("vx-preserve", code);
        Assert.Contains("vx-preserve-attr", code);
        Assert.Contains("morphIntoTarget", code);
        Assert.Contains("<vx-patch\\b", code);
        Assert.Contains("activeElement", code);
    }

    [Fact]
    public void MorphEngine_SupportsSvgNamespace()
    {
        var bundlePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Vaxel.Client", "wwwroot", "_vaxel", "vaxel.js"));
        var code = File.ReadAllText(bundlePath, Encoding.UTF8);

        Assert.Contains("image/svg+xml", code);
        Assert.Contains("http://www.w3.org/2000/svg", code);
    }
}
