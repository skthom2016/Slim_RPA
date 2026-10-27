namespace Tracto.Desktop.Recovery;

/// <summary>
/// Executes every <see cref="IResourceReleaseStep"/> in order, collecting all
/// exceptions and throwing a single <see cref="AggregateException"/> at the end.
/// Every step runs regardless of prior failures (DT-17 release guarantee).
/// </summary>
public sealed class CancellationReleaseSequence
{
    /// <summary>
    /// Runs all <paramref name="steps"/> against <paramref name="ctx"/> in sequence.
    /// Each step is wrapped in an individual try/catch so that a failure in one step
    /// does not prevent subsequent steps from running.
    /// </summary>
    /// <param name="ctx">The cancellation context describing held resources.</param>
    /// <param name="steps">Ordered sequence of release steps to execute.</param>
    /// <param name="ct">
    ///   Cooperative cancellation token; individual steps may inspect it but must still
    ///   attempt the release.
    /// </param>
    /// <exception cref="AggregateException">
    ///   Thrown after <em>all</em> steps have run when one or more steps threw an
    ///   exception.  All collected exceptions are included.
    /// </exception>
    public async Task RunAsync(
        CancellationContext ctx,
        IEnumerable<IResourceReleaseStep> steps,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(steps);

        List<Exception>? errors = null;

        foreach (IResourceReleaseStep step in steps)
        {
            try
            {
                await step.ExecuteAsync(ctx, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                errors ??= new List<Exception>();
                errors.Add(ex);
            }
        }

        if (errors is { Count: > 0 })
            throw new AggregateException(
                "One or more resource-release steps failed during cancellation cleanup.",
                errors);
    }
}
