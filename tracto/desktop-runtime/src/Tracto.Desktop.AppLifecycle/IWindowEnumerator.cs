namespace Tracto.Desktop.AppLifecycle;

/// <summary>
/// Enumerates top-level Windows windows and detects unexpected modals above a given target.
/// </summary>
public interface IWindowEnumerator
{
    /// <summary>Returns all visible top-level windows on the current desktop.</summary>
    IReadOnlyList<WindowInfo> EnumerateTopLevel();

    /// <summary>
    /// Returns windows whose title contains <paramref name="titleFragment"/> (case-insensitive)
    /// and belong to the process identified by <paramref name="processId"/>.
    /// </summary>
    IReadOnlyList<WindowInfo> FindByTitle(string titleFragment, int processId);

    /// <summary>
    /// Returns <c>true</c> when there is a visible top-level window above
    /// <paramref name="targetHandle"/> that does not belong to the same process and is
    /// therefore an unexpected modal that would block input.
    /// </summary>
    bool HasUnexpectedModal(IntPtr targetHandle);
}
