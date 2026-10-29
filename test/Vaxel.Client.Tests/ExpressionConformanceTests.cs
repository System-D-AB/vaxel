using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace Vaxel.Client.Tests;

/// <summary>
/// Runs the jsdom expressions conformance suite (conformance/expressions, 305+ golden cases
/// plus security and binding checks) and verifies the DivKit Apache-2.0 provenance NOTICE.
/// </summary>
public sealed class ExpressionConformanceTests
{
    private readonly ITestOutputHelper _output;

    public ExpressionConformanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void DivKitNotice_Exists_AndHas40CharSha()
    {
        var vendorDir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "conformance", "vendor", "divkit"));

        var noticePath = Path.Combine(vendorDir, "NOTICE");
        var licensePath = Path.Combine(vendorDir, "LICENSE");
        var changesPath = Path.Combine(vendorDir, "CHANGES.md");

        Assert.True(File.Exists(noticePath), "conformance/vendor/divkit/NOTICE must exist.");
        Assert.True(File.Exists(licensePath), "conformance/vendor/divkit/LICENSE must exist.");
        Assert.True(File.Exists(changesPath), "conformance/vendor/divkit/CHANGES.md must exist.");

        var notice = File.ReadAllText(noticePath, Encoding.UTF8);
        Assert.Matches(new Regex(@"\b[0-9a-fA-F]{40}\b"), notice);
        Assert.Contains("Apache License, Version 2.0", notice);
    }

    [Fact]
    public void JsdomExpressionsSuite_Passes()
    {
        var exprDir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "conformance", "expressions"));

        Assert.True(Directory.Exists(exprDir), $"Expressions conformance directory must exist at {exprDir}.");
        Assert.True(File.Exists(Path.Combine(exprDir, "run.mjs")), "conformance/expressions/run.mjs must exist.");

        if (!IsNodeAvailable(exprDir, out var nodeVersion))
        {
            Assert.Fail("Node.js is required to run the jsdom expressions suite but was not found on PATH.");
        }

        _output.WriteLine($"node {nodeVersion}");

        if (!Directory.Exists(Path.Combine(exprDir, "node_modules")))
        {
            var (installCode, installOut) = RunProcess("npm", "install --no-audit --no-fund", exprDir);
            _output.WriteLine(installOut);
            Assert.True(installCode == 0, "npm install failed for the expressions conformance suite.");
        }

        var (code, output) = RunProcess("node", "run.mjs", exprDir);
        _output.WriteLine(output);
        Assert.True(code == 0, $"jsdom expressions conformance suite failed (exit {code}). See output above.");
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
            // falls through
        }

        return false;
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
