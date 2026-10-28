using System.Text;
using Xunit;

namespace Vaxel.Client.Tests;

/// <summary>
/// US-6: docs/04 and the README must not promise an attribute the shipped bundle lacks.
/// This test is the executable half of that promise — every name docs/04 lists is either
/// in the bundle (ImplementedIn10) or explicitly marked "not in 1.0" (NotIn10). A name that
/// is documented, unmarked and absent fails here; so does a name marked "not in 1.0" that
/// has quietly been implemented (the mark must then move).
/// </summary>
public sealed class VocabularyHonestyTests
{
    // Attributes the 1.0 agent implements (present in vaxel.js).
    private static readonly string[] ImplementedIn10 =
    [
        "vx-get", "vx-post", "vx-put", "vx-patch", "vx-delete",
        "vx-target", "vx-vals-", "vx-confirm", "vx-indicator", "vx-disable",
        "vx-sync", "vx-encoding",
        "vx-preserve", "vx-preserve-attr", "vx-overwrite-dirty", "vx-sse",
        "vx-text", "vx-show", "vx-class:", "vx-attr:", "vx-style:", "vx-bind",
        "vx-signals", "vx-signals-if-missing", "vx-persist", "vx-persist-session", "vx-url-sync",
    ];

    // Attributes documented in docs/04 but NOT in the 1.0 bundle. Marked "not in 1.0" in
    // docs/04, the README and parity/SCOREBOARD.md. Implementing any is a later packet.
    private static readonly string[] NotIn10 =
    [
        "vx-on", "vx-swap", "vx-include",
        "vx-debounce", "vx-debounce-leading", "vx-throttle", "vx-delay", "vx-once",
        "vx-prevent", "vx-stop", "vx-capture", "vx-passive",
        "vx-window", "vx-document", "vx-outside",
        "vx-trigger-load", "vx-trigger-load-delay", "vx-trigger-visible", "vx-poll",
        "vx-ignore", "vx-ignore-self", "vx-transition-name",
        "vx-bind-event", "vx-bind-prop", "vx-signal-set:", "vx-match-media:",
        "vx-debug-signals", "vx-url-sync-history",
    ];

    private static string ReadBundle()
    {
        var served = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "Vaxel.AspNetCore", "wwwroot", "_vaxel", "vaxel.js"));
        var client = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "Vaxel.Client", "wwwroot", "_vaxel", "vaxel.js"));

        var path = File.Exists(served) ? served : client;
        Assert.True(File.Exists(path), $"vaxel.js bundle must exist at {path}.");
        return File.ReadAllText(path, Encoding.UTF8);
    }

    [Fact]
    public void EveryImplementedAttribute_IsPresentInBundle()
    {
        var code = ReadBundle();
        foreach (var name in ImplementedIn10)
        {
            Assert.True(code.Contains(name, StringComparison.Ordinal),
                $"docs/04 marks '{name}' implemented in 1.0, but it is absent from vaxel.js.");
        }
    }

    [Fact]
    public void EveryNotIn10Attribute_IsAbsentFromBundle()
    {
        var code = ReadBundle();
        foreach (var name in NotIn10)
        {
            Assert.False(code.Contains(name, StringComparison.Ordinal),
                $"'{name}' is marked 'not in 1.0' but now appears in vaxel.js — move the mark to implemented.");
        }
    }

    [Fact]
    public void ServedAndClientBundles_AreInSync()
    {
        var served = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "Vaxel.AspNetCore", "wwwroot", "_vaxel", "vaxel.js"));
        var client = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "Vaxel.Client", "wwwroot", "_vaxel", "vaxel.js"));

        Assert.True(File.Exists(served), "served bundle must exist.");
        Assert.True(File.Exists(client), "client bundle must exist.");

        // The served copy (embedded by Vaxel.AspNetCore) and the Client copy must be identical;
        // there is no build step syncing them, so a drift here means an edit landed in only one.
        Assert.Equal(
            File.ReadAllText(client, Encoding.UTF8).ReplaceLineEndings("\n"),
            File.ReadAllText(served, Encoding.UTF8).ReplaceLineEndings("\n"));
    }
}
