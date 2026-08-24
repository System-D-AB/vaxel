namespace Vaxel;

/// <summary>
/// Thrown when a vaxel Tag Helper violates architectural or protocol rules at render time.
/// </summary>
public sealed class VaxelTagHelperException : InvalidOperationException
{
    public VaxelTagHelperException(string message) : base(message)
    {
    }

    public VaxelTagHelperException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
