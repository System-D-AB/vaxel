using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Vaxel.Analyzers;
using Xunit;

namespace Vaxel.AspNetCore.Tests.Analyzers;

public sealed class AnalyzerTests
{
    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var references = new MetadataReference[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            // System.Runtime facade so BCL types (StringBuilder, Dictionary) resolve by their fully-qualified name.
            MetadataReference.CreateFromFile(Path.Combine(
                Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(typeof(System.Text.StringBuilder).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Collections.Generic.Dictionary<,>).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ISignalReader).Assembly.Location)
        };

        var compilation = CSharpCompilation.Create("TestAssembly", [syntaxTree], references);
        var analyzer = new VaxelDiagnosticAnalyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers([analyzer]);

        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    [Fact]
    public async Task Analyzer_WarnsIsAdmin_OnSignalType()
    {
        const string source = @"
namespace TestApp;

public sealed record UserSignals(string Tab, bool IsAdmin);
";
        var diagnostics = await AnalyzeAsync(source);
        var warning = Assert.Single(diagnostics, d => d.Id == VaxelDiagnosticAnalyzer.Vaxel001Id);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Contains("IsAdmin", warning.GetMessage());
    }

    [Fact]
    public async Task Analyzer_WarnsIsAdmin_OnSignalReader()
    {
        const string source = @"
using Vaxel;

public class TestHandler
{
    public void Handle(ISignalReader reader)
    {
        reader.TryGet<bool>(""isAdmin"", out var admin);
    }
}
";
        var diagnostics = await AnalyzeAsync(source);
        var warning = Assert.Single(diagnostics, d => d.Id == VaxelDiagnosticAnalyzer.Vaxel001Id);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Contains("isAdmin", warning.GetMessage());
    }

    [Fact]
    public async Task Analyzer_ErrorsClassTarget_OnPatchBuilder()
    {
        const string source = @"
using Vaxel;

public class TestHandler
{
    public void Handle()
    {
        Patch.Ok().Replace("".invalid-class"", null);
    }
}
";
        var diagnostics = await AnalyzeAsync(source);
        var error = Assert.Single(diagnostics, d => d.Id == VaxelDiagnosticAnalyzer.Vaxel003Id);
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Contains(".invalid-class", error.GetMessage());
    }

    [Fact]
    public async Task Analyzer_ValidTarget_NoDiagnostics()
    {
        const string source = @"
using Vaxel;

public class TestHandler
{
    public void Handle()
    {
        Patch.Ok().Replace(""#pane"", null);
    }
}
";
        var diagnostics = await AnalyzeAsync(source);
        Assert.Empty(diagnostics.Where(d => d.Id.StartsWith("VAXEL")));
    }

    // US-1: VAXEL003 must fire only on a Vaxel patch builder's target argument,
    // never on a same-named method of an unrelated receiver type.

    [Fact]
    public async Task Analyzer_StringBuilderAppend_NoDiagnostic()
    {
        const string source = @"
using System.Text;

public class TestHandler
{
    public void Handle()
    {
        var sb = new StringBuilder();
        sb.Append(""<div class='x'>"");
    }
}
";
        var diagnostics = await AnalyzeAsync(source);
        Assert.Empty(diagnostics.Where(d => d.Id == VaxelDiagnosticAnalyzer.Vaxel003Id));
    }

    [Fact]
    public async Task Analyzer_StringReplace_NoDiagnostic()
    {
        const string source = @"
public class TestHandler
{
    public string Handle(string path)
    {
        return path.Replace(""."", ""_"");
    }
}
";
        var diagnostics = await AnalyzeAsync(source);
        Assert.Empty(diagnostics.Where(d => d.Id == VaxelDiagnosticAnalyzer.Vaxel003Id));
    }

    [Fact]
    public async Task Analyzer_DictionaryAppendLikeCall_NoDiagnostic()
    {
        // A method named Append on a user type that is not a PatchBuilder.
        const string source = @"
public sealed class HeaderBag
{
    public void Append(string name, string value) { }
}

public class TestHandler
{
    public void Handle()
    {
        var headers = new HeaderBag();
        headers.Append(""X-Frame-Options"", ""DENY"");
    }
}
";
        var diagnostics = await AnalyzeAsync(source);
        Assert.Empty(diagnostics.Where(d => d.Id == VaxelDiagnosticAnalyzer.Vaxel003Id));
    }

    [Fact]
    public async Task Analyzer_UnresolvableReceiver_NoDiagnostic()
    {
        // Receiver type is undefined, so the analyser cannot resolve it: stay quiet (AC-3).
        const string source = @"
public class TestHandler
{
    public void Handle()
    {
        thing.Replace(""not-an-id"", null);
    }
}
";
        var diagnostics = await AnalyzeAsync(source);
        Assert.Empty(diagnostics.Where(d => d.Id == VaxelDiagnosticAnalyzer.Vaxel003Id));
    }

    [Fact]
    public async Task Analyzer_PatchBuilderInvalidTarget_StillFires()
    {
        const string source = @"
using Vaxel;

public class TestHandler
{
    public void Handle()
    {
        Patch.Ok().Append(""not-an-id"", null);
    }
}
";
        var diagnostics = await AnalyzeAsync(source);
        var error = Assert.Single(diagnostics, d => d.Id == VaxelDiagnosticAnalyzer.Vaxel003Id);
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
    }
}
