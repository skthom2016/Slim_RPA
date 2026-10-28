namespace Tracto.Designer.Core.Publication;

public interface IScriptPublisher
{
    string Name { get; }
    Task<PublicationResult> PublishAsync(PublicationRequest request, CancellationToken ct = default);
}
