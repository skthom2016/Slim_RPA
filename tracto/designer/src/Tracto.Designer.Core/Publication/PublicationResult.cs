namespace Tracto.Designer.Core.Publication;

/// <param name="Success">Whether the publication went through.</param>
/// <param name="Message">A sentence for the person reading the output.</param>
/// <param name="PublicationId">The id of what was published, when there is one.</param>
/// <param name="PublishedAt">When it was published.</param>
/// <param name="MovedLatest">The Orchestrator's "latest" tag was moved to this version as part of the publish.</param>
/// <param name="AlreadyPublished">
/// The Orchestrator already held this exact version with identical content, so the publish
/// succeeded but added nothing. Raise the version to publish a change.
/// </param>
public sealed record PublicationResult(
    bool Success,
    string Message,
    string? PublicationId = null,
    DateTimeOffset? PublishedAt = null,
    bool MovedLatest = false,
    bool AlreadyPublished = false)
{
    public static PublicationResult Failure(string reason) => new(false, reason);
    public static PublicationResult Ok(string id, bool movedLatest = false, bool alreadyPublished = false) =>
        new(true, alreadyPublished ? "Already published; nothing was added." : "Published successfully.",
            id, DateTimeOffset.UtcNow, movedLatest, alreadyPublished);
}
