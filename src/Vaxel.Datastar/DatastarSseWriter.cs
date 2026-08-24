using System.Text;
using System.Text.Json;

namespace Vaxel.Datastar;

/// <summary>
/// Converts vaxel patch documents to Datastar SSE event frames.
/// <para>
/// NOTE: The Datastar client requires 'unsafe-eval' in Content-Security-Policy (CSP) to evaluate client-side expressions.
/// This is not vaxel's default security architecture (Rule R2).
/// </para>
/// </summary>
public static class DatastarSseWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string WritePatchElements(
        string elements,
        string selector = "",
        string mode = "morph",
        bool focus = false,
        string viewTransition = "")
    {
        var sb = new StringBuilder();
        sb.Append("event: datastar-patch-elements\n");

        if (!string.IsNullOrEmpty(selector))
        {
            sb.Append("data: selector ").Append(selector).Append('\n');
        }

        if (!string.IsNullOrEmpty(mode) && mode != "morph")
        {
            sb.Append("data: mode ").Append(mode).Append('\n');
        }

        if (focus)
        {
            sb.Append("data: focus true\n");
        }

        if (!string.IsNullOrEmpty(viewTransition))
        {
            sb.Append("data: viewTransition ").Append(viewTransition).Append('\n');
        }

        var lines = elements.Split('\n');
        foreach (var line in lines)
        {
            sb.Append("data: elements ").Append(line.TrimEnd('\r')).Append('\n');
        }

        sb.Append('\n');
        return sb.ToString();
    }

    public static string WritePatchSignals(object signals, bool onlyIfMissing = false)
    {
        var sb = new StringBuilder();
        sb.Append("event: datastar-patch-signals\n");

        if (onlyIfMissing)
        {
            sb.Append("data: onlyIfMissing true\n");
        }

        var json = signals is string s ? s : JsonSerializer.Serialize(signals, signals.GetType(), JsonOptions);
        var lines = json.Split('\n');
        foreach (var line in lines)
        {
            sb.Append("data: signals ").Append(line.TrimEnd('\r')).Append('\n');
        }

        sb.Append('\n');
        return sb.ToString();
    }

    public static string WriteRemoveElements(string selector)
    {
        var sb = new StringBuilder();
        sb.Append("event: datastar-remove-elements\n");
        sb.Append("data: selector ").Append(selector).Append("\n\n");
        return sb.ToString();
    }

    public static string WriteRemoveSignals(params string[] paths)
    {
        var sb = new StringBuilder();
        sb.Append("event: datastar-remove-signals\n");
        foreach (var path in paths)
        {
            sb.Append("data: paths ").Append(path).Append('\n');
        }
        sb.Append('\n');
        return sb.ToString();
    }

    public static string RefuseExecuteScript(string reason)
    {
        var sb = new StringBuilder();
        sb.Append("event: datastar-refused\n");
        sb.Append("data: error ").Append(reason).Append("\n\n");
        return sb.ToString();
    }
}
