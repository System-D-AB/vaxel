namespace Vaxel;

/// <summary>
/// Specifies how a fragment is applied to its target element in the DOM.
/// </summary>
public enum SwapMode
{
    /// <summary>
    /// Merge into the target, preserving identity, focus, caret, and scroll.
    /// </summary>
    Morph,

    /// <summary>
    /// Morph the target element itself, attributes included.
    /// </summary>
    Outer,

    /// <summary>
    /// Destroy the target and replace it with the fragment.
    /// </summary>
    Replace,

    /// <summary>
    /// Morph the target's children only.
    /// </summary>
    Inner,

    /// <summary>
    /// Insert inside the target, at the end.
    /// </summary>
    Append,

    /// <summary>
    /// Insert inside the target, at the start.
    /// </summary>
    Prepend,

    /// <summary>
    /// Insert as a preceding sibling of the target.
    /// </summary>
    Before,

    /// <summary>
    /// Insert as a following sibling of the target.
    /// </summary>
    After,

    /// <summary>
    /// Delete the target element. Carries no content.
    /// </summary>
    Remove
}
