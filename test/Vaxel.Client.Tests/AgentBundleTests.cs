using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace Vaxel.Client.Tests;

public sealed class AgentBundleTests
{
    private static string GetBundlePath()
    {
        var primaryPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Vaxel.Client", "wwwroot", "_vaxel", "vaxel.js"));
        if (File.Exists(primaryPath)) return primaryPath;

        var aspNetPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Vaxel.AspNetCore", "wwwroot", "_vaxel", "vaxel.js"));
        if (File.Exists(aspNetPath)) return aspNetPath;

        throw new FileNotFoundException($"Cannot locate vaxel.js bundle at {primaryPath}");
    }

    [Fact]
    public void Bundle_Exists_AndUnder12kGzip()
    {
        var path = GetBundlePath();
        Assert.True(File.Exists(path), "vaxel.js bundle file must exist.");

        var rawBytes = File.ReadAllBytes(path);
        Assert.True(rawBytes.Length > 0, "vaxel.js must not be empty.");

        using var memoryStream = new MemoryStream();
        using (var gzip = new GZipStream(memoryStream, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(rawBytes, 0, rawBytes.Length);
        }

        var gzipSize = memoryStream.ToArray().Length;

        // Size budget constraint: Total bundle ≤ 12 KB (12,288 bytes) gzip
        const int MaxGzipBudget = 12 * 1024;
        Assert.True(gzipSize <= MaxGzipBudget, $"vaxel.js gzip size ({gzipSize} bytes) exceeded budget ({MaxGzipBudget} bytes).");
    }

    [Fact]
    public void Bundle_NoEval_StrictCspCompliant()
    {
        var path = GetBundlePath();
        var code = File.ReadAllText(path, Encoding.UTF8);

        // Security check: Zero eval, new Function, or string execution
        var evalRegex = new Regex(@"\beval\s*\(", RegexOptions.IgnoreCase);
        var functionRegex = new Regex(@"new\s+Function\s*\(", RegexOptions.IgnoreCase);
        var setTimeoutStringRegex = new Regex(@"setTimeout\s*\(\s*['""]", RegexOptions.IgnoreCase);
        var setIntervalStringRegex = new Regex(@"setInterval\s*\(\s*['""]", RegexOptions.IgnoreCase);

        Assert.False(evalRegex.IsMatch(code), "eval() must not appear in vaxel.js (Rule R2, Strict CSP).");
        Assert.False(functionRegex.IsMatch(code), "new Function() must not appear in vaxel.js (Rule R2, Strict CSP).");
        Assert.False(setTimeoutStringRegex.IsMatch(code), "String setTimeout must not appear in vaxel.js (Rule R2, Strict CSP).");
        Assert.False(setIntervalStringRegex.IsMatch(code), "String setInterval must not appear in vaxel.js (Rule R2, Strict CSP).");
    }

    [Fact]
    public void Notice_Exists_AndHas40CharSha()
    {
        var noticePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Vaxel.Client", "NOTICE"));
        Assert.True(File.Exists(noticePath), "NOTICE file must exist.");

        var content = File.ReadAllText(noticePath, Encoding.UTF8);
        var shaMatch = Regex.Match(content, @"\b[0-9a-fA-F]{40}\b");
        Assert.True(shaMatch.Success, "NOTICE file must contain a 40-character commit SHA.");
    }

    [Fact]
    public void Bundle_ContainsAllCoreSubsystems()
    {
        var path = GetBundlePath();
        var code = File.ReadAllText(path, Encoding.UTF8);

        // 1. Signal Store
        Assert.Contains("patchSignals", code);
        Assert.Contains("subscribeSignal", code);
        Assert.Contains("vx-signals", code);
        Assert.Contains("vx-bind", code);

        // 2. DOM Morph & Every Swap Mode
        Assert.Contains("applyPatch", code);
        Assert.Contains("morphElement", code);
        Assert.Contains("vx-preserve", code);
        Assert.Contains("'morph'", code);
        Assert.Contains("'outer'", code);
        Assert.Contains("'replace'", code);
        Assert.Contains("'inner'", code);
        Assert.Contains("'append'", code);
        Assert.Contains("'prepend'", code);
        Assert.Contains("'before'", code);
        Assert.Contains("'after'", code);
        Assert.Contains("'remove'", code);

        // 3. Directives & History
        Assert.Contains("vx-directive", code);
        Assert.Contains("push-url", code);
        Assert.Contains("replace-url", code);
        Assert.Contains("vx-live-region", code);
        Assert.Contains("popstate", code);

        // 4. SSE Client
        Assert.Contains("vx-sse", code);
        Assert.Contains("EventSource", code);

        // 5. Public API Export
        Assert.Contains("global.Vaxel", code);
    }
}
