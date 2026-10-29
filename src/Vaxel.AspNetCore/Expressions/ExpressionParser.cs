using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace Vaxel.Expressions;

internal static class ExpressionParser
{
    public const int MaxExpressionDepth = 32;

    private static readonly ConcurrentDictionary<(string Source, bool AllowTemplate), ExpressionResult<ExpressionNode>> Cache = new();

    private static readonly Dictionary<string, (int LeftBp, int RightBp)> InfixBindingPower = new(StringComparer.Ordinal)
    {
        ["||"] = (10, 11),
        ["&&"] = (20, 21),
        ["=="] = (30, 31),
        ["!="] = (30, 31),
        ["<"] = (40, 41),
        ["<="] = (40, 41),
        [">"] = (40, 41),
        [">="] = (40, 41),
        ["+"] = (50, 51),
        ["-"] = (50, 51),
        ["*"] = (60, 61),
        ["/"] = (60, 61),
        ["%"] = (60, 61),
    };

    private const int PrefixBindingPower = 70;
    private const int PostfixBindingPower = 80;
    private const int TernaryBindingPower = 5;

    public static ExpressionResult<ExpressionNode> ParseBinding(string source, bool allowTemplate)
    {
        if (source is null)
        {
            return ExpressionResult<ExpressionNode>.Refuse(
                "expressions.syntax",
                "Expression cannot be null",
                new ExpressionSpan(0, 0));
        }

        return Cache.GetOrAdd((source, allowTemplate), static key =>
        {
            var (src, isTpl) = key;
            if (isTpl && src.Contains("@{", StringComparison.Ordinal))
            {
                return ParseTemplateOrExpr(src);
            }

            if (src.Contains("@{", StringComparison.Ordinal))
            {
                return ParseTemplateOrExpr(src);
            }

            return ParseBare(src);
        });
    }

    public static ExpressionResult<ExpressionNode> ParseBare(string source, int baseOffset = 0)
    {
        if (source is null)
        {
            return ExpressionResult<ExpressionNode>.Refuse(
                "expressions.syntax",
                "Expression cannot be null",
                new ExpressionSpan(baseOffset, 0));
        }

        if (source.Length > ExpressionLexer.MaxExpressionLength)
        {
            return ExpressionResult<ExpressionNode>.Refuse(
                "expressions.syntax",
                $"Expression exceeds maximum length of {ExpressionLexer.MaxExpressionLength} characters",
                new ExpressionSpan(baseOffset, source.Length));
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            return ExpressionResult<ExpressionNode>.Refuse(
                "expressions.syntax",
                "Empty expression is not allowed",
                new ExpressionSpan(baseOffset, source.Length));
        }

        var lex = ExpressionLexer.Tokenize(source, baseOffset);
        if (!lex.Ok || lex.Value is null)
        {
            return ExpressionResult<ExpressionNode>.Refuse(
                lex.Code ?? "expressions.syntax",
                lex.Reason ?? "Lexical error",
                lex.Span);
        }

        return ParseTokens(lex.Value);
    }

    public static ExpressionResult<ExpressionNode> ParseTemplateOrExpr(string source)
    {
        if (source is null)
        {
            return ExpressionResult<ExpressionNode>.Refuse(
                "expressions.syntax",
                "Expression cannot be null",
                new ExpressionSpan(0, 0));
        }

        if (source.Length > ExpressionLexer.MaxExpressionLength)
        {
            return ExpressionResult<ExpressionNode>.Refuse(
                "expressions.syntax",
                $"Expression exceeds maximum length of {ExpressionLexer.MaxExpressionLength} characters",
                new ExpressionSpan(0, source.Length));
        }

        if (!source.Contains("@{", StringComparison.Ordinal))
        {
            var unescaped = source.Replace("\\@", "@", StringComparison.Ordinal);
            return ExpressionResult<ExpressionNode>.Success(
                new LiteralNode(unescaped, new ExpressionSpan(0, source.Length)),
                new ExpressionSpan(0, source.Length));
        }

        var parts = new List<TemplatePart>();
        var pos = 0;
        var textBuf = new StringBuilder();

        while (pos < source.Length)
        {
            if (source[pos] == '\\' && pos + 1 < source.Length && source[pos + 1] == '@')
            {
                textBuf.Append('@');
                pos += 2;
                continue;
            }

            if (source[pos] == '@' && pos + 1 < source.Length && source[pos + 1] == '{')
            {
                if (textBuf.Length > 0)
                {
                    parts.Add(new TemplateTextPart(textBuf.ToString()));
                    textBuf.Clear();
                }

                pos += 2;
                var exprStart = pos;
                var braceDepth = 1;
                char? inString = null;

                while (pos < source.Length && braceDepth > 0)
                {
                    var ch = source[pos];
                    if (inString.HasValue)
                    {
                        if (ch == '\\')
                        {
                            pos++;
                        }
                        else if (ch == inString.Value)
                        {
                            inString = null;
                        }
                    }
                    else
                    {
                        if (ch is '\'' or '"')
                        {
                            inString = ch;
                        }
                        else if (ch == '{')
                        {
                            braceDepth++;
                        }
                        else if (ch == '}')
                        {
                            braceDepth--;
                            if (braceDepth == 0)
                            {
                                break;
                            }
                        }
                    }

                    pos++;
                }

                if (braceDepth > 0)
                {
                    return ExpressionResult<ExpressionNode>.Refuse(
                        "expressions.syntax",
                        "Unclosed '@{' in expression template",
                        new ExpressionSpan(exprStart - 2, source.Length - (exprStart - 2)));
                }

                var rawExpr = source[exprStart..pos];
                pos++; // consume '}'

                if (string.IsNullOrWhiteSpace(rawExpr))
                {
                    return ExpressionResult<ExpressionNode>.Refuse(
                        "expressions.syntax",
                        "Empty '@{}' expression block is not allowed",
                        new ExpressionSpan(exprStart - 2, pos - (exprStart - 2)));
                }

                var parsed = ParseBare(rawExpr, exprStart);
                if (!parsed.Ok || parsed.Value is null)
                {
                    return parsed;
                }

                parts.Add(new TemplateExprPart(parsed.Value));
            }
            else
            {
                textBuf.Append(source[pos]);
                pos++;
            }
        }

        if (textBuf.Length > 0)
        {
            parts.Add(new TemplateTextPart(textBuf.ToString()));
        }

        if (parts.Count == 1 && parts[0] is TemplateExprPart singleExpr)
        {
            return ExpressionResult<ExpressionNode>.Success(singleExpr.Expr, singleExpr.Expr.Span);
        }

        var fullSpan = new ExpressionSpan(0, source.Length);
        return ExpressionResult<ExpressionNode>.Success(new TemplateNode(parts, fullSpan), fullSpan);
    }

    public static ExpressionResult<ExpressionNode> ParseTokens(IReadOnlyList<ExpressionToken> tokens)
    {
        var pos = 0;

        ExpressionToken Peek() => pos < tokens.Count
            ? tokens[pos]
            : new ExpressionToken(ExpressionTokenType.Eof, string.Empty, default);

        ExpressionToken Next() => pos < tokens.Count
            ? tokens[pos++]
            : new ExpressionToken(ExpressionTokenType.Eof, string.Empty, default);

        ExpressionResult<ExpressionNode> ParseExpr(int minBp, int depth)
        {
            if (depth > MaxExpressionDepth)
            {
                var cur = Peek();
                return ExpressionResult<ExpressionNode>.Refuse(
                    "expressions.syntax",
                    $"Expression exceeds maximum nesting depth of {MaxExpressionDepth}",
                    cur.Span);
            }

            var token = Next();
            ExpressionNode left;

            switch (token.Type)
            {
                case ExpressionTokenType.Number:
                {
                    if (!double.TryParse(token.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
                    {
                        return ExpressionResult<ExpressionNode>.Refuse(
                            "expressions.syntax",
                            $"Invalid number literal '{token.Value}'",
                            token.Span);
                    }
                    left = new LiteralNode(num, token.Span);
                    break;
                }

                case ExpressionTokenType.String:
                    left = new LiteralNode(token.Value, token.Span);
                    break;

                case ExpressionTokenType.Boolean:
                    left = new LiteralNode(token.Value == "true", token.Span);
                    break;

                case ExpressionTokenType.Null:
                    left = new LiteralNode(null, token.Span);
                    break;

                case ExpressionTokenType.Identifier:
                {
                    if (Peek().Type == ExpressionTokenType.Punctuation && Peek().Value == "(")
                    {
                        Next(); // consume '('
                        var args = new List<ExpressionNode>();
                        if (Peek().Type != ExpressionTokenType.Punctuation || Peek().Value != ")")
                        {
                            while (true)
                            {
                                var argRes = ParseExpr(0, depth + 1);
                                if (!argRes.Ok || argRes.Value is null)
                                {
                                    return argRes;
                                }
                                args.Add(argRes.Value);

                                var nextTok = Peek();
                                if (nextTok.Type == ExpressionTokenType.Punctuation && nextTok.Value == ",")
                                {
                                    Next();
                                    if (Peek().Type == ExpressionTokenType.Punctuation && Peek().Value == ")")
                                    {
                                        return ExpressionResult<ExpressionNode>.Refuse(
                                            "expressions.syntax",
                                            $"Trailing comma in function call at position {Peek().Span.Offset}",
                                            Peek().Span);
                                    }
                                }
                                else
                                {
                                    break;
                                }
                            }
                        }

                        var closeParen = Next();
                        if (closeParen.Type != ExpressionTokenType.Punctuation || closeParen.Value != ")")
                        {
                            return ExpressionResult<ExpressionNode>.Refuse(
                                "expressions.syntax",
                                $"Expected ')' after function arguments, got '{closeParen.Value}' at position {closeParen.Span.Offset}",
                                closeParen.Span);
                        }

                        var callSpan = new ExpressionSpan(
                            token.Span.Offset,
                            closeParen.Span.Offset + closeParen.Span.Length - token.Span.Offset);
                        left = new CallNode(token.Value, args, callSpan);
                    }
                    else
                    {
                        left = new IdentifierNode(token.Value, token.Span);
                    }
                    break;
                }

                case ExpressionTokenType.Operator when token.Value is "!" or "-" or "+":
                {
                    var rightRes = ParseExpr(PrefixBindingPower, depth + 1);
                    if (!rightRes.Ok || rightRes.Value is null)
                    {
                        return rightRes;
                    }
                    var span = new ExpressionSpan(
                        token.Span.Offset,
                        rightRes.Value.Span.Offset + rightRes.Value.Span.Length - token.Span.Offset);
                    left = new UnaryOpNode(token.Value, rightRes.Value, span);
                    break;
                }

                case ExpressionTokenType.Punctuation when token.Value == "(":
                {
                    var exprRes = ParseExpr(0, depth + 1);
                    if (!exprRes.Ok || exprRes.Value is null)
                    {
                        return exprRes;
                    }
                    var closeParen = Next();
                    if (closeParen.Type != ExpressionTokenType.Punctuation || closeParen.Value != ")")
                    {
                        return ExpressionResult<ExpressionNode>.Refuse(
                            "expressions.syntax",
                            $"Expected ')' at position {closeParen.Span.Offset}",
                            closeParen.Span);
                    }
                    left = exprRes.Value;
                    break;
                }

                default:
                    return ExpressionResult<ExpressionNode>.Refuse(
                        "expressions.syntax",
                        $"Unexpected token '{token.Value}' ({token.Type}) at position {token.Span.Offset}",
                        token.Span);
            }

            while (true)
            {
                var nextTok = Peek();
                if (nextTok.Type == ExpressionTokenType.Eof)
                {
                    break;
                }

                if (nextTok.Type == ExpressionTokenType.Punctuation && nextTok.Value == ".")
                {
                    if (PostfixBindingPower < minBp)
                    {
                        break;
                    }

                    Next(); // consume '.'
                    var propTok = Next();
                    if (propTok.Type != ExpressionTokenType.Identifier &&
                        propTok.Type != ExpressionTokenType.Boolean &&
                        propTok.Type != ExpressionTokenType.Null)
                    {
                        return ExpressionResult<ExpressionNode>.Refuse(
                            "expressions.syntax",
                            $"Expected property identifier after '.' at position {propTok.Span.Offset}",
                            propTok.Span);
                    }

                    var span = new ExpressionSpan(
                        left.Span.Offset,
                        propTok.Span.Offset + propTok.Span.Length - left.Span.Offset);
                    left = new MemberAccessNode(left, propTok.Value, span);
                    continue;
                }

                if (nextTok.Type == ExpressionTokenType.Operator && nextTok.Value == "?")
                {
                    if (TernaryBindingPower < minBp)
                    {
                        break;
                    }

                    Next(); // consume '?'
                    var consequentRes = ParseExpr(0, depth + 1);
                    if (!consequentRes.Ok || consequentRes.Value is null)
                    {
                        return consequentRes;
                    }

                    var colonTok = Next();
                    if (colonTok.Type != ExpressionTokenType.Operator || colonTok.Value != ":")
                    {
                        return ExpressionResult<ExpressionNode>.Refuse(
                            "expressions.syntax",
                            $"Expected ':' in ternary expression at position {colonTok.Span.Offset}",
                            colonTok.Span);
                    }

                    var alternateRes = ParseExpr(TernaryBindingPower, depth + 1);
                    if (!alternateRes.Ok || alternateRes.Value is null)
                    {
                        return alternateRes;
                    }

                    var span = new ExpressionSpan(
                        left.Span.Offset,
                        alternateRes.Value.Span.Offset + alternateRes.Value.Span.Length - left.Span.Offset);
                    left = new TernaryNode(left, consequentRes.Value, alternateRes.Value, span);
                    continue;
                }

                if (nextTok.Type == ExpressionTokenType.Operator &&
                    InfixBindingPower.TryGetValue(nextTok.Value, out var bp))
                {
                    if (bp.LeftBp < minBp)
                    {
                        break;
                    }

                    Next(); // consume operator
                    var rightRes = ParseExpr(bp.RightBp, depth + 1);
                    if (!rightRes.Ok || rightRes.Value is null)
                    {
                        return rightRes;
                    }

                    var span = new ExpressionSpan(
                        left.Span.Offset,
                        rightRes.Value.Span.Offset + rightRes.Value.Span.Length - left.Span.Offset);
                    left = new BinaryOpNode(nextTok.Value, left, rightRes.Value, span);
                    continue;
                }

                break;
            }

            return ExpressionResult<ExpressionNode>.Success(left, left.Span);
        }

        var res = ParseExpr(0, 1);
        if (!res.Ok || res.Value is null)
        {
            return res;
        }

        var rem = Peek();
        if (rem.Type != ExpressionTokenType.Eof)
        {
            return ExpressionResult<ExpressionNode>.Refuse(
                "expressions.syntax",
                $"Unexpected trailing token '{rem.Value}' at position {rem.Span.Offset}",
                rem.Span);
        }

        return res;
    }
}
