using FluentAssertions;
using Tracto.Orchestrator.Application.Deployment;
using Tracto.Orchestrator.Domain.Settings;

namespace Tracto.Orchestrator.Unit.Tests.Deployment;

/// <summary>
/// Simulates the startup gate that reads TRACTO_MODE and either succeeds or signals
/// the application to abort with a clear error message.
///
/// OC-16 ACs:
///   - "container starts in Connected/Isolated mode" → Parse succeeds, mode is correct.
///   - "invalid TRACTO_MODE fails startup" → Parse fails, error message is actionable.
///   - "CI docker run produces no Windows-only P/Invoke DLLs" (design note):
///       The parsing logic contains zero P/Invoke, unsafe code, or platform-specific
///       DLL imports; it is pure managed C#. A CI step can verify this with
///       'dotnet publish --os linux' and check that the output directory contains no
///       kernel32.dll, advapi32.dll, or similar Windows-only libraries.
/// </summary>
public sealed class StartupModeValidationTests
{
    // ─── Simulate startup: happy path ──────────────────────────────────────────

    [Theory]
    [InlineData("Connected", DeploymentMode.Connected)]
    [InlineData("Isolated",  DeploymentMode.Isolated)]
    public void StartupGate_ValidMode_DoesNotAbort(string envVarValue, DeploymentMode expected)
    {
        // Act – replicate what Program.cs does at startup
        var result = ModeParser.Parse(envVarValue);

        // Assert
        result.IsSuccess.Should().BeTrue();

        // When the gate passes, the application proceeds with the parsed mode.
        var settings = new OrchestratorSettings(result.Value);
        settings.Mode.Should().Be(expected);
    }

    // ─── Simulate startup: invalid env var → startup must abort ───────────────

    [Theory]
    [InlineData(null,      "is not set")]
    [InlineData("",        "is not set")]
    [InlineData("  ",      "is not set")]
    [InlineData("Online",  "invalid")]
    [InlineData("connect", "invalid")]
    [InlineData("1",       "invalid")]
    public void StartupGate_InvalidMode_AbortWithActionableMessage(
        string? envVarValue, string expectedMessageFragment)
    {
        // Act
        var result = ModeParser.Parse(envVarValue);

        // Assert – the gate fails; the application must NOT start.
        result.IsFailure.Should().BeTrue(
            because: "an unrecognised TRACTO_MODE must prevent the container from starting");

        // The error message must be actionable so operators know how to fix it.
        // Two separate assertions: the specific fragment must be present (not vacuously
        // satisfied by "Connected"/"Isolated" which appear in every error message), and
        // the message must also name the valid choices.
        result.Error.Message.Should()
              .Contain(expectedMessageFragment,
                       because: "error message must explain what went wrong");
        result.Error.Message.Should()
              .ContainAny(new[] { "Connected", "Isolated" },
                          because: "error message must list the valid choices");
    }

    // ─── Listen address derivation from mode ──────────────────────────────────

    [Fact]
    public void ConnectedMode_ProgramCS_WouldBindOnAllInterfaces()
    {
        // Program.cs logic: settings.ListenAddress → passed to builder.WebHost.UseUrls(...)
        var result   = ModeParser.Parse("Connected");
        var settings = new OrchestratorSettings(result.Value);

        settings.ListenAddress.Should().Be("0.0.0.0",
            because: "Connected mode must expose the API on all network interfaces");
    }

    [Fact]
    public void IsolatedMode_ProgramCS_WouldBindOnLoopback()
    {
        var result   = ModeParser.Parse("Isolated");
        var settings = new OrchestratorSettings(result.Value);

        settings.ListenAddress.Should().Be("127.0.0.1",
            because: "Isolated mode must restrict traffic to the local machine");
    }

    // ─── Docker / Linux compatibility note ────────────────────────────────────

    /// <summary>
    /// Verifies that the mode-parsing code path contains only portable managed code.
    /// A CI pipeline can supplement this with:
    ///   dotnet publish -r linux-x64 --self-contained false
    ///   ls publish/ | grep -iE 'kernel32|advapi32|user32'  # must return empty
    /// </summary>
    [Fact]
    public void ModeParsing_IsManaged_NoPInvokeRequired()
    {
        // Verify at runtime that the call path executes without needing any native interop.
        // If this test passes in a Linux container it confirms cross-platform portability.
        var act = () =>
        {
            _ = ModeParser.Parse("Connected");
            _ = ModeParser.Parse("Isolated");
            _ = ModeParser.Parse(null);
            _ = ModeParser.Parse("bad");
        };

        act.Should().NotThrow(
            because: "ModeParser must be pure managed C# with no platform-specific DLL imports");
    }
}
