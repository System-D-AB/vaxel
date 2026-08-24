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
}
