using System.Diagnostics;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace Vaxel.Client.Tests;

/// <summary>
/// Runs the jsdom regression fixtures (conformance/regression) that exercise the shipped
/// client agent in a DOM — the cheapest level that can observe US-3 (directive decoding),
/// US-4 (GET form fields), US-5 (morph of unrelated trees) and US-7 (antiforgery header name).
/// The .NET bundle-grep tests cannot see these behaviours.
/// </summary>
public sealed class RegressionConformanceTests
{
    private readonly ITestOutputHelper _output;

    public RegressionConformanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void JsdomRegressionSuite_Passes()
    {
        var regressionDir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "conformance", "regression"));

        Assert.True(Directory.Exists(regressionDir),
            $"Regression suite directory must exist at {regressionDir}.");
        Assert.True(File.Exists(Path.Combine(regressionDir, "run.mjs")),
            "conformance/regression/run.mjs must exist.");

        if (!IsNodeAvailable(regressionDir, out var nodeVersion))
        {
            // Node is required to run the agent in a DOM. A gate that silently skips is not a
            // gate; make the absence explicit so CI is configured with Node rather than hiding it.
            Assert.Fail("Node.js is required to run the jsdom regression suite but was not found on PATH.");
        }

        _output.WriteLine($"node {nodeVersion}");

        // Ensure jsdom is installed (node_modules is gitignored).
        if (!Directory.Exists(Path.Combine(regressionDir, "node_modules")))
        {
            var (installCode, installOut) = RunProcess("npm", "install --no-audit --no-fund", regressionDir);
            _output.WriteLine(installOut);
            Assert.True(installCode == 0, "npm install failed for the regression suite.");
        }

        var (code, output) = RunProcess("node", "run.mjs", regressionDir);
        _output.WriteLine(output);
        Assert.True(code == 0, $"jsdom regression suite failed (exit {code}). See output above.");
    }

    private static bool IsNodeAvailable(string cwd, out string version)
    {
        version = string.Empty;
        try
        {
            var (code, output) = RunProcess("node", "--version", cwd);
            if (code == 0)
            {
                version = output.Trim();
                return true;
            }
        }
        catch
        {
            // falls through to false
        }

        return false;
    }

    private static (int ExitCode, string Output) RunProcess(string fileName, string arguments, string workingDirectory)
    {
        // On Windows, npm/node are often .cmd shims; run through the shell resolver.
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
