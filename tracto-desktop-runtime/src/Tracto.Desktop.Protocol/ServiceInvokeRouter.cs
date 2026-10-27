#nullable enable

using Tracto.Contract.Models;
using Tracto.Contract.Models.Protocol;
using Tracto.Contract.Orchestrator;
using Tracto.Mock.BusinessService;

namespace Tracto.Desktop.Protocol;

/// <summary>
/// Routes service.invoke actions through the approved service registry.
/// serviceRef not in the registry → throws <see cref="PolicyViolationException"/> (no network call made).
/// Maps all 5 <see cref="ServiceOutcome"/> values from <see cref="MockBusinessService"/>.
/// </summary>
public sealed class ServiceInvokeRouter
{
    private readonly MockBusinessService _businessService;

    /// <summary>
    /// Creates a router backed by the given mock business service.
    /// In production the mock is replaced by the real service adapter.
    /// </summary>
    public ServiceInvokeRouter(MockBusinessService businessService)
    {
        ArgumentNullException.ThrowIfNull(businessService);
        _businessService = businessService;
    }

    /// <summary>
    /// Invokes the named service operation after validating that <paramref name="serviceRef"/> is registered.
    /// </summary>
    /// <param name="serviceRef">Logical service identifier.</param>
    /// <param name="operationRef">Operation to invoke on the service.</param>
    /// <param name="registry">Approved service registry.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The <see cref="ServiceInvokeResponse"/> from the service.</returns>
    /// <exception cref="PolicyViolationException">
    /// Thrown when <paramref name="serviceRef"/> is not in the approved registry.
    /// </exception>
    public Task<ServiceInvokeResponse> InvokeAsync(
        string serviceRef,
        string operationRef,
        IServiceRegistry registry,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceRef);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationRef);
        ArgumentNullException.ThrowIfNull(registry);

        if (!registry.IsRegistered(serviceRef))
            throw new PolicyViolationException(serviceRef);

        var request = new ServiceInvokeRequest(
            ServiceRef:   serviceRef,
            OperationRef: operationRef);

        var response = _businessService.Invoke(request);
        return Task.FromResult(response);
    }
}
