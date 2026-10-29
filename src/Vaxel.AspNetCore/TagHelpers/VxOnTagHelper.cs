using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Vaxel.Expressions;

namespace Vaxel.TagHelpers;

/// <summary>
/// Tag Helper for <c>vx-on</c> declarative trigger chains (P2: v2.0-triggers).
/// Parses and validates the <c>source | operator... | sink</c> pipeline at render time,
/// enforcing Rule R2 (closed grammar, literal numeric arguments, validated <c>filter</c> and <c>set</c> expressions).
/// </summary>
[HtmlTargetElement("*", Attributes = "vx-on")]
public sealed partial class VxOnTagHelper : TagHelper
{
    private static readonly HashSet<string> ValidScopes = new(StringComparer.Ordinal)
    {
        "window",
        "document",
        "outside",
    };

    private static readonly HashSet<string> OperatorKeywords = new(StringComparer.Ordinal)
    {
        "debounce",
        "throttle",
        "delay",
        "once",
        "distinct",
        "key",
        "filter",
        "prevent",
        "stop",
        "set",
    };

    [GeneratedRegex(@"^[a-zA-Z][a-zA-Z0-9:_-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex EventNameRegex();

    [GeneratedRegex(@"^[a-zA-Z_][a-zA-Z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierRegex();

    [HtmlAttributeName("vx-on")]
    public string? VxOn { get; set; }

    private readonly VaxelOptions? _options;

    public VxOnTagHelper() : this(null)
    {
    }

    [Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructor]
    public VxOnTagHelper(Microsoft.Extensions.Options.IOptions<VaxelOptions>? options)
    {
        _options = options?.Value;
    }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(output);

        if (VxOn is not null)
        {
            ValidateChain(VxOn);
            output.Attributes.SetAttribute("vx-on", VxOn);
        }
    }

    private void ValidateChain(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new VaxelTagHelperException(
                "Attribute 'vx-on' has an empty trigger chain at span 0..0. " +
                "Provide a source event or pipeline (e.g. vx-on=\"input | debounce 200 | distinct\").");
        }

        var stages = SplitChainStages(raw);
        if (stages.Count == 0)
        {
            throw new VaxelTagHelperException(
                "Attribute 'vx-on' has an empty trigger chain at span 0..0.");
        }

        ValidateSourceStage(stages[0], raw);

        var eventSchema = new EventContextSignalSchema(_options?.SignalSchema);

        for (var i = 1; i < stages.Count; i++)
        {
            var stage = stages[i];
            var isLast = i == stages.Count - 1;

            if (IsSetStage(stage.Text))
            {
                if (!isLast)
                {
                    throw new VaxelTagHelperException(
                        $"Attribute 'vx-on' has sink '{stage.Text}' at span {stage.Span} before another stage. " +
                        "A 'set <ident> = <expr>' sink must be the final stage in a vx-on chain.");
                }

                ValidateSetSink(stage, eventSchema);
            }
            else
            {
                ValidateOperatorStage(stage, eventSchema);
            }
        }
    }

    private static List<ChainStage> SplitChainStages(string raw)
    {
        var stages = new List<ChainStage>();
        var stageStart = 0;
        char? quote = null;

        for (var i = 0; i < raw.Length; i++)
        {
            var ch = raw[i];
            if (quote.HasValue)
            {
                if (ch == '\\' && i + 1 < raw.Length)
                {
                    i++;
                }
                else if (ch == quote.Value)
                {
                    quote = null;
                }
            }
            else if (ch is '\'' or '"')
            {
                quote = ch;
            }
            else if (ch == '|' && (i + 1 >= raw.Length || raw[i + 1] != '|') && (i == 0 || raw[i - 1] != '|'))
            {
                stages.Add(TrimStage(raw, stageStart, i));
                stageStart = i + 1;
            }
        }

        if (quote.HasValue)
        {
            throw new VaxelTagHelperException(
                $"Attribute 'vx-on' has an unterminated string literal in '{raw}' at span {stageStart}..{raw.Length}.");
        }

        stages.Add(TrimStage(raw, stageStart, raw.Length));

        foreach (var stage in stages)
        {
            if (string.IsNullOrEmpty(stage.Text))
            {
                throw new VaxelTagHelperException(
                    $"Attribute 'vx-on' contains an empty pipeline stage in '{raw}' at span {stage.Span}.");
            }
        }

        return stages;
    }

    private static ChainStage TrimStage(string raw, int start, int end)
    {
        while (start < end && char.IsWhiteSpace(raw[start]))
        {
            start++;
        }

        while (end > start && char.IsWhiteSpace(raw[end - 1]))
        {
            end--;
        }

        return new ChainStage(raw[start..end], new ExpressionSpan(start, end - start));
    }

    private static void ValidateSourceStage(ChainStage stage, string fullChain)
    {
        var tokens = TokenizeWhitespace(stage.Text, stage.Span.Offset);
        var src = tokens[0];

        if (src.Text == "interval")
        {
            if (tokens.Count != 2)
            {
                throw new VaxelTagHelperException(
                    $"Attribute 'vx-on' source 'interval' in '{fullChain}' requires a single positive integer millisecond argument at span {stage.Span} (e.g. 'interval 5000').");
            }

            var msToken = tokens[1];
            if (!int.TryParse(msToken.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var ms) || ms <= 0)
            {
                throw new VaxelTagHelperException(
                    $"Attribute 'vx-on' source 'interval' requires a positive integer literal argument, got '{msToken.Text}' at span {msToken.Span}.");
            }

            return;
        }

        if (src.Text is "visible" or "load")
        {
            if (tokens.Count > 1)
            {
                throw new VaxelTagHelperException(
                    $"Attribute 'vx-on' source '{src.Text}' does not accept scope arguments, got '{tokens[1].Text}' at span {tokens[1].Span}.");
            }

            return;
        }

        if (OperatorKeywords.Contains(src.Text) || !EventNameRegex().IsMatch(src.Text))
        {
            throw new VaxelTagHelperException(
                $"Attribute 'vx-on' has invalid source '{src.Text}' at span {src.Span}. " +
                "The first stage must be a DOM event name, 'interval <ms>', 'visible', or 'load'.");
        }

        for (var i = 1; i < tokens.Count; i++)
        {
            var scopeTok = tokens[i];
            if (!ValidScopes.Contains(scopeTok.Text))
            {
                throw new VaxelTagHelperException(
                    $"Attribute 'vx-on' has unknown event scope '{scopeTok.Text}' at span {scopeTok.Span}. " +
                    "Allowed scopes are: window, document, outside.");
            }
        }
    }

    private static bool IsSetStage(string text) =>
        text.Equals("set", StringComparison.Ordinal) ||
        text.StartsWith("set ", StringComparison.Ordinal) ||
        text.StartsWith("set\t", StringComparison.Ordinal);

    private void ValidateSetSink(ChainStage stage, EventContextSignalSchema eventSchema)
    {
        var afterSet = stage.Text.Length > 3 ? stage.Text[3..] : string.Empty;
        var eqIdx = afterSet.IndexOf('=', StringComparison.Ordinal);
        if (eqIdx <= 0)
        {
            throw new VaxelTagHelperException(
                $"Attribute 'vx-on' sink '{stage.Text}' at span {stage.Span} must have syntax 'set <signal> = <expr>'.");
        }

        // Reject '==' where '=' was expected
        if (eqIdx + 1 < afterSet.Length && afterSet[eqIdx + 1] == '=')
        {
            throw new VaxelTagHelperException(
                $"Attribute 'vx-on' sink '{stage.Text}' at span {stage.Span} must use single '=' assignment ('set <signal> = <expr>').");
        }

        var identRaw = afterSet[..eqIdx].Trim();
        if (!IdentifierRegex().IsMatch(identRaw))
        {
            throw new VaxelTagHelperException(
                $"Attribute 'vx-on' sink '{stage.Text}' has invalid signal name '{identRaw}' at span {stage.Span}.");
        }

        var exprOffsetInStage = 3 + eqIdx + 1;
        while (exprOffsetInStage < stage.Text.Length && char.IsWhiteSpace(stage.Text[exprOffsetInStage]))
        {
            exprOffsetInStage++;
        }

        var exprText = stage.Text[exprOffsetInStage..].TrimEnd();
        var exprSpanStart = stage.Span.Offset + exprOffsetInStage;
        var exprSpan = new ExpressionSpan(exprSpanStart, exprText.Length);

        if (string.IsNullOrEmpty(exprText))
        {
            throw new VaxelTagHelperException(
                $"Attribute 'vx-on' sink '{stage.Text}' has an empty expression at span {exprSpan}.");
        }

        var userSchema = _options?.SignalSchema;
        if (userSchema is not null && !userSchema.IsAllowed(identRaw))
        {
            throw new VaxelTagHelperException(
                $"Attribute 'vx-on' sink 'set {identRaw}' references unknown signal '{identRaw}' not defined in registered schema '{userSchema.TypeName}' at span {stage.Span}.");
        }

        var parseRes = ExpressionParser.ParseBinding(exprText, allowTemplate: false);
        if (!parseRes.Ok || parseRes.Value is null)
        {
            var absSpan = OffsetSpan(parseRes.Span, exprSpanStart);
            throw new VaxelTagHelperException(
                $"Attribute 'vx-on' sink '{stage.Text}' has invalid expression '{exprText}' at span {absSpan} ({parseRes.Code}): {parseRes.Reason}.");
        }

        var checkRes = ExpressionTypeChecker.Check(parseRes.Value, eventSchema);
        if (!checkRes.Ok)
        {
            var absSpan = OffsetSpan(checkRes.Span, exprSpanStart);
            throw new VaxelTagHelperException(
                $"Attribute 'vx-on' sink '{stage.Text}' expression '{exprText}' failed validation at span {absSpan} ({checkRes.Code}): {checkRes.Reason}.");
        }

        if (userSchema is not null)
        {
            var targetType = userSchema.GetSignalType(identRaw);
            var actualType = checkRes.Value;
            if (targetType != ExprValueType.Any &&
                actualType != ExprValueType.Any &&
                actualType != ExprValueType.Null &&
                targetType != actualType)
            {
                throw new VaxelTagHelperException(
                    $"Attribute 'vx-on' sink 'set {identRaw} = {exprText}' assigns expression of type '{actualType}' to signal '{identRaw}' of schema type '{targetType}' at span {exprSpan} (expressions.type).");
            }
        }
    }

    private static void ValidateOperatorStage(ChainStage stage, EventContextSignalSchema eventSchema)
    {
        var spIdx = -1;
        for (var i = 0; i < stage.Text.Length; i++)
        {
            if (char.IsWhiteSpace(stage.Text[i]))
            {
                spIdx = i;
                break;
            }
        }

        var opName = spIdx == -1 ? stage.Text : stage.Text[..spIdx];
        var opNameSpan = new ExpressionSpan(stage.Span.Offset, opName.Length);

        var argOffset = spIdx == -1 ? stage.Text.Length : spIdx;
        while (argOffset < stage.Text.Length && char.IsWhiteSpace(stage.Text[argOffset]))
        {
            argOffset++;
        }

        var argText = stage.Text[argOffset..].TrimEnd();
        var argSpan = new ExpressionSpan(stage.Span.Offset + argOffset, argText.Length);

        switch (opName)
        {
            case "debounce":
            case "throttle":
            case "delay":
                if (string.IsNullOrEmpty(argText) ||
                    !int.TryParse(argText, NumberStyles.None, CultureInfo.InvariantCulture, out var ms) ||
                    ms < 0)
                {
                    throw new VaxelTagHelperException(
                        $"Attribute 'vx-on' operator '{opName}' requires a non-negative integer millisecond literal, got '{argText}' at span {(string.IsNullOrEmpty(argText) ? stage.Span : argSpan)}.");
                }
                break;

            case "once":
            case "distinct":
            case "prevent":
            case "stop":
                if (!string.IsNullOrEmpty(argText))
                {
                    throw new VaxelTagHelperException(
                        $"Attribute 'vx-on' operator '{opName}' does not accept arguments, got '{argText}' at span {argSpan}.");
                }
                break;

            case "key":
                if (string.IsNullOrEmpty(argText) || argText.Any(char.IsWhiteSpace))
                {
                    throw new VaxelTagHelperException(
                        $"Attribute 'vx-on' operator 'key' requires a single key name argument (e.g. 'key Enter'), got '{argText}' at span {(string.IsNullOrEmpty(argText) ? stage.Span : argSpan)}.");
                }
                break;

            case "filter":
            {
                if (string.IsNullOrEmpty(argText))
                {
                    throw new VaxelTagHelperException(
                        $"Attribute 'vx-on' operator 'filter' requires a boolean expression argument at span {stage.Span}.");
                }

                var parseRes = ExpressionParser.ParseBinding(argText, allowTemplate: false);
                if (!parseRes.Ok || parseRes.Value is null)
                {
                    var absSpan = OffsetSpan(parseRes.Span, argSpan.Offset);
                    throw new VaxelTagHelperException(
                        $"Attribute 'vx-on' operator 'filter' has invalid expression '{argText}' at span {absSpan} ({parseRes.Code}): {parseRes.Reason}.");
                }

                var checkRes = ExpressionTypeChecker.Check(parseRes.Value, eventSchema);
                if (!checkRes.Ok)
                {
                    var absSpan = OffsetSpan(checkRes.Span, argSpan.Offset);
                    throw new VaxelTagHelperException(
                        $"Attribute 'vx-on' operator 'filter' expression '{argText}' failed validation at span {absSpan} ({checkRes.Code}): {checkRes.Reason}.");
                }

                if (checkRes.Value is not (ExprValueType.Bool or ExprValueType.Any))
                {
                    throw new VaxelTagHelperException(
                        $"Attribute 'vx-on' operator 'filter' requires a boolean expression, but '{argText}' has static type '{checkRes.Value}' at span {argSpan} (expressions.type).");
                }
                break;
            }

            default:
                throw new VaxelTagHelperException(
                    $"Attribute 'vx-on' has unknown operator '{opName}' at span {opNameSpan}. " +
                    "Allowed operators are: debounce, throttle, delay, once, distinct, key, filter, prevent, stop (and sink 'set <signal> = <expr>').");
        }
    }

    private static ExpressionSpan OffsetSpan(ExpressionSpan span, int offset) =>
        new(span.Offset + offset, span.Length);

    private static List<ChainStage> TokenizeWhitespace(string text, int baseOffset)
    {
        var list = new List<ChainStage>();
        var i = 0;
        while (i < text.Length)
        {
            while (i < text.Length && char.IsWhiteSpace(text[i]))
            {
                i++;
            }

            if (i >= text.Length)
            {
                break;
            }

            var start = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i]))
            {
                i++;
            }

            list.Add(new ChainStage(text[start..i], new ExpressionSpan(baseOffset + start, i - start)));
        }

        return list;
    }

    private readonly record struct ChainStage(string Text, ExpressionSpan Span);

    private sealed class EventContextSignalSchema(ISignalSchema? inner) : ISignalSchema
    {
        private static readonly HashSet<string> DefaultEventSignals = ["key", "value", "checked"];

        public string TypeName => inner?.TypeName ?? "EventContext";

        public IReadOnlySet<string> AllowedSignals =>
            inner is null
                ? DefaultEventSignals
                : new HashSet<string>(inner.AllowedSignals, StringComparer.Ordinal) { "key", "value", "checked" };

        public bool IsAllowed(string name)
        {
            if (name is "key" or "value" or "checked")
            {
                return true;
            }

            return inner?.IsAllowed(name) ?? true;
        }

        public ExprValueType GetSignalType(string name) => name switch
        {
            "key" => ExprValueType.String,
            "value" => ExprValueType.String,
            "checked" => ExprValueType.Bool,
            _ => inner?.GetSignalType(name) ?? ExprValueType.Any,
        };
    }
}
