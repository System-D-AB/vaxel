namespace Vaxel;

/// <summary>
/// Represents a structured refusal explaining why a requested mutation was not performed.
/// </summary>
public sealed record Refusal(string Code, string Reason, string? Remedy = null, int StatusCode = 409);
