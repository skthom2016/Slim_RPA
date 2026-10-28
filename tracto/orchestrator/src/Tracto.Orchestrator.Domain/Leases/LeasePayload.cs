#nullable enable
namespace Tracto.Orchestrator.Domain.Leases;

/// <summary>
/// Immutable value object returned to a worker after successful lease acceptance (OC-04).
///
/// Contains every field the worker needs to execute the run:
///   runId, attemptId, artifactId, artifactVersion, artifactHash, inputRefs, secretRefs.
///
/// Domain invariant 4: no plaintext secrets may appear in this payload.
/// SecretRefs are opaque reference strings (e.g. "ref:vault/my-secret") — never resolved values.
/// Call <see cref="AssertNoResolvedSecrets"/> at construction time to enforce this invariant.
/// </summary>
public sealed record LeasePayload
{
    /// <summary>Prefix all opaque secret references must begin with.</summary>
    private const string SecretRefPrefix = "ref:";

    private LeasePayload() { }

    /// <param name="runId">The run this lease covers.</param>
    /// <param name="attemptId">
    /// The AttemptId issued at offer time. Must be echoed back in all subsequent
    /// stage-transition calls — domain invariant 3.
    /// </param>
    /// <param name="artifactId">Script artifact identifier the worker should execute.</param>
    /// <param name="artifactVersion">Pinned artifact version.</param>
    /// <param name="artifactHash">SHA-256 content hash for integrity verification — immutable (invariant 7).</param>
    /// <param name="inputRefs">Opaque references to input blobs/files. Never stores content.</param>
    /// <param name="secretRefs">
    /// Opaque references to secrets. Every element MUST start with "ref:" — domain invariant 4.
    /// Construct via <see cref="Create"/> to enforce this.
    /// </param>
    public LeasePayload(
        Guid     runId,
        Guid     attemptId,
        string   artifactId,
        string   artifactVersion,
        string   artifactHash,
        string[] inputRefs,
        string[] secretRefs)
    {
        if (string.IsNullOrWhiteSpace(artifactId))
            throw new ArgumentException("ArtifactId is required.", nameof(artifactId));
        if (string.IsNullOrWhiteSpace(artifactHash))
            throw new ArgumentException("ArtifactHash is required.", nameof(artifactHash));

        RunId           = runId;
        AttemptId       = attemptId;
        ArtifactId      = artifactId;
        ArtifactVersion = artifactVersion;
        ArtifactHash    = artifactHash;
        InputRefs       = inputRefs;
        SecretRefs      = secretRefs;
    }

    public Guid     RunId           { get; }
    public Guid     AttemptId       { get; }
    public string   ArtifactId      { get; } = string.Empty;
    public string   ArtifactVersion { get; } = string.Empty;

    /// <summary>Content hash pinned at offer time. Immutable — domain invariant 7.</summary>
    public string   ArtifactHash    { get; } = string.Empty;

    /// <summary>Opaque input blob references. Not interpreted by the orchestrator.</summary>
    public string[] InputRefs       { get; } = [];

    /// <summary>
    /// Opaque secret references. Presence of these strings does not disclose a secret value.
    /// Domain invariant 4: every entry must begin with <c>"ref:"</c>.
    /// </summary>
    public string[] SecretRefs      { get; } = [];

    // ── Factory (validated) ────────────────────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="LeasePayload"/> and immediately asserts domain invariant 4
    /// (no resolved secret values). Throws <see cref="InvalidOperationException"/> if any
    /// SecretRef does not begin with <c>"ref:"</c>.
    /// </summary>
    public static LeasePayload Create(
        Guid     runId,
        Guid     attemptId,
        string   artifactId,
        string   artifactVersion,
        string   artifactHash,
        string[] inputRefs,
        string[] secretRefs)
    {
        var payload = new LeasePayload(runId, attemptId, artifactId, artifactVersion,
                                       artifactHash, inputRefs, secretRefs);
        payload.AssertNoResolvedSecrets();
        return payload;
    }

    // ── Domain invariant checks ────────────────────────────────────────────────

    /// <summary>
    /// Verifies domain invariant 4: no element of <see cref="SecretRefs"/> looks like
    /// a resolved secret value. Every entry must begin with <c>"ref:"</c>.
    /// Throws <see cref="InvalidOperationException"/> on violation.
    /// </summary>
    public void AssertNoResolvedSecrets()
    {
        foreach (var secretRef in SecretRefs)
        {
            if (!secretRef.StartsWith(SecretRefPrefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Domain invariant 4 violated: SecretRef '{Redact(secretRef)}' does not begin " +
                    $"with '{SecretRefPrefix}'. Resolved secret values must never appear in LeasePayload.");
        }
    }

    /// <summary>
    /// Returns true when any <see cref="SecretRefs"/> element does not start with
    /// <c>"ref:"</c> — indicating a resolved secret value may be present.
    /// </summary>
    public bool HasResolvedSecretValues()
        => SecretRefs.Any(r => !r.StartsWith(SecretRefPrefix, StringComparison.OrdinalIgnoreCase));

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>Returns a safe truncated representation for logging — never logs the full value.</summary>
    private static string Redact(string value)
        => value.Length <= 4 ? "***" : $"{value[..4]}***";
}
