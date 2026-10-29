using System.Text.Json;

namespace Vaxel.Expressions;

internal static class ExpressionTypeChecker
{
    public static ExpressionResult<ExprValueType> Check(ExpressionNode node, ISignalSchema? schema = null)
    {
        switch (node)
        {
            case LiteralNode lit:
                return lit.Value switch
                {
                    null => ExpressionResult<ExprValueType>.Success(ExprValueType.Null, lit.Span),
                    bool => ExpressionResult<ExprValueType>.Success(ExprValueType.Bool, lit.Span),
                    double => ExpressionResult<ExprValueType>.Success(ExprValueType.Number, lit.Span),
                    string => ExpressionResult<ExprValueType>.Success(ExprValueType.String, lit.Span),
                    _ => ExpressionResult<ExprValueType>.Success(ExprValueType.Any, lit.Span),
                };

            case IdentifierNode id:
                if (schema is null)
                {
                    return ExpressionResult<ExprValueType>.Success(ExprValueType.Any, id.Span);
                }

                if (!schema.IsAllowed(id.Name))
                {
                    return ExpressionResult<ExprValueType>.Refuse(
                        "expressions.unknown_field",
                        $"Signal '{id.Name}' is not defined in registered schema '{schema.TypeName}' (at {id.Span}). " +
                        $"Allowed schema signals: {string.Join(", ", schema.AllowedSignals)}",
                        id.Span);
                }

                return ExpressionResult<ExprValueType>.Success(schema.GetSignalType(id.Name), id.Span);

            case MemberAccessNode mem:
            {
                if (mem.Object is IdentifierNode { Name: "dataset" } &&
                    (schema is null || !schema.IsAllowed("dataset")))
                {
                    if (mem.Property is "__proto__" or "constructor" or "prototype")
                    {
                        return ExpressionResult<ExprValueType>.Success(ExprValueType.Null, mem.Span);
                    }

                    return ExpressionResult<ExprValueType>.Success(ExprValueType.String, mem.Span);
                }

                var objRes = Check(mem.Object, schema);
                if (!objRes.Ok)
                {
                    return objRes;
                }

                if (mem.Property is "__proto__" or "constructor" or "prototype")
                {
                    return ExpressionResult<ExprValueType>.Success(ExprValueType.Null, mem.Span);
                }

                if (objRes.Value == ExprValueType.List)
                {
                    return mem.Property == "length"
                        ? ExpressionResult<ExprValueType>.Success(ExprValueType.Number, mem.Span)
                        : ExpressionResult<ExprValueType>.Success(ExprValueType.Null, mem.Span);
                }

                if (objRes.Value is ExprValueType.Object or ExprValueType.Any)
                {
                    return ExpressionResult<ExprValueType>.Success(ExprValueType.Any, mem.Span);
                }

                return ExpressionResult<ExprValueType>.Success(ExprValueType.Null, mem.Span);
            }

            case UnaryOpNode un:
            {
                var argRes = Check(un.Argument, schema);
                if (!argRes.Ok)
                {
                    return argRes;
                }

                if (un.Op == "!")
                {
                    if (argRes.Value is not (ExprValueType.Bool or ExprValueType.Any))
                    {
                        return ExpressionResult<ExprValueType>.Refuse(
                            "expressions.type",
                            $"Operator '!' expects a boolean operand, got {argRes.Value} (at {un.Span})",
                            un.Span);
                    }
                    return ExpressionResult<ExprValueType>.Success(ExprValueType.Bool, un.Span);
                }

                if (argRes.Value is not (ExprValueType.Number or ExprValueType.Any))
                {
                    return ExpressionResult<ExprValueType>.Refuse(
                        "expressions.type",
                        $"Operator '{un.Op}' expects a numeric operand, got {argRes.Value} (at {un.Span})",
                        un.Span);
                }

                return ExpressionResult<ExprValueType>.Success(ExprValueType.Number, un.Span);
            }

            case TernaryNode tern:
            {
                var condRes = Check(tern.Condition, schema);
                if (!condRes.Ok)
                {
                    return condRes;
                }

                if (condRes.Value is not (ExprValueType.Bool or ExprValueType.Any))
                {
                    return ExpressionResult<ExprValueType>.Refuse(
                        "expressions.type",
                        $"Ternary condition expects a boolean, got {condRes.Value} (at {tern.Condition.Span})",
                        tern.Condition.Span);
                }

                var consRes = Check(tern.Consequent, schema);
                if (!consRes.Ok)
                {
                    return consRes;
                }

                var altRes = Check(tern.Alternate, schema);
                if (!altRes.Ok)
                {
                    return altRes;
                }

                var resultType = consRes.Value == altRes.Value ? consRes.Value : ExprValueType.Any;
                return ExpressionResult<ExprValueType>.Success(resultType, tern.Span);
            }

            case BinaryOpNode bin:
                return CheckBinaryOp(bin, schema);

            case CallNode call:
                return CheckCall(call, schema);

            case TemplateNode tpl:
                foreach (var part in tpl.Parts)
                {
                    if (part is TemplateExprPart exprPart)
                    {
                        var r = Check(exprPart.Expr, schema);
                        if (!r.Ok)
                        {
                            return r;
                        }
                    }
                }
                return ExpressionResult<ExprValueType>.Success(ExprValueType.String, tpl.Span);

            default:
                return ExpressionResult<ExprValueType>.Refuse(
                    "expressions.syntax",
                    $"Unsupported expression node '{node.GetType().Name}'",
                    node.Span);
        }
    }

    private static ExpressionResult<ExprValueType> CheckBinaryOp(BinaryOpNode bin, ISignalSchema? schema)
    {
        var leftRes = Check(bin.Left, schema);
        if (!leftRes.Ok)
        {
            return leftRes;
        }

        if (bin.Op is "&&" or "||")
        {
            if (leftRes.Value is not (ExprValueType.Bool or ExprValueType.Any))
            {
                return ExpressionResult<ExprValueType>.Refuse(
                    "expressions.type",
                    $"Operator '{bin.Op}' expects a boolean left operand, got {leftRes.Value} (at {bin.Left.Span})",
                    bin.Span);
            }

            var rightLogicRes = Check(bin.Right, schema);
            if (!rightLogicRes.Ok)
            {
                return rightLogicRes;
            }

            if (rightLogicRes.Value is not (ExprValueType.Bool or ExprValueType.Any))
            {
                return ExpressionResult<ExprValueType>.Refuse(
                    "expressions.type",
                    $"Operator '{bin.Op}' expects a boolean right operand, got {rightLogicRes.Value} (at {bin.Right.Span})",
                    bin.Span);
            }

            return ExpressionResult<ExprValueType>.Success(ExprValueType.Bool, bin.Span);
        }

        var rightRes = Check(bin.Right, schema);
        if (!rightRes.Ok)
        {
            return rightRes;
        }

        var left = leftRes.Value;
        var right = rightRes.Value;

        switch (bin.Op)
        {
            case "+":
                if (left == ExprValueType.String || right == ExprValueType.String)
                {
                    return ExpressionResult<ExprValueType>.Success(ExprValueType.String, bin.Span);
                }
                if (left == ExprValueType.Number && right == ExprValueType.Number)
                {
                    return ExpressionResult<ExprValueType>.Success(ExprValueType.Number, bin.Span);
                }
                if (left == ExprValueType.Any || right == ExprValueType.Any)
                {
                    return ExpressionResult<ExprValueType>.Success(ExprValueType.Any, bin.Span);
                }
                return ExpressionResult<ExprValueType>.Refuse(
                    "expressions.type",
                    $"Operator '+' cannot add {left} and {right} (at {bin.Span})",
                    bin.Span);

            case "-":
            case "*":
            case "/":
            case "%":
                if (left is not (ExprValueType.Number or ExprValueType.Any) ||
                    right is not (ExprValueType.Number or ExprValueType.Any))
                {
                    return ExpressionResult<ExprValueType>.Refuse(
                        "expressions.type",
                        $"Operator '{bin.Op}' expects numeric operands, got {left} and {right} (at {bin.Span})",
                        bin.Span);
                }
                return ExpressionResult<ExprValueType>.Success(ExprValueType.Number, bin.Span);

            case "==":
            case "!=":
                return ExpressionResult<ExprValueType>.Success(ExprValueType.Bool, bin.Span);

            case "<":
            case "<=":
            case ">":
            case ">=":
                if (!IsRelationalOperand(left) || !IsRelationalOperand(right))
                {
                    return ExpressionResult<ExprValueType>.Refuse(
                        "expressions.type",
                        $"Relational operator '{bin.Op}' cannot compare {left} and {right} (at {bin.Span})",
                        bin.Span);
                }
                if (left != ExprValueType.Any && right != ExprValueType.Any && left != right)
                {
                    return ExpressionResult<ExprValueType>.Refuse(
                        "expressions.type",
                        $"Relational operator '{bin.Op}' requires matching operand types, got {left} and {right} (at {bin.Span})",
                        bin.Span);
                }
                return ExpressionResult<ExprValueType>.Success(ExprValueType.Bool, bin.Span);

            default:
                return ExpressionResult<ExprValueType>.Refuse(
                    "expressions.syntax",
                    $"Unknown binary operator '{bin.Op}' (at {bin.Span})",
                    bin.Span);
        }
    }

    private static bool IsRelationalOperand(ExprValueType type)
        => type is ExprValueType.Number or ExprValueType.String or ExprValueType.Any;

    private static ExpressionResult<ExprValueType> CheckCall(CallNode call, ISignalSchema? schema)
    {
        var argTypes = new ExprValueType[call.Args.Count];
        for (var i = 0; i < call.Args.Count; i++)
        {
            var r = Check(call.Args[i], schema);
            if (!r.Ok)
            {
                return r;
            }
            argTypes[i] = r.Value;
        }

        switch (call.Callee)
        {
            case "abs":
            case "round":
            case "floor":
            case "ceil":
                if (argTypes.Length != 1 || argTypes[0] is not (ExprValueType.Number or ExprValueType.Any))
                {
                    return ExpressionResult<ExprValueType>.Refuse(
                        "expressions.type",
                        $"Function '{call.Callee}' expects 1 numeric argument (at {call.Span})",
                        call.Span);
                }
                return ExpressionResult<ExprValueType>.Success(ExprValueType.Number, call.Span);

            case "min":
            case "max":
                if (argTypes.Length != 2 ||
                    argTypes[0] is not (ExprValueType.Number or ExprValueType.Any) ||
                    argTypes[1] is not (ExprValueType.Number or ExprValueType.Any))
                {
                    return ExpressionResult<ExprValueType>.Refuse(
                        "expressions.type",
                        $"Function '{call.Callee}' expects 2 numeric arguments (at {call.Span})",
                        call.Span);
                }
                return ExpressionResult<ExprValueType>.Success(ExprValueType.Number, call.Span);

            case "len":
                if (argTypes.Length != 1 || argTypes[0] is not (ExprValueType.String or ExprValueType.Any))
                {
                    return ExpressionResult<ExprValueType>.Refuse(
                        "expressions.type",
                        $"Function 'len' expects 1 string argument (at {call.Span})",
                        call.Span);
                }
                return ExpressionResult<ExprValueType>.Success(ExprValueType.Number, call.Span);

            case "trim":
            case "toUpperCase":
            case "toLowerCase":
            case "encodeUri":
                if (argTypes.Length != 1 || argTypes[0] is not (ExprValueType.String or ExprValueType.Any))
                {
                    return ExpressionResult<ExprValueType>.Refuse(
                        "expressions.type",
                        $"Function '{call.Callee}' expects 1 string argument (at {call.Span})",
                        call.Span);
                }
                return ExpressionResult<ExprValueType>.Success(ExprValueType.String, call.Span);

            case "contains":
            case "startsWith":
            case "endsWith":
                if (argTypes.Length != 2 ||
                    argTypes[0] is not (ExprValueType.String or ExprValueType.Any) ||
                    argTypes[1] is not (ExprValueType.String or ExprValueType.Any))
                {
                    return ExpressionResult<ExprValueType>.Refuse(
                        "expressions.type",
                        $"Function '{call.Callee}' expects 2 string arguments (at {call.Span})",
                        call.Span);
                }
                return ExpressionResult<ExprValueType>.Success(ExprValueType.Bool, call.Span);

            case "substring":
                if (argTypes.Length is < 2 or > 3 ||
                    argTypes[0] is not (ExprValueType.String or ExprValueType.Any) ||
                    argTypes[1] is not (ExprValueType.Number or ExprValueType.Any) ||
                    (argTypes.Length == 3 && argTypes[2] is not (ExprValueType.Number or ExprValueType.Any)))
                {
                    return ExpressionResult<ExprValueType>.Refuse(
                        "expressions.type",
                        $"Function 'substring' expects (string, number[, number]) arguments (at {call.Span})",
                        call.Span);
                }
                return ExpressionResult<ExprValueType>.Success(ExprValueType.String, call.Span);

            case "replace":
                if (argTypes.Length != 3 ||
                    argTypes[0] is not (ExprValueType.String or ExprValueType.Any) ||
                    argTypes[1] is not (ExprValueType.String or ExprValueType.Any) ||
                    argTypes[2] is not (ExprValueType.String or ExprValueType.Any))
                {
                    return ExpressionResult<ExprValueType>.Refuse(
                        "expressions.type",
                        $"Function 'replace' expects 3 string arguments (at {call.Span})",
                        call.Span);
                }
                return ExpressionResult<ExprValueType>.Success(ExprValueType.String, call.Span);

            case "toBoolean":
                if (argTypes.Length != 1 ||
                    argTypes[0] is not (ExprValueType.Bool or ExprValueType.Number or ExprValueType.String or ExprValueType.Any))
                {
                    return ExpressionResult<ExprValueType>.Refuse(
                        "expressions.type",
                        $"Function 'toBoolean' expects 1 boolean, number, or string argument (at {call.Span})",
                        call.Span);
                }
                return ExpressionResult<ExprValueType>.Success(ExprValueType.Bool, call.Span);

            case "toInteger":
                if (argTypes.Length != 1 ||
                    argTypes[0] is not (ExprValueType.Number or ExprValueType.Bool or ExprValueType.String or ExprValueType.Any))
                {
                    return ExpressionResult<ExprValueType>.Refuse(
                        "expressions.type",
                        $"Function 'toInteger' expects 1 number, boolean, or string argument (at {call.Span})",
                        call.Span);
                }
                return ExpressionResult<ExprValueType>.Success(ExprValueType.Number, call.Span);

            case "toNumber":
                if (argTypes.Length != 1 ||
                    argTypes[0] is not (ExprValueType.Number or ExprValueType.String or ExprValueType.Any))
                {
                    return ExpressionResult<ExprValueType>.Refuse(
                        "expressions.type",
                        $"Function 'toNumber' expects 1 number or string argument (at {call.Span})",
                        call.Span);
                }
                return ExpressionResult<ExprValueType>.Success(ExprValueType.Number, call.Span);

            case "toString":
                if (argTypes.Length != 1)
                {
                    return ExpressionResult<ExprValueType>.Refuse(
                        "expressions.type",
                        $"Function 'toString' expects 1 argument (at {call.Span})",
                        call.Span);
                }
                return ExpressionResult<ExprValueType>.Success(ExprValueType.String, call.Span);

            default:
                return ExpressionResult<ExprValueType>.Refuse(
                    "expressions.unknown_function",
                    $"Unknown function '{call.Callee}' (at {call.Span})",
                    call.Span);
        }
    }

    public static ISignalSchema FromJsonState(JsonElement stateObject, string name = "JsonState")
        => new JsonInferredSignalSchema(stateObject, name);

    private sealed class JsonInferredSignalSchema : ISignalSchema
    {
        private readonly HashSet<string> _allowed = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ExprValueType> _types = new(StringComparer.Ordinal);

        public string TypeName { get; }
        public IReadOnlySet<string> AllowedSignals => _allowed;

        public JsonInferredSignalSchema(JsonElement stateObject, string name)
        {
            TypeName = name;
            if (stateObject.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in stateObject.EnumerateObject())
                {
                    _allowed.Add(prop.Name);
                    _types[prop.Name] = prop.Value.ValueKind switch
                    {
                        JsonValueKind.Number => ExprValueType.Number,
                        JsonValueKind.String => ExprValueType.String,
                        JsonValueKind.True or JsonValueKind.False => ExprValueType.Bool,
                        JsonValueKind.Array => ExprValueType.List,
                        JsonValueKind.Object => ExprValueType.Object,
                        JsonValueKind.Null => ExprValueType.Null,
                        _ => ExprValueType.Any,
                    };
                }
            }
        }

        public bool IsAllowed(string signalName) => _allowed.Contains(signalName);

        public ExprValueType GetSignalType(string signalName)
            => _types.TryGetValue(signalName, out var t) ? t : ExprValueType.Any;
    }
}
