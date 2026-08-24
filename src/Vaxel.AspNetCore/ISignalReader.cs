using System.Diagnostics.CodeAnalysis;

namespace Vaxel;

/// <summary>
/// Provides read-only access to client signals sent in the VX-Signals request header.
/// <para>
/// <strong>SECURITY NOTICE:</strong> Signals are user-controlled client UI state. They must never be used
/// for authorization, identity, pricing, totals, or security-sensitive server-side decisions.
/// </para>
/// </summary>
public interface ISignalReader
{
    /// <summary>
    /// Attempts to retrieve and convert a signal value by name.
    /// </summary>
    /// <typeparam name="T">The expected type.</typeparam>
    /// <param name="name">The signal name (case-insensitive).</param>
    /// <param name="value">The parsed value if found; otherwise default.</param>
    /// <returns><c>true</c> if the signal was present and convertible; otherwise <c>false</c>.</returns>
    bool TryGet<T>(string name, [NotNullWhen(true)] out T? value);

    /// <summary>
    /// Retrieves a signal value by name, or returns the default value if absent or invalid.
    /// </summary>
    /// <typeparam name="T">The expected type.</typeparam>
    /// <param name="name">The signal name (case-insensitive).</param>
    /// <param name="defaultValue">The default value to return if not found.</param>
    /// <returns>The signal value or default.</returns>
    T? Get<T>(string name, T? defaultValue = default);

    /// <summary>
    /// Returns all signals as a dictionary of raw JSON element values.
    /// </summary>
    IReadOnlyDictionary<string, object?> All();
}
