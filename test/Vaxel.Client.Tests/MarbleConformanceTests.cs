using System.Diagnostics;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace Vaxel.Client.Tests;

/// <summary>
/// Runs the jsdom marble &amp; trigger-pipeline conformance suite under <c>conformance/marbles/</c>
/// against the shipped <c>vaxel.js</c> bundle.
/// </summary>
public sealed class MarbleConformanceTests
{
    private readonly ITestOutputHelper _output;

    public MarbleConformanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static string SuiteDir => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..",
        "conformance", "marbles"));

    [Fact]
    public void JsdomMarbleSuite_Passes()
    {
        Assert.True(Directory.Exists(SuiteDir), $"Marble suite directory missing at {SuiteDir}.");
        Assert.True(File.Exists(Path.Combine(SuiteDir, "run.mjs")), "conformance/marbles/run.mjs must exist.");

        if (!Directory.Exists(Path.Combine(SuiteDir, "node_modules")))
        {
            var (installCode, installOut) = RunProcess("npm", "install --no-audit --no-fund", SuiteDir);
            _output.WriteLine(installOut);
            Assert.True(installCode == 0, "npm install failed for the marbles conformance suite.");
        }

        var (code, output) = RunProcess("node", "run.mjs", SuiteDir);
        _output.WriteLine(output);
        Assert.True(code == 0, $"jsdom marbles conformance suite failed (exit {code}).\n{output}");

        var resultsPath = Path.Combine(SuiteDir, "results.browser.json");
        Assert.True(File.Exists(resultsPath), "results.browser.json must be emitted by run.mjs.");

        var scoreboardPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "parity", "SCOREBOARD.md"));
        var scoreboard = File.ReadAllText(scoreboardPath, Encoding.UTF8);

        foreach (var op in new[] { "debounce", "throttle", "delay", "once", "distinct" })
        {
            Assert.Contains($"conformance/marbles/{op}.json", scoreboard);
        }
    }

    private static (int ExitCode, string Output) RunProcess(string fileName, string arguments, string workingDirectory)
    {
        var isWindows = OperatingSystem.IsWindows();
        var psi = new ProcessStartInfo
        {
            FileName = isWindows ? "cmd.exe" : fileName,
            Arguments = isWindows ? $"/c {fileName} {arguments}" : arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start '{fileName}'.");

        var sb = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) sb.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) sb.AppendLine(e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (!process.WaitForExit(120_000))
        {
            try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
            return (-1, sb.ToString() + "\n[timed out after 120s]");
        }

        process.WaitForExit();
        return (process.ExitCode, sb.ToString());
    }
}
