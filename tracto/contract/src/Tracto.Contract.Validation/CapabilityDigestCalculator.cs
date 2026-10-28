using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Tracto.Contract.Models.Protocol;

namespace Tracto.Contract.Validation;

/// <summary>
/// Computes and verifies the capabilityDigest for a WorkerRegistration (C4 APPROVED).
/// The digest is an INTEGRITY HINT ONLY — not authentication proof (ST1-09-AC03).
/// Calculation: SHA-256( workerId + runtimeType + workerVersion + canonicalized(advertisedCapabilities) )
/// Canonical form: UTF-8 JSON with actionNamespaces and predicateNamespaces arrays sorted alphabetically.
/// </summary>
public sealed class CapabilityDigestCalculator
{
    /// <summary>
    /// Computes the expected digest for the given registration fields.
    /// Returns the digest in the format "sha256:&lt;hex&gt;".
    /// </summary>
    public string Compute(
        string workerId,
        string runtimeType,
        string workerVersion,
        AdvertisedCapabilities capabilities)
    {
        var canonical = BuildCanonical(workerId, runtimeType, workerVersion, capabilities);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return "sha256:" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Verifies that the registration's capabilityDigest matches the computed expected value.
    /// Returns (isMatch, expected). The Orchestrator uses this to detect accidental mutation.
    /// IMPORTANT: A digest mismatch is a warning, not a security gate.
    /// Authentication is via mTLS/token, not the digest.
    /// </summary>
    public (bool IsMatch, string Expected) Verify(WorkerRegistration registration)
    {
        if (registration.AdvertisedCapabilities is null)
            return (false, string.Empty);

        var expected = Compute(
            registration.WorkerId,
            registration.RuntimeType.ToString().ToLowerInvariant(),
            registration.WorkerVersion,
            registration.AdvertisedCapabilities);

        return (registration.CapabilityDigest == expected, expected);
    }

    private static string BuildCanonical(
        string workerId, string runtimeType, string workerVersion,
        AdvertisedCapabilities capabilities)
    {
        // Sort actionNamespaces and predicateNamespaces alphabetically for canonical form.
        var sortedAction = (capabilities.ActionNamespaces ?? Array.Empty<string>())
            .OrderBy(s => s, StringComparer.Ordinal).ToList();
        var sortedPredicate = (capabilities.PredicateNamespaces ?? Array.Empty<string>())
            .OrderBy(s => s, StringComparer.Ordinal).ToList();

        // Build the JSON object with keys in declaration order (fixed by C4 approval).
        // maxIterations included per Q9 APPROVED (2026-10-23): cap of 100 is normative.
        return JsonSerializer.Serialize(new
        {
            workerId,
            runtimeType,
            workerVersion,
            actionNamespaces = sortedAction,
            maxIterations = capabilities.MaxIterations,
            predicateNamespaces = sortedPredicate
        });
    }
}
