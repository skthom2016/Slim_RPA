namespace Tracto.Desktop.Recovery;

/// <summary>
/// The element state predicate that <see cref="IElementWaiter"/> polls until the timeout.
/// </summary>
public enum WaitCondition
{
    /// <summary>Wait until the element is visible (not off-screen).</summary>
    Visible,

    /// <summary>Wait until the element is enabled.</summary>
    Enabled,

    /// <summary>Wait until the element exists in the UIA tree (regardless of state).</summary>
    Exists,

    /// <summary>Wait until the element is absent from the UIA tree.</summary>
    NotExists,
}
