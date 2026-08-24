using System.Text.RegularExpressions;

namespace Vaxel;

/// <summary>
/// Thrown when a patch target selector is not a valid HTML element id selector (e.g. #my-id).
/// </summary>
public sealed partial class VaxelTargetException : ArgumentException
{
    private static readonly Regex TargetRegex = CreateTargetRegex();

    public string Target { get; }

    public VaxelTargetException(string target)
        : base($"Target '{target}' is not a valid element id selector. Target must be a single '#id' (e.g. '#pane').", nameof(target))
    {
        Target = target;
    }

    public static void ThrowIfInvalid(string? target)
    {
        if (string.IsNullOrWhiteSpace(target) || !TargetRegex.IsMatch(target))
        {
            throw new VaxelTargetException(target ?? string.Empty);
        }
    }

    [GeneratedRegex(@"^#[A-Za-z][\w:-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex CreateTargetRegex();
}
