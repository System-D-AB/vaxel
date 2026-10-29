using System.Text;
using System.Text.Json;
using Vaxel.Expressions;
using Xunit;

namespace Vaxel.AspNetCore.Tests.Expressions;

public sealed class ExpressionValidatorTests
{
    private static string ConformanceDir => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..",
        "conformance", "expressions"));

    [Fact]
    public void CorpusParity_CanonicalTrees_And_StaticRefusals_MatchBrowserResults()
    {
        var casesDir = Path.Combine(ConformanceDir, "cases");
        var resultsPath = Path.Combine(ConformanceDir, "results.browser.json");
        var runtimeOnlyPath = Path.Combine(ConformanceDir, "runtime-only.json");

        Assert.True(Directory.Exists(casesDir), $"Cases directory must exist at {casesDir}");
        Assert.True(File.Exists(resultsPath), $"Browser results file must exist at {resultsPath} (run Vaxel.Client.Tests first).");
        Assert.True(File.Exists(runtimeOnlyPath), $"runtime-only.json must exist at {runtimeOnlyPath}");

        using var resultsDoc = JsonDocument.Parse(File.ReadAllText(resultsPath, Encoding.UTF8));
        var browserResults = resultsDoc.RootElement;

        using var runtimeDoc = JsonDocument.Parse(File.ReadAllText(runtimeOnlyPath, Encoding.UTF8));
        var runtimeOnly = new HashSet<string>(
            runtimeDoc.RootElement.EnumerateArray().Select(e => e.GetString()!),
            StringComparer.Ordinal);

        var seenRuntimeOnly = new HashSet<string>(StringComparer.Ordinal);
        var caseFiles = Directory.GetFiles(casesDir, "*.json").OrderBy(f => f, StringComparer.Ordinal).ToArray();
        Assert.NotEmpty(caseFiles);

        var totalCases = 0;

        foreach (var file in caseFiles)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file, Encoding.UTF8));
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                totalCases++;
                var name = item.GetProperty("name").GetString()!;
                var expr = item.GetProperty("expression").GetString()!;
                var state = item.GetProperty("state");

                Assert.True(browserResults.TryGetProperty(name, out var jsRes),
                    $"Case '{name}' missing from results.browser.json.");

                var jsParseOk = jsRes.GetProperty("parseOk").GetBoolean();
                var csParse = ExpressionParser.ParseTemplateOrExpr(expr);

                Assert.True(jsParseOk == csParse.Ok,
                    $"Parse parity mismatch on '{name}' ({expr}): JS parseOk={jsParseOk}, C# Ok={csParse.Ok} ({csParse.Reason})");

                if (!jsParseOk)
                {
                    var jsCode = jsRes.GetProperty("code").GetString();
                    Assert.Equal("expressions.syntax", jsCode);
                    Assert.Equal("expressions.syntax", csParse.Code);
                    continue;
                }

                var jsCanonical = jsRes.GetProperty("canonical").GetString()!;
                var csCanonical = ExpressionCanonical.ToCanonical(csParse.Value);
                Assert.Equal(jsCanonical, csCanonical);

                var schema = ExpressionTypeChecker.FromJsonState(state, name);
                var csCheck = ExpressionTypeChecker.Check(csParse.Value!, schema);
                var jsEvalOk = jsRes.GetProperty("evalOk").GetBoolean();

                if (jsEvalOk)
                {
                    Assert.True(csCheck.Ok,
                        $"US-2 AC-4 violation on '{name}' ({expr}): client evaluated OK, but server refused with {csCheck.Code}: {csCheck.Reason}");
                }
                else
                {
                    var jsCode = jsRes.GetProperty("code").GetString()!;
                    if (runtimeOnly.Contains(name))
                    {
                        seenRuntimeOnly.Add(name);
                        Assert.True(csCheck.Ok,
                            $"Runtime-only case '{name}' ({expr}) should be accepted statically by the server, but was refused with {csCheck.Code}: {csCheck.Reason}");
                    }
                    else
                    {
                        Assert.False(csCheck.Ok,
                            $"Static refusal case '{name}' ({expr}) was refused by JS with '{jsCode}', but accepted by C# type checker.");
                        Assert.Equal(jsCode, csCheck.Code);
                    }
                }
            }
        }

        Assert.True(totalCases >= 305, $"Expected at least 305 cases, ran {totalCases}.");
        Assert.Equal(runtimeOnly, seenRuntimeOnly);

        UpdateScoreboardExpressionsRow(totalCases);
    }

    private static void UpdateScoreboardExpressionsRow(int totalCases)
    {
        var scoreboardPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "parity", "SCOREBOARD.md"));
        if (!File.Exists(scoreboardPath))
        {
            return;
        }

        var original = File.ReadAllText(scoreboardPath, Encoding.UTF8);
        var vaxelAdditions = totalCases - 305;
        var headlineRow = $"| Expressions corpus (client eval + server AST/type parity) | **{totalCases} / {totalCases}** | **{totalCases} / {totalCases}** (305 lifted + {vaxelAdditions} vaxel additions; 0 failing) |";
        var oursRow = $"| Closed expressions corpus (DivKit + Centralen + vaxel, JS eval & C# AST/type parity) | {totalCases} / {totalCases} | ✅ {totalCases} / {totalCases} |";

        var updated = System.Text.RegularExpressions.Regex.Replace(
            original,
            @"^\| Expressions corpus \(client eval \+ server AST/type parity\) \|.*$",
            headlineRow,
            System.Text.RegularExpressions.RegexOptions.Multiline);

        updated = System.Text.RegularExpressions.Regex.Replace(
            updated,
            @"^\| Closed expressions corpus \(DivKit \+ Centralen \+ vaxel, JS eval & C# AST/type parity\) \|.*$",
            oursRow,
            System.Text.RegularExpressions.RegexOptions.Multiline);

        if (!string.Equals(original, updated, StringComparison.Ordinal))
        {
            File.WriteAllText(scoreboardPath, updated, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        Assert.Contains(headlineRow, updated);
        Assert.Contains(oursRow, updated);
    }
}
