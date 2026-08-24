namespace Vaxel;

/// <summary>
/// Thrown when a fragment is rendered without an HTTP request and the Razor unit
/// needs request-bound APIs such as <c>Url.Page</c> or <c>User</c>.
/// </summary>
public sealed class VaxelFragmentContextException : InvalidOperationException
{
    public VaxelFragmentContextException(string missingCapability)
        : base(
            $"Fragment rendering in a background scope cannot use {missingCapability}. " +
            $"Render fragments that need {missingCapability} inside an HTTP request.")
    {
        MissingCapability = missingCapability;
    }

    public string MissingCapability { get; }
}
