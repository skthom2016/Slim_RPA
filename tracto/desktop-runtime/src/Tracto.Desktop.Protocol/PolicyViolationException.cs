#nullable enable

namespace Tracto.Desktop.Protocol;

/// <summary>
/// Thrown when a service invocation references a serviceRef not present in the approved service registry.
/// No network call is made when this exception is thrown.
/// </summary>
public sealed class PolicyViolationException : Exception
{
    /// <summary>The serviceRef that was rejected.</summary>
    public string ServiceRef { get; }

    public PolicyViolationException(string serviceRef, string? message = null)
        : base(message ?? $"serviceRef '{serviceRef}' is not in the approved service registry.")
    {
        ServiceRef = serviceRef;
    }
}
