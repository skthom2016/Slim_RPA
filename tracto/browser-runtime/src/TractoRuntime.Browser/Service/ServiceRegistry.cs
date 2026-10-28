namespace TractoRuntime.Browser.Service;

/// <summary>
/// In-memory registry that maps service IDs to HTTP endpoint URIs
/// (CEC §2.1 / BRW-US-015).
///
/// <para>Populated from the deployment policy configuration at worker startup.
/// Service entries are immutable after construction; the registry does not expose
/// a mutation surface at runtime.</para>
/// </summary>
public sealed class ServiceRegistry : IServiceRegistry
{
    private readonly Dictionary<string, Uri> _endpoints;

    /// <param name="endpointMap">
    /// Map from service ID (case-insensitive) to the absolute HTTP/HTTPS endpoint URL.
    /// </param>
    public ServiceRegistry(IReadOnlyDictionary<string, string> endpointMap)
    {
        _endpoints = new Dictionary<string, Uri>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, url) in endpointMap)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                throw new ArgumentException(
                    $"Service '{id}' has an invalid endpoint URL: '{url}'.", nameof(endpointMap));
            _endpoints[id] = uri;
        }
    }

    /// <inheritdoc/>
    public bool TryGetEndpoint(string serviceId, out Uri? endpoint)
    {
        if (_endpoints.TryGetValue(serviceId, out var uri))
        {
            endpoint = uri;
            return true;
        }
        endpoint = null;
        return false;
    }
}
