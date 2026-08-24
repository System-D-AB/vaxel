using Microsoft.AspNetCore.Http;

namespace Vaxel;

/// <summary>
/// Factory entry point for constructing vaxel patch responses.
/// </summary>
public static class Patch
{
    /// <summary>
    /// Creates a new patch builder with status 200 OK.
    /// </summary>
    public static PatchBuilder Ok() => new(StatusCodes.Status200OK);

    /// <summary>
    /// Creates a new patch builder with the specified HTTP status code.
    /// </summary>
    public static PatchBuilder Status(int statusCode) => new(statusCode);

    /// <summary>
    /// Creates a new patch builder for a refusal with the status code from the refusal (default 409).
    /// </summary>
    public static PatchBuilder Refused(Refusal refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        return new PatchBuilder(refusal.StatusCode).WithRefusal(refusal);
    }

    /// <summary>
    /// Creates a new patch builder for a refusal with the specified reason and status code.
    /// </summary>
    public static PatchBuilder Refused(string reason, int statusCode = StatusCodes.Status409Conflict, string? remedy = null)
    {
        ArgumentNullException.ThrowIfNull(reason);
        return Refused(new Refusal("refusal", reason, remedy, statusCode));
    }
}
