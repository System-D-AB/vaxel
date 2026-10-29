using System.Globalization;
using System.Text;

namespace Vaxel.Expressions;

internal static class ExpressionCanonical
{
    public static string ToCanonical(ExpressionNode? node)
    {
        if (node is null)
        {
            return string.Empty;
        }

        return node switch
        {
            LiteralNode lit => FormatLiteral(lit.Value),
            IdentifierNode id => id.Name,
            MemberAccessNode mem => $"(. {ToCanonical(mem.Object)} {mem.Property})",
            UnaryOpNode un => $"(u{un.Op} {ToCanonical(un.Argument)})",
            BinaryOpNode bin => $"({bin.Op} {ToCanonical(bin.Left)} {ToCanonical(bin.Right)})",
            TernaryNode tern => $"(? {ToCanonical(tern.Condition)} {ToCanonical(tern.Consequent)} {ToCanonical(tern.Alternate)})",
            CallNode call => call.Args.Count == 0
                ? $"(call {call.Callee})"
                : $"(call {call.Callee} {string.Join(' ', call.Args.Select(ToCanonical))})",
            TemplateNode tpl => $"(tpl {string.Join(' ', tpl.Parts.Select(FormatTemplatePart))})",
            _ => throw new InvalidOperationException($"Unknown AST node type: {node.GetType().Name}"),
        };
    }

    private static string FormatTemplatePart(TemplatePart part) => part switch
    {
        TemplateTextPart txt => JsonQuoteString(txt.Value),
        TemplateExprPart expr => ToCanonical(expr.Expr),
        _ => throw new InvalidOperationException($"Unknown template part: {part.GetType().Name}"),
    };

    private static string FormatLiteral(object? value) => value switch
    {
        null => "null",
        bool b => b ? "true" : "false",
        double d => d.ToString(CultureInfo.InvariantCulture),
        string s => JsonQuoteString(s),
        _ => value.ToString() ?? string.Empty,
    };

    private static string JsonQuoteString(string s)
    {
        var sb = new StringBuilder(s.Length + 2);
        sb.Append('"');
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (ch < 0x20)
                    {
                        sb.Append("\\u");
                        sb.Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(ch);
                    }
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }
}
