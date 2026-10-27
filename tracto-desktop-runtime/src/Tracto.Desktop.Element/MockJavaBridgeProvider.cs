namespace Tracto.Desktop.Element;

/// <summary>
/// Test double for <see cref="IJavaBridgeProvider"/>.
/// All return values are configurable so unit tests can exercise every
/// code path in <see cref="JavaBridgeElementFinder"/> without a live JRE.
/// </summary>
public sealed class MockJavaBridgeProvider : IJavaBridgeProvider
{
    /// <summary>
    /// Controls the value returned by <see cref="IsAvailable"/>.
    /// Default is <c>true</c>.
    /// </summary>
    public bool AvailableResult { get; set; } = true;

    /// <summary>
    /// Element returned by <see cref="FindByName"/> when <see cref="AvailableResult"/> is true.
    /// Set to null to simulate a not-found result.
    /// </summary>
    public JavaElement? FindResult { get; set; }

    /// <summary>
    /// Records the last arguments passed to <see cref="InvokeClick"/>.
    /// </summary>
    public JavaElement? LastClickedElement { get; private set; }

    /// <summary>
    /// Records the last arguments passed to <see cref="InvokeTypeText"/>.
    /// </summary>
    public (JavaElement Element, string Text)? LastTypedText { get; private set; }

    public bool IsAvailable() => AvailableResult;

    public JavaElement? FindByName(IntPtr window, string name, string? role)
        => FindResult;

    public void InvokeClick(JavaElement el)
        => LastClickedElement = el;

    public void InvokeTypeText(JavaElement el, string text)
        => LastTypedText = (el, text);
}
