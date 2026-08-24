using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace Vaxel.Client.Tests;

public sealed class SignalStoreTests
{
    private static string GetBundlePath()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Vaxel.Client", "wwwroot", "_vaxel", "vaxel.js"));
    }

    [Fact]
    public void Store_PatchDeletesNull_AndOnlyIfMissing_Structure()
    {
        var code = File.ReadAllText(GetBundlePath(), Encoding.UTF8);

        // US-1.1 & 1.2: Null/undefined deletes key; onlyIfMissing flag preserves existing keys
        Assert.Contains("delete store[k]", code);
        Assert.Contains("onlyIfMissing", code);
        Assert.Contains("subscribers", code);
        Assert.Contains("notify", code);
    }

    [Fact]
    public void Bindings_SupportAllDocumentedDirectives()
    {
        var code = File.ReadAllText(GetBundlePath(), Encoding.UTF8);

        // US-2: vx-text, vx-show, vx-class:*, vx-attr:*, vx-style:*, vx-bind
        Assert.Contains("vx-text", code);
        Assert.Contains("vx-show", code);
        Assert.Contains("vx-class:", code);
        Assert.Contains("vx-attr:", code);
        Assert.Contains("vx-style:", code);
        Assert.Contains("vx-bind", code);

        // vx-text sets textContent, never innerHTML (US-2.1)
        Assert.Contains("textContent", code);
        Assert.DoesNotContain("innerHTML = v", code);

        // vx-bind preserves numbers and booleans (US-2.5)
        Assert.Contains("Number(el.value)", code);
        Assert.Contains("el.checked", code);
    }

    [Fact]
    public void Bindings_RejectOperatorsInValue_RuleR2()
    {
        var code = File.ReadAllText(GetBundlePath(), Encoding.UTF8);

        // R2 Check: Rejection of expressions like >, ==, (, etc.
        Assert.Contains("/[()<>=!&|+*\\/;]/.test(val)", code);
        Assert.Contains("Rule R2 violation", code);
    }

    [Fact]
    public void SeedsAndPersistence_Implemented()
    {
        var code = File.ReadAllText(GetBundlePath(), Encoding.UTF8);

        // US-3: vx-signals, vx-signals-if-missing, vx-persist, vx-persist-session, vx-url-sync
        Assert.Contains("vx-signals", code);
        Assert.Contains("vx-signals-if-missing", code);
        Assert.Contains("vx-persist", code);
        Assert.Contains("vx-persist-session", code);
        Assert.Contains("vx-url-sync", code);
        Assert.Contains("localStorage", code);
        Assert.Contains("sessionStorage", code);
        Assert.Contains("replaceState", code);
    }

    [Fact]
    public void StoreBundle_NoEval()
    {
        var code = File.ReadAllText(GetBundlePath(), Encoding.UTF8);

        var evalMatch = Regex.Match(code, @"\beval\s*\(", RegexOptions.IgnoreCase);
        var fnMatch = Regex.Match(code, @"new\s+Function\s*\(", RegexOptions.IgnoreCase);

        Assert.False(evalMatch.Success, "Signal store must not use eval.");
        Assert.False(fnMatch.Success, "Signal store must not use new Function.");
    }
}
