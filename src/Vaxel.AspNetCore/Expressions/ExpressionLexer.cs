using System.Globalization;
using System.Text;

namespace Vaxel.Expressions;

internal enum ExpressionTokenType
{
    Number,
    String,
    Boolean,
    Null,
    Identifier,
    Operator,
    Punctuation,
    Eof,
}

internal readonly record struct ExpressionToken(
    ExpressionTokenType Type,
    string Value,
    ExpressionSpan Span);

internal static class ExpressionLexer
{
    public const int MaxExpressionLength = 512;

    private static readonly HashSet<string> TwoCharOps = new(StringComparer.Ordinal)
    {
        "==", "!=", "<=", ">=", "&&", "||",
    };

    private static readonly HashSet<char> SingleCharOps =
    [
        '+', '-', '*', '/', '%', '<', '>', '!', '?', ':',
    ];

    private static readonly HashSet<char> PunctuationChars =
    [
        '(', ')', ',', '.',
    ];

    public static ExpressionResult<IReadOnlyList<ExpressionToken>> Tokenize(string input, int baseOffset = 0)
    {
        if (input is null)
        {
            return ExpressionResult<IReadOnlyList<ExpressionToken>>.Refuse(
                "expressions.syntax",
                "Expression cannot be null",
                new ExpressionSpan(baseOffset, 0));
        }

        if (input.Length > MaxExpressionLength)
        {
            return ExpressionResult<IReadOnlyList<ExpressionToken>>.Refuse(
                "expressions.syntax",
                $"Expression exceeds maximum length of {MaxExpressionLength} characters",
                new ExpressionSpan(baseOffset, input.Length));
        }

        var tokens = new List<ExpressionToken>();
        var pos = 0;

        while (pos < input.Length)
        {
            var ch = input[pos];

            if (char.IsWhiteSpace(ch))
            {
                pos++;
                continue;
            }

            if (char.IsAsciiDigit(ch))
            {
                var start = pos;
                var hasDot = false;
                while (pos < input.Length)
                {
                    var c = input[pos];
                    if (char.IsAsciiDigit(c))
                    {
                        pos++;
                    }
                    else if (c == '.' && !hasDot)
                    {
                        if (pos + 1 < input.Length && char.IsAsciiDigit(input[pos + 1]))
                        {
                            hasDot = true;
                            pos++;
                        }
                        else
                        {
                            break;
                        }
                    }
                    else
                    {
                        break;
                    }
                }

                if (pos < input.Length && (char.IsAsciiLetter(input[pos]) || input[pos] == '_'))
                {
                    return ExpressionResult<IReadOnlyList<ExpressionToken>>.Refuse(
                        "expressions.syntax",
                        $"Invalid number literal starting at position {start}",
                        new ExpressionSpan(baseOffset + start, pos - start + 1));
                }

                tokens.Add(new ExpressionToken(
                    ExpressionTokenType.Number,
                    input[start..pos],
                    new ExpressionSpan(baseOffset + start, pos - start)));
                continue;
            }

            if (ch == '\'' || ch == '"')
            {
                var quote = ch;
                var start = pos;
                pos++;
                var sb = new StringBuilder();
                var closed = false;

                while (pos < input.Length)
                {
                    var c = input[pos];
                    if (c == '\\')
                    {
                        pos++;
                        if (pos >= input.Length)
                        {
                            return ExpressionResult<IReadOnlyList<ExpressionToken>>.Refuse(
                                "expressions.syntax",
                                $"Unterminated string escape at position {start}",
                                new ExpressionSpan(baseOffset + start, pos - start));
                        }

                        var esc = input[pos];
                        switch (esc)
                        {
                            case 'n': sb.Append('\n'); break;
                            case 't': sb.Append('\t'); break;
                            case 'r': sb.Append('\r'); break;
                            case '\\': sb.Append('\\'); break;
                            case '\'': sb.Append('\''); break;
                            case '"': sb.Append('"'); break;
                            case '@': sb.Append('@'); break;
                            default:
                                return ExpressionResult<IReadOnlyList<ExpressionToken>>.Refuse(
                                    "expressions.syntax",
                                    $"Invalid escape sequence '\\{esc}' at position {pos - 1}",
                                    new ExpressionSpan(baseOffset + pos - 1, 2));
                        }
                        pos++;
                    }
                    else if (c == quote)
                    {
                        closed = true;
                        pos++;
                        break;
                    }
                    else
                    {
                        sb.Append(c);
                        pos++;
                    }
                }

                if (!closed)
                {
                    return ExpressionResult<IReadOnlyList<ExpressionToken>>.Refuse(
                        "expressions.syntax",
                        $"Unterminated string literal starting at position {start}",
                        new ExpressionSpan(baseOffset + start, pos - start));
                }

                tokens.Add(new ExpressionToken(
                    ExpressionTokenType.String,
                    sb.ToString(),
                    new ExpressionSpan(baseOffset + start, pos - start)));
                continue;
            }

            if (char.IsAsciiLetter(ch) || ch == '_')
            {
                var start = pos;
                while (pos < input.Length && (char.IsAsciiLetterOrDigit(input[pos]) || input[pos] == '_'))
                {
                    pos++;
                }

                var val = input[start..pos];
                var span = new ExpressionSpan(baseOffset + start, pos - start);
                if (val is "true" or "false")
                {
                    tokens.Add(new ExpressionToken(ExpressionTokenType.Boolean, val, span));
                }
                else if (val == "null")
                {
                    tokens.Add(new ExpressionToken(ExpressionTokenType.Null, val, span));
                }
                else
                {
                    tokens.Add(new ExpressionToken(ExpressionTokenType.Identifier, val, span));
                }
                continue;
            }

            if (pos + 1 < input.Length)
            {
                var two = input.Substring(pos, 2);
                if (TwoCharOps.Contains(two))
                {
                    tokens.Add(new ExpressionToken(
                        ExpressionTokenType.Operator,
                        two,
                        new ExpressionSpan(baseOffset + pos, 2)));
                    pos += 2;
                    continue;
                }
            }

            if (SingleCharOps.Contains(ch))
            {
                tokens.Add(new ExpressionToken(
                    ExpressionTokenType.Operator,
                    ch.ToString(CultureInfo.InvariantCulture),
                    new ExpressionSpan(baseOffset + pos, 1)));
                pos++;
                continue;
            }

            if (PunctuationChars.Contains(ch))
            {
                tokens.Add(new ExpressionToken(
                    ExpressionTokenType.Punctuation,
                    ch.ToString(CultureInfo.InvariantCulture),
                    new ExpressionSpan(baseOffset + pos, 1)));
                pos++;
                continue;
            }

            return ExpressionResult<IReadOnlyList<ExpressionToken>>.Refuse(
                "expressions.syntax",
                $"Unexpected character '{ch}' at position {pos}",
                new ExpressionSpan(baseOffset + pos, 1));
        }

        tokens.Add(new ExpressionToken(
            ExpressionTokenType.Eof,
            string.Empty,
            new ExpressionSpan(baseOffset + pos, 0)));
        return ExpressionResult<IReadOnlyList<ExpressionToken>>.Success(tokens);
    }
}
