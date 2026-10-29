namespace Vaxel.Expressions;

internal readonly record struct ExpressionSpan(int Offset, int Length)
{
    public override string ToString() => $"{Offset}..{Offset + Length}";
}

internal readonly record struct ExpressionResult<T>(
    bool Ok,
    T? Value,
    string? Code,
    string? Reason,
    ExpressionSpan Span)
{
    public static ExpressionResult<T> Success(T value, ExpressionSpan span = default)
        => new(true, value, null, null, span);

    public static ExpressionResult<T> Refuse(string code, string reason, ExpressionSpan span = default)
        => new(false, default, code, reason, span);
}

internal abstract record ExpressionNode(ExpressionSpan Span);

internal sealed record LiteralNode(object? Value, ExpressionSpan Span) : ExpressionNode(Span);

internal sealed record IdentifierNode(string Name, ExpressionSpan Span) : ExpressionNode(Span);

internal sealed record MemberAccessNode(ExpressionNode Object, string Property, ExpressionSpan Span) : ExpressionNode(Span);

internal sealed record UnaryOpNode(string Op, ExpressionNode Argument, ExpressionSpan Span) : ExpressionNode(Span);

internal sealed record BinaryOpNode(string Op, ExpressionNode Left, ExpressionNode Right, ExpressionSpan Span) : ExpressionNode(Span);

internal sealed record TernaryNode(
    ExpressionNode Condition,
    ExpressionNode Consequent,
    ExpressionNode Alternate,
    ExpressionSpan Span) : ExpressionNode(Span);

internal sealed record CallNode(string Callee, IReadOnlyList<ExpressionNode> Args, ExpressionSpan Span) : ExpressionNode(Span);

internal abstract record TemplatePart;

internal sealed record TemplateTextPart(string Value) : TemplatePart;

internal sealed record TemplateExprPart(ExpressionNode Expr) : TemplatePart;

internal sealed record TemplateNode(IReadOnlyList<TemplatePart> Parts, ExpressionSpan Span) : ExpressionNode(Span);
