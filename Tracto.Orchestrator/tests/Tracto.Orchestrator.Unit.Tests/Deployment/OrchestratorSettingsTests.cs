using FluentAssertions;
using Tracto.Orchestrator.Domain.Settings;

namespace Tracto.Orchestrator.Unit.Tests.Deployment;

/// <summary>
/// Unit tests for <see cref="OrchestratorSettings"/> domain record.
/// Verifies that Connected / Isolated modes produce the correct bind address and URL.
/// OC-16 AC: container starts in Connected/Isolated mode; health 200 OK.
/// </summary>
public sealed class OrchestratorSettingsTests
{
    // ─── Connected mode ────────────────────────────────────────────────────────

    [Fact]
    public void ConnectedMode_ListenAddress_Is0000()
    {
        var settings = new OrchestratorSettings(DeploymentMode.Connected);

        settings.ListenAddress.Should().Be("0.0.0.0",
            because: "Connected mode must bind on all network interfaces");
    }

    [Fact]
    public void ConnectedMode_ListenUrl_IncludesDefaultPort()
    {
        var settings = new OrchestratorSettings(DeploymentMode.Connected);

        settings.ListenUrl.Should().Be("http://0.0.0.0:8080",
            because: "Connected mode should expose the API on the LAN");
    }

    [Fact]
    public void ConnectedMode_ListenUrl_RespectsCustomPort()
    {
        var settings = new OrchestratorSettings(DeploymentMode.Connected, ApiPort: 9090);

        settings.ListenUrl.Should().Be("http://0.0.0.0:9090");
    }

    // ─── Isolated mode ─────────────────────────────────────────────────────────

    [Fact]
    public void IsolatedMode_ListenAddress_IsLoopback()
    {
        var settings = new OrchestratorSettings(DeploymentMode.Isolated);

        settings.ListenAddress.Should().Be("127.0.0.1",
            because: "Isolated mode must never bind on network-reachable interfaces");
    }

    [Fact]
    public void IsolatedMode_ListenUrl_IsLoopbackWithDefaultPort()
    {
        var settings = new OrchestratorSettings(DeploymentMode.Isolated);

        settings.ListenUrl.Should().Be("http://127.0.0.1:8080",
            because: "Isolated mode restricts traffic to the local machine");
    }

    [Fact]
    public void IsolatedMode_ListenUrl_RespectsCustomPort()
    {
        var settings = new OrchestratorSettings(DeploymentMode.Isolated, ApiPort: 7777);

        settings.ListenUrl.Should().Be("http://127.0.0.1:7777");
    }

    // ─── Default values ────────────────────────────────────────────────────────

    [Fact]
    public void DefaultApiPort_Is8080()
    {
        var settings = new OrchestratorSettings(DeploymentMode.Connected);

        settings.ApiPort.Should().Be(8080);
    }

    [Fact]
    public void DefaultDbPath_IsNotNullOrWhiteSpace()
    {
        var settings = new OrchestratorSettings(DeploymentMode.Connected);

        settings.DbPath.Should().NotBeNullOrWhiteSpace(
            because: "there must always be a fallback SQLite file path");
    }

    [Fact]
    public void DefaultTlsCertPath_IsNull()
    {
        var settings = new OrchestratorSettings(DeploymentMode.Connected);

        settings.TlsCertPath.Should().BeNull(
            because: "TLS is optional; it must not be required by default");
    }

    [Fact]
    public void DefaultAllowedCidr_IsNull()
    {
        var settings = new OrchestratorSettings(DeploymentMode.Connected);

        settings.AllowedCidr.Should().BeNull(
            because: "CIDR filtering is optional; it must not be required by default");
    }

    // ─── Immutability (record equality) ────────────────────────────────────────

    [Fact]
    public void TwoIdenticalSettings_AreEqual()
    {
        var a = new OrchestratorSettings(DeploymentMode.Connected, 8080, "db.sqlite");
        var b = new OrchestratorSettings(DeploymentMode.Connected, 8080, "db.sqlite");

        a.Should().Be(b, because: "OrchestratorSettings is a value-equality record");
    }

    [Fact]
    public void DifferentModes_AreNotEqual()
    {
        var connected = new OrchestratorSettings(DeploymentMode.Connected);
        var isolated  = new OrchestratorSettings(DeploymentMode.Isolated);

        connected.Should().NotBe(isolated);
    }
}
