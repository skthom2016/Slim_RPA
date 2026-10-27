namespace Tracto.Desktop.Recovery;

/// <summary>
/// A single idempotent cleanup step in the cancellation release sequence (DT-17).
/// Each implementation releases one category of held resource (modifier keys, mouse
/// button, clipboard handle, input lease, etc.).
/// </summary>
public interface IResourceReleaseStep
{
    /// <summary>
    /// Executes the release step asynchronously.
    /// </summary>
    /// <param name="ctx">
    ///   Snapshot of the resources held at the time of cancellation.
    /// </param>
    /// <param name="ct">
    ///   Cancellation token.  Implementations should honour it when performing
    ///   async work, but <em>must</em> attempt the release regardless of whether
    ///   the token has already fired — the purpose of this step is cleanup.
    /// </param>
    Task ExecuteAsync(CancellationContext ctx, CancellationToken ct);
}
