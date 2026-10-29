using System.Diagnostics;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace Vaxel.Client.Tests;

/// <summary>
/// Runs the jsdom signal store conformance fixtures (conformance/signals) that exercise
/// the v2.0 auto-tracking signal engine, private scheduling queue, window.Vaxel shim,
/// and null/absence semantics.
/// </summary>
public sealed class SignalConformanceTests
{
    private readonly ITestOutputHelper _output;

    public SignalConformanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void JsdomSignalsSuite_Passes()
    {
        var signalsDir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "conformance", "signals"));

        Assert.True(Directory.Exists(signalsDir),
            $"Signals conformance directory must exist at {signalsDir}.");
        Assert.True(File.Exists(Path.Combine(signalsDir, "run.mjs")),
            "conformance/signals/run.mjs must exist.");

        if (!IsNodeAvailable(signalsDir, out var nodeVersion))
        {
            Assert.Fail("Node.js is required to run the jsdom signals suite but was not found on PATH.");
        }

        _output.WriteLine($"node {nodeVersion}");

        if (!Directory.Exists(Path.Combine(signalsDir, "node_modules")))
        {
            var (installCode, installOut) = RunProcess("npm", "install --no-audit --no-fund", signalsDir);
            _output.WriteLine(installOut);
            Assert.True(installCode == 0, "npm install failed for the signals conformance suite.");
        }

        var (code, output) = RunProcess("node", "run.mjs", signalsDir);
        _output.WriteLine(output);
        Assert.True(code == 0, $"jsdom signals conformance suite failed (exit {code}). See output above.");
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
