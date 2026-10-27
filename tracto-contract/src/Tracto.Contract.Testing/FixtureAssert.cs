#nullable enable

using FluentAssertions;
using Tracto.Contract.Models;
using Tracto.Contract.Models.Execution;

namespace Tracto.Contract.Testing;

/// <summary>
/// FluentAssertions-based assertion helpers for common contract invariants.
/// Centralises assertion language so individual test projects stay concise and consistent.
/// </summary>
public static class FixtureAssert
{
    /// <summary>
    /// Assert that <paramref name="result"/> has the expected <see cref="RunState"/>.
    /// </summary>
    public static void RunStateIs(RunResult result, RunState expected)
        => result.RunState.Should().Be(expected,
            because: $"run state must be {expected}");

    /// <summary>
    /// Assert that <paramref name="result"/> has <c>needsAttention = true</c>.
    /// Required for any <see cref="RunState.Unknown"/> outcome (C3).
    /// </summary>
    public static void HasNeedsAttention(RunResult result)
        => result.NeedsAttention.Should().BeTrue(
            because: "UNKNOWN outcomes must set needsAttention=true");

    /// <summary>
    /// Assert that <paramref name="json"/> contains no recognisable plaintext secrets.
    /// Validates the telemetry-redaction contract: no passwords or raw authorization headers
    /// may appear in any emitted telemetry payload.
    /// </summary>
    public static void HasNoPlaintextSecrets(string json)
    {
        json.Should().NotContainAny(
            new[] { "password", "PASSWORD", "Password" },
            because: "secrets must not appear in telemetry");

        json.Should().NotContain("Authorization:",
            because: "auth headers must be redacted before emission");
    }

    /// <summary>
    /// Assert that <paramref name="result"/> is in one of the terminal run states
    /// defined by the state-machine contract.
    /// </summary>
    public static void IsTerminalState(RunResult result)
        => new[]
           {
               RunState.Succeeded,
               RunState.Failed,
               RunState.TimedOut,
               RunState.Unknown,
               RunState.Cancelled,
               RunState.HandedOff,
           }
           .Should().Contain(result.RunState,
               because: $"{result.RunState} must be one of the defined terminal states");
}
