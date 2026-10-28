#nullable enable

using System.Collections.Generic;
using Tracto.Contract.Models;
using Tracto.Contract.Models.Protocol;

namespace Tracto.Mock.BusinessService;

/// <summary>
/// A configurable fake service that returns pre-set ServiceOutcome values,
/// used by conformance runner tests.
/// </summary>
public sealed class MockBusinessService
{
    /// <summary>Default outcome when none is pre-configured.</summary>
    private ServiceOutcome _defaultOutcome;

    /// <summary>Per-operationRef outcome overrides.</summary>
    private readonly Dictionary<string, ServiceOutcome> _overrides = new();

    public MockBusinessService(ServiceOutcome defaultOutcome = ServiceOutcome.Success)
    {
        _defaultOutcome = defaultOutcome;
    }

    public void ConfigureOutcome(string operationRef, ServiceOutcome outcome)
        => _overrides[operationRef] = outcome;

    /// <summary>
    /// Returns a ServiceInvokeResponse with the configured outcome.
    /// If no per-operation override exists, returns the defaultOutcome.
    /// </summary>
    public ServiceInvokeResponse Invoke(ServiceInvokeRequest request)
    {
        var outcome = _overrides.TryGetValue(request.OperationRef, out var o) ? o : _defaultOutcome;
        return new ServiceInvokeResponse(
            ServiceOutcome: outcome,
            ActionCode: outcome == ServiceOutcome.BusinessRejection ? "BUSINESS_RULE_VIOLATION" : null,
            ErrorDetail: outcome == ServiceOutcome.TechnicalFailure ? "service.networkTimeout" : null);
    }
}
