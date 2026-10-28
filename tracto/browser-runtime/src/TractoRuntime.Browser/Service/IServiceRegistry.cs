namespace TractoRuntime.Browser.Service;

/// <summary>
/// Resolves the HTTP endpoint URI for a registered custom business service
/// (CEC §2.1 / BRW-US-015 AC#3, AC#4).
///
/// <para>Only independently registered services are callable via <c>service.invoke</c>.
/// Direct third-party business API calls are never permitted in the Browser Runtime.</para>
/// </summary>
public interface IServiceRegistry
{
    /// <summary>
    /// Returns <see langword="true"/> and sets <paramref name="endpoint"/> when
    /// <paramref name="serviceId"/> is registered.  Returns <see langword="false"/>
    /// and sets <paramref name="endpoint"/> to <see langword="null"/> when it is not.
    /// </summary>
    bool TryGetEndpoint(string serviceId, out Uri? endpoint);
}
