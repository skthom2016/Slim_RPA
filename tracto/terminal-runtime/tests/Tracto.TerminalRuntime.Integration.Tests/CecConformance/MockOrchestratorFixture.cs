using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Tracto.TerminalRuntime.Application;
using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Domain;
using Tracto.TerminalRuntime.Infrastructure;

namespace Tracto.TerminalRuntime.Integration.Tests.CecConformance;

/// <summary>
/// Shared fixture that provides lightweight in-memory implementations of the
/// core CEC interfaces so conformance tests run without a real Orchestrator
/// (CF-01 – CF-10 pass against this mock fixture).
///
/// Integration tests that require a real Orchestrator are marked with the
/// [Trait("Category", "RequiresOrchestrator")] attribute and skipped in CI
/// unless the ORCHESTRATOR_BASE_URL environment variable is set.
/// </summary>
public sealed class MockOrchestratorFixture : IDisposable
{
    // ── HMAC key used to sign valid test artifacts ─────────────────────────────
    // 32 random bytes; never used outside of test fixtures.
    private static readonly byte[] TestHmacKey =
        Convert.FromBase64String("dGVzdC1obWFjLWtleS0zMi1ieXRlcy0hISE=");

    public static string TestHmacKeyBase64 => Convert.ToBase64String(TestHmacKey);

    // ── Temporary journal directory ────────────────────────────────────────────
    public string JournalRoot { get; } =
        Path.Combine(Path.GetTempPath(), "Tracto_CEC_CF_" + Guid.NewGuid().ToString("N"));

    // ── Services ───────────────────────────────────────────────────────────────

    public JsonRunJournal Journal { get; }

    public CapabilityAdvertiser CapabilityAdvertiser { get; }

    public ArtifactValidator ArtifactValidator { get; }

    public MockOrchestratorFixture()
    {
        Directory.CreateDirectory(JournalRoot);
        Journal = new JsonRunJournal(NullLogger<JsonRunJournal>.Instance, JournalRoot);

        var capOptions = Options.Create(new CapabilityAdvertiserOptions
        {
            WorkerId        = "worker-cf-test-01",
            SupportedShells = ["cmd", "powershell", "wsl-bash"],
            WorkerVersion   = "1.0.0-cf"
        });
        var clockMock = new Mock<IClock>();
        clockMock.Setup(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);
        CapabilityAdvertiser = new CapabilityAdvertiser(capOptions, clockMock.Object);

        var artifactSettings = Options.Create(new ArtifactSignatureSettings
        {
            HmacKeyBase64 = TestHmacKeyBase64
        });
        ArtifactValidator = new ArtifactValidator(
            artifactSettings,
            NullLogger<ArtifactValidator>.Instance);
    }

    /// <summary>
    /// Builds a correctly-signed <see cref="ArtifactManifest"/> for the given parameters.
    /// Use this to create "good" manifests in positive-path tests.
    /// </summary>
    public ArtifactManifest BuildSignedManifest(
        string      runtimeType  = "terminal",
        int         schemaMajor  = 1,
        string[]?   actionRefs   = null,
        string[]?   commandRefs  = null)
    {
        actionRefs  ??= ["terminal.run-script"];
        commandRefs ??= ["echo hello"];

        var unsigned  = new ArtifactManifest(runtimeType, schemaMajor, actionRefs, commandRefs, string.Empty);
        var signature = ArtifactValidator.ComputeSignature(unsigned, Convert.FromBase64String(TestHmacKeyBase64));
        return unsigned with { Signature = signature };
    }

    public void Dispose()
    {
        try { Directory.Delete(JournalRoot, recursive: true); }
        catch { /* best-effort */ }
    }
}
