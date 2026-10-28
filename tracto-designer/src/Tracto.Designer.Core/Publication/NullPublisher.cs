namespace Tracto.Designer.Core.Publication;

public sealed class NullPublisher : IScriptPublisher
{
    public string Name => "Null Publisher";

    public Task<PublicationResult> PublishAsync(PublicationRequest request, CancellationToken ct = default) =>
        Task.FromResult(PublicationResult.Ok(Guid.NewGuid().ToString("N")));
}
