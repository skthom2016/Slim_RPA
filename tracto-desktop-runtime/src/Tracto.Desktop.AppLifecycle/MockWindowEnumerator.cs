namespace Tracto.Desktop.AppLifecycle;

/// <summary>
/// Injectable test double for <see cref="IWindowEnumerator"/>.
/// Allows tests to control which windows are "visible" and whether a modal is detected.
/// </summary>
public sealed class MockWindowEnumerator : IWindowEnumerator
{
    /// <summary>Windows returned by <see cref="EnumerateTopLevel"/> and used by <see cref="FindByTitle"/>.</summary>
    public List<WindowInfo> TopLevelWindows { get; set; } = [];

    /// <summary>
    /// When set, <see cref="HasUnexpectedModal"/> returns this value regardless of the window list.
    /// When <c>null</c>, the method falls back to simple list-based detection logic.
    /// </summary>
    public bool? HasUnexpectedModalOverride { get; set; }

    /// <inheritdoc/>
    public IReadOnlyList<WindowInfo> EnumerateTopLevel() => TopLevelWindows;

    /// <inheritdoc/>
    public IReadOnlyList<WindowInfo> FindByTitle(string titleFragment, int processId)
    {
        return TopLevelWindows
            .Where(w => w.ProcessId == processId &&
                        w.Title.Contains(titleFragment, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <inheritdoc/>
    public bool HasUnexpectedModal(IntPtr targetHandle)
    {
        if (HasUnexpectedModalOverride.HasValue)
            return HasUnexpectedModalOverride.Value;

        // Fallback: any window in the list that is not the target is treated as a modal.
        return TopLevelWindows.Any(w => w.Handle != targetHandle);
    }
}
