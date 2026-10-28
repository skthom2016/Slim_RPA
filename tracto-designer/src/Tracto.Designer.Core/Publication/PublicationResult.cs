namespace Tracto.Designer.Core.Publication;

public sealed record PublicationResult(
    bool Success,
    string Message,
    string? PublicationId = null,
    DateTimeOffset? PublishedAt = null)
{
    public static PublicationResult Failure(string reason) => new(false, reason);
    public static PublicationResult Ok(string id) => new(true, "Published successfully.", id, DateTimeOffset.UtcNow);
}
