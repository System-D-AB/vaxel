using System;
using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Vaxel.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class VaxelDiagnosticAnalyzer : DiagnosticAnalyzer
{
    public const string Vaxel001Id = "VAXEL001";
    public const string Vaxel002Id = "VAXEL002";
    public const string Vaxel003Id = "VAXEL003";

    private static readonly DiagnosticDescriptor Vaxel001Rule = new(
        Vaxel001Id,
        "Signals must not carry authorization decisions",
        "Signal property or query '{0}' appears to represent authorization or role state. Per Rule R2/R4 and docs/06, signals are untrusted client state and must not be used for security decisions.",
        "Security",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor Vaxel002Rule = new(
        Vaxel002Id,
        "Triggers must only sit on degradable elements",
        "Trigger attribute '{0}' is placed on a non-degradable element '<{1}>'. Per Rule R3, triggers must only sit on <a>, <form>, or <button>.",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor Vaxel003Rule = new(
        Vaxel003Id,
        "Target must be a single #id selector",
        "Target selector '{0}' is invalid. Targets must match single '#id' format (^#[A-Za-z][\\w:-]*$).",
        "Correctness",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly Regex AuthWordRegex = new(
        @"\b(IsAdmin|Role|Permission|Can[A-Z]\w*|IsAuthorized|IsSuperUser)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex ValidIdTargetRegex = new(
        @"^#[A-Za-z][\w:-]*$",
        RegexOptions.CultureInvariant);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Vaxel001Rule, Vaxel002Rule, Vaxel003Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // 1. Analyze properties/parameters in records/classes marked or used as signal types
        context.RegisterSyntaxNodeAction(AnalyzePropertyDeclaration, SyntaxKind.PropertyDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeParameterDeclaration, SyntaxKind.Parameter);

        // 2. Analyze method invocations (ISignalReader.Get/TryGet, Patch.Replace, etc.)
        context.RegisterSyntaxNodeAction(AnalyzeInvocationExpression, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeParameterDeclaration(SyntaxNodeAnalysisContext context)
    {
        var param = (ParameterSyntax)context.Node;
        var paramName = param.Identifier.Text;

        if (AuthWordRegex.IsMatch(paramName))
        {
            var parentType = param.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
            if (parentType is not null && (parentType.Identifier.Text.EndsWith("Signals", StringComparison.OrdinalIgnoreCase) ||
                                          parentType.AttributeLists.Any(al => al.Attributes.Any(a => a.Name.ToString().Contains("Signals")))))
            {
                var diagnostic = Diagnostic.Create(Vaxel001Rule, param.Identifier.GetLocation(), paramName);
                context.ReportDiagnostic(diagnostic);
            }
        }
    }

    private static void AnalyzePropertyDeclaration(SyntaxNodeAnalysisContext context)
    {
        var propertyDecl = (PropertyDeclarationSyntax)context.Node;
        var propName = propertyDecl.Identifier.Text;

        if (AuthWordRegex.IsMatch(propName))
        {
            // Check if parent type or parameter has [FromSignals] or is ShellSignals
            var parentType = propertyDecl.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
            if (parentType is not null && (parentType.Identifier.Text.EndsWith("Signals", StringComparison.OrdinalIgnoreCase) ||
                                          parentType.AttributeLists.Any(al => al.Attributes.Any(a => a.Name.ToString().Contains("Signals")))))
            {
                var diagnostic = Diagnostic.Create(Vaxel001Rule, propertyDecl.Identifier.GetLocation(), propName);
                context.ReportDiagnostic(diagnostic);
            }
        }
    }

    private static void AnalyzeInvocationExpression(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var memberAccess = invocation.Expression as MemberAccessExpressionSyntax;
        if (memberAccess is null) return;

        var methodName = memberAccess.Name.Identifier.Text;

        // ISignalReader.TryGet("isAdmin", out ...) / Get("isAdmin", ...)
        if ((methodName == "TryGet" || methodName == "Get") && invocation.ArgumentList.Arguments.Count > 0)
        {
            var firstArg = invocation.ArgumentList.Arguments[0].Expression as LiteralExpressionSyntax;
            if (firstArg?.Token.Value is string keyName && AuthWordRegex.IsMatch(keyName))
            {
                var diagnostic = Diagnostic.Create(Vaxel001Rule, firstArg.GetLocation(), keyName);
                context.ReportDiagnostic(diagnostic);
            }
        }

        // Patch.Replace / Outer / etc. target validation.
        // Only fire when the invocation genuinely resolves to a Vaxel.PatchBuilder method:
        // the same method names (Append, Replace, Remove, …) exist on StringBuilder,
        // IHeaderDictionary, string and countless other types, and must not be flagged.
        if ((methodName == "Replace" || methodName == "Outer" || methodName == "ReplaceHard" ||
             methodName == "Inner" || methodName == "Append" || methodName == "Prepend" ||
             methodName == "Before" || methodName == "After" || methodName == "Remove" ||
             methodName == "Focus") && invocation.ArgumentList.Arguments.Count > 0)
        {
            if (!IsPatchBuilderInvocation(context, invocation))
            {
                return;
            }

            var firstArg = invocation.ArgumentList.Arguments[0].Expression as LiteralExpressionSyntax;
            if (firstArg?.Token.Value is string targetStr)
            {
                var trimmed = targetStr.Trim();
                if (trimmed != "top" && !ValidIdTargetRegex.IsMatch(trimmed))
                {
                    var diagnostic = Diagnostic.Create(Vaxel003Rule, firstArg.GetLocation(), targetStr);
                    context.ReportDiagnostic(diagnostic);
                }
            }
        }
    }

    private const string PatchBuilderMetadataName = "Vaxel.PatchBuilder";

    /// <summary>
    /// Resolves the invoked method and returns true only when it is declared on
    /// <c>Vaxel.PatchBuilder</c>. Returns false when the symbol cannot be resolved,
    /// so an analyser that cannot see the receiver type stays quiet (AC-3).
    /// </summary>
    private static bool IsPatchBuilderInvocation(SyntaxNodeAnalysisContext context, InvocationExpressionSyntax invocation)
    {
        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);
        var method = symbolInfo.Symbol as IMethodSymbol;

        // Unresolved: a single candidate is still a usable signal; anything less is silence.
        if (method is null && symbolInfo.CandidateSymbols.Length == 1)
        {
            method = symbolInfo.CandidateSymbols[0] as IMethodSymbol;
        }

        var containingType = method?.ContainingType;
        if (containingType is null)
        {
            return false;
        }

        return string.Equals(
            containingType.ToDisplayString(),
            PatchBuilderMetadataName,
            StringComparison.Ordinal);
    }
}
