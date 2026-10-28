#nullable enable

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Tracto.Desktop.Protocol;

/// <summary>
/// Computes the SHA-256 capability digest sent at worker registration.
/// The digest is an integrity hint only — it is not an authentication proof (CEC C4 constraint).
/// </summary>
public static class CapabilityDigestCalculator
{
    /// <summary>
    /// Computes a lowercase hex SHA-256 digest over the JSON-serialised representation of
    /// <paramref name="capabilities"/>.
    /// </summary>
    public static string Compute(WorkerCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);

        var envelope = new
        {
            runtimeType        = capabilities.RuntimeType,
            actionNamespaces   = capabilities.ActionNamespaces,
            predicateNamespaces = capabilities.PredicateNamespaces,
            maxIterations      = capabilities.MaxIterations,
            supportedSchemaMajors = capabilities.SupportedSchemaMajors,
        };

        string json = JsonSerializer.Serialize(envelope);
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexStringLower(bytes);
    }
}
