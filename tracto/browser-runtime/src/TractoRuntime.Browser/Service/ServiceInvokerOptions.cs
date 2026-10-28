namespace TractoRuntime.Browser.Service;

/// <summary>
/// Configuration for <see cref="ServiceInvoker"/>.
/// Maps logical service IDs to their HTTP endpoint base addresses and optional timeouts.
/// </summary>
public sealed class ServiceInvokerOptions
{
    /// <summary>
    /// Per-service endpoint configuration, keyed by the <c>serviceId</c> declared in the script's
    /// <c>service.invoke</c> step.  Only services registered here may be invoked
    /// (CEC §9: all invocations go to registered independent custom business services).
    /// </summary>
    public Dictionary<string, ServiceEndpointConfig> Endpoints { get; set; } = [];

    /// <summary>
    /// Default HTTP timeout for all service invocations when not overridden per-endpoint.
    /// Defaults to 30 seconds.
    /// </summary>
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Configuration for one registered service endpoint.
/// </summary>
public sealed class ServiceEndpointConfig
{
    /// <summary>
    /// Absolute URI of the service invoke endpoint
    /// (e.g. <c>https://svc.internal/api/invoke</c>).
    /// </summary>
    public required string BaseAddress { get; set; }

    /// <summary>
    /// Optional per-endpoint HTTP timeout.  Inherits <see cref="ServiceInvokerOptions.DefaultTimeout"/>
    /// when not set.
    /// </summary>
    public TimeSpan? Timeout { get; set; }
}
