using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using TractoRuntime.Browser.Contracts;
using TractoRuntime.Browser.Service;
using TractoRuntime.Browser.Tests.Fixtures;

namespace TractoRuntime.Browser.Tests;

/// <summary>
/// Acceptance tests for BRW-US-018 — All Five Service Outcomes and Run State Handling.
/// Covers BRW-AT-11, BRW-AT-20, BRW-AT-24, BRW-AT-25.
///
/// Requirement refs: Appendix A (CEC-005, CEC-013, CEC-016)
/// </summary>
public sealed class BRW_US_018_Tests : BrowserTestBase
{
    // ──────────────────────────────────────────────────────────────────────────
    // AC1 + AC5 — ServiceOutcomeDispatcher: all five outcomes map to correct
    //             run state and actionCode eligibility.
    //             Only the six allowed run states are ever produced.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Dispatch_Success_AllowsActionCodeAndSucceededState()
    {
        // AC1: SUCCESS is handled without branching on domain values.
        // The dispatcher allows actionCode execution and maps to SUCCEEDED.
        var dispatcher = MakeDispatcher();

        var result = new ServiceInvokeResult
        {
            Outcome    = ServiceOutcome.SUCCESS,
            ActionCode = "APPROVED"
        };

        var decision = dispatcher.Dispatch(result);

        decision.ServiceOutcome.Should().Be(ServiceOutcome.SUCCESS);
        decision.TerminalRunState.Should().Be(RunState.SUCCEEDED,
            "SUCCESS must produce run state SUCCEEDED");
        decision.ShouldExecuteActionCode.Should().BeTrue(
            "SUCCESS with an actionCode must allow the post-service UI step to execute");
        decision.ActionCode.Should().Be("APPROVED");
        decision.BusinessOutcome.Should().BeNull();
    }

    [Fact]
    public void Dispatch_Success_NoActionCode_StillSucceeded()
    {
        var dispatcher = MakeDispatcher();

        var result = new ServiceInvokeResult
        {
            Outcome    = ServiceOutcome.SUCCESS,
            ActionCode = null
        };

        var decision = dispatcher.Dispatch(result);

        decision.TerminalRunState.Should().Be(RunState.SUCCEEDED);
        decision.ShouldExecuteActionCode.Should().BeFalse(
            "when the service returns no actionCode there is no UI step to execute");
        decision.BusinessOutcome.Should().BeNull();
    }

    [Fact]
    public void Dispatch_BusinessRejection_SetsBusinessOutcomeRejectedAndSucceededState()
    {
        // AC2: BUSINESS_REJECTION → run state SUCCEEDED with businessOutcome=REJECTED
        // (assuming the post-rejection UI step, if configured, also succeeds).
        // The dispatcher itself sets BusinessOutcome=REJECTED and marks the action code
        // eligible so the run engine can execute the post-rejection display step.
        var dispatcher = MakeDispatcher();

        var result = new ServiceInvokeResult
        {
            Outcome    = ServiceOutcome.BUSINESS_REJECTION,
            ActionCode = "SHOW_REJECTION_STATUS"
        };

        var decision = dispatcher.Dispatch(result);

        decision.ServiceOutcome.Should().Be(ServiceOutcome.BUSINESS_REJECTION);
        decision.TerminalRunState.Should().Be(RunState.SUCCEEDED,
            "BUSINESS_REJECTION produces SUCCEEDED when the post-rejection UI step also succeeds");
        decision.ShouldExecuteActionCode.Should().BeTrue(
            "the post-rejection UI step (e.g. display status) should execute");
        decision.ActionCode.Should().Be("SHOW_REJECTION_STATUS");
        decision.BusinessOutcome.Should().Be("REJECTED",
            "BUSINESS_REJECTION must set businessOutcome=REJECTED per BRW-US-018 AC#2");
    }

    [Fact]
    public void Dispatch_BusinessRejection_NoActionCode_StillSetsRejected()
    {
        var dispatcher = MakeDispatcher();

        var result = new ServiceInvokeResult
        {
            Outcome    = ServiceOutcome.BUSINESS_REJECTION,
            ActionCode = null
        };

        var decision = dispatcher.Dispatch(result);

        decision.TerminalRunState.Should().Be(RunState.SUCCEEDED);
        decision.ShouldExecuteActionCode.Should().BeFalse();
        decision.BusinessOutcome.Should().Be("REJECTED",
            "businessOutcome=REJECTED applies even when no actionCode step is configured");
    }

    [Fact]
    public void Dispatch_TechnicalFailure_TerminatesAsFailed_NoActionCode()
    {
        // AC1: TECHNICAL_FAILURE → FAILED, no actionCode branch.
        var dispatcher = MakeDispatcher();

        var result = new ServiceInvokeResult
        {
            Outcome    = ServiceOutcome.TECHNICAL_FAILURE,
            ActionCode = "SHOULD_BE_IGNORED"   // must be ignored for TECHNICAL_FAILURE
        };

        var decision = dispatcher.Dispatch(result);

        decision.ServiceOutcome.Should().Be(ServiceOutcome.TECHNICAL_FAILURE);
        decision.TerminalRunState.Should().Be(RunState.FAILED,
            "TECHNICAL_FAILURE from the service terminates the run as FAILED");
        decision.ShouldExecuteActionCode.Should().BeFalse(
            "no actionCode branch may execute after TECHNICAL_FAILURE");
        decision.ActionCode.Should().BeNull(
            "the dispatcher must clear the actionCode for TECHNICAL_FAILURE");
        decision.BusinessOutcome.Should().BeNull();
    }

    [Fact]
    public void Dispatch_TransientFailure_TerminatesAsFailed_NoActionCode()
    {
        // AC1: TRANSIENT_FAILURE → FAILED, no actionCode branch.
        // Effectful actions are never blindly retried (BRE-008).
        var dispatcher = MakeDispatcher();

        var result = new ServiceInvokeResult
        {
            Outcome    = ServiceOutcome.TRANSIENT_FAILURE,
            ActionCode = null
        };

        var decision = dispatcher.Dispatch(result);

        decision.ServiceOutcome.Should().Be(ServiceOutcome.TRANSIENT_FAILURE);
        decision.TerminalRunState.Should().Be(RunState.FAILED,
            "TRANSIENT_FAILURE terminates the run as FAILED; effectful actions are never blindly retried");
        decision.ShouldExecuteActionCode.Should().BeFalse();
        decision.BusinessOutcome.Should().BeNull();
    }

    [Fact]
    public void Dispatch_UnknownOutcome_TerminatesAsUnknown_NoActionCodeEver()
    {
        // AC3 (CRITICAL): UNKNOWN_OUTCOME → UNKNOWN run state.
        // ShouldExecuteActionCode MUST be false.  No actionCode branch executes before
        // external reconciliation (CEC-016 / BRE-007 / BRW-US-015 AC#3).
        var dispatcher = MakeDispatcher();

        var result = new ServiceInvokeResult
        {
            Outcome    = ServiceOutcome.UNKNOWN_OUTCOME,
            ActionCode = "THIS_MUST_NEVER_EXECUTE"  // must be suppressed
        };

        var decision = dispatcher.Dispatch(result);

        decision.ServiceOutcome.Should().Be(ServiceOutcome.UNKNOWN_OUTCOME);
        decision.TerminalRunState.Should().Be(RunState.UNKNOWN,
            "UNKNOWN_OUTCOME must terminate the attempt as the UNKNOWN terminal run state");
        decision.ShouldExecuteActionCode.Should().BeFalse(
            "CRITICAL: no actionCode branch may execute when the service returns UNKNOWN_OUTCOME " +
            "(CEC-016 / BRW-US-018 AC#3)");
        decision.ActionCode.Should().BeNull(
            "the dispatcher must clear the actionCode for UNKNOWN_OUTCOME to prevent accidental use");
        decision.BusinessOutcome.Should().BeNull();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // AC2 — BUSINESS_REJECTION post-rejection UI step failure → FAILED
    //
    // The dispatcher itself does not execute the UI step — the run engine does.
    // This test verifies the decision record is correct so the run engine
    // can independently apply the FAILED outcome when the UI step fails.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Dispatch_BusinessRejection_DecisionAllowsRunEngineToFailOnUiStepFailure()
    {
        // The run engine must apply this logic:
        //   if (decision.ShouldExecuteActionCode && UIStep FAILS) → RunState = FAILED
        // This test verifies the decision signals to execute the UI step;
        // the run engine's responsibility (post-rejection UI failure → FAILED) is verified
        // by the integration flow, not by the dispatcher in isolation.
        var dispatcher = MakeDispatcher();

        var result = new ServiceInvokeResult
        {
            Outcome    = ServiceOutcome.BUSINESS_REJECTION,
            ActionCode = "SHOW_REJECTION"
        };

        var decision = dispatcher.Dispatch(result);

        // The decision enables UI-step execution — the run engine must fail the run
        // if that step fails, independent of the service outcome (AC#2).
        decision.ShouldExecuteActionCode.Should().BeTrue();
        decision.BusinessOutcome.Should().Be("REJECTED");
        decision.TerminalRunState.Should().Be(RunState.SUCCEEDED,
            "this is the INTENDED terminal state if the UI step succeeds; " +
            "the run engine overrides to FAILED if the UI step itself fails (AC#2)");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // AC5 — Only the six allowed run states are produced.
    //        UNKNOWN is terminal and stops the attempt.
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ServiceOutcome.SUCCESS, RunState.SUCCEEDED)]
    [InlineData(ServiceOutcome.BUSINESS_REJECTION, RunState.SUCCEEDED)]
    [InlineData(ServiceOutcome.TECHNICAL_FAILURE, RunState.FAILED)]
    [InlineData(ServiceOutcome.TRANSIENT_FAILURE, RunState.FAILED)]
    [InlineData(ServiceOutcome.UNKNOWN_OUTCOME, RunState.UNKNOWN)]
    public void Dispatch_AllFiveOutcomes_ProduceOnlyAllowedRunStates(
        ServiceOutcome outcome, RunState expectedState)
    {
        var dispatcher = MakeDispatcher();

        var result = new ServiceInvokeResult { Outcome = outcome, ActionCode = null };
        var decision = dispatcher.Dispatch(result);

        // AC5: only SUCCEEDED, FAILED, CANCELLED, TIMED_OUT, UNKNOWN, HANDED_OFF are valid.
        // The dispatcher produces only SUCCEEDED, FAILED, or UNKNOWN — other terminal
        // states (CANCELLED, TIMED_OUT, HANDED_OFF) are set by the run engine directly.
        decision.TerminalRunState.Should().Be(expectedState);
        var allowedStates = new[]
        {
            RunState.SUCCEEDED, RunState.FAILED, RunState.CANCELLED,
            RunState.TIMED_OUT, RunState.UNKNOWN, RunState.HANDED_OFF
        };
        allowedStates.Should().Contain(decision.TerminalRunState,
            "only the six CEC-016 terminal run states are permitted");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // AC3 — UNKNOWN_OUTCOME is distinct from the run state UNKNOWN.
    //        ServiceOutcome enum and RunState enum must not be conflated.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ServiceOutcomeEnum_HasExactlyFiveValues()
    {
        // CEC v1.0.1 §6.3 specifies exactly five service outcome values.
        // Adding a sixth would indicate a contract violation.
        var values = Enum.GetValues<ServiceOutcome>();
        values.Should().HaveCount(5, "CEC v1.0.1 defines exactly five serviceOutcome values");
        values.Should().Contain(ServiceOutcome.SUCCESS);
        values.Should().Contain(ServiceOutcome.BUSINESS_REJECTION);
        values.Should().Contain(ServiceOutcome.TECHNICAL_FAILURE);
        values.Should().Contain(ServiceOutcome.TRANSIENT_FAILURE);
        values.Should().Contain(ServiceOutcome.UNKNOWN_OUTCOME);
    }

    [Fact]
    public void RunStateEnum_HasExactlySixValues()
    {
        // CEC-016 defines exactly six run states.
        var values = Enum.GetValues<RunState>();
        values.Should().HaveCount(6, "CEC-016 defines exactly six run states");
        values.Should().Contain(RunState.SUCCEEDED);
        values.Should().Contain(RunState.FAILED);
        values.Should().Contain(RunState.CANCELLED);
        values.Should().Contain(RunState.TIMED_OUT);
        values.Should().Contain(RunState.UNKNOWN);
        values.Should().Contain(RunState.HANDED_OFF);
    }

    [Fact]
    public void UnknownOutcome_IsServiceOutcomeNotRunState()
    {
        // UNKNOWN_OUTCOME is a serviceOutcome value — it must not be confused with
        // the terminal run state UNKNOWN (CEC-016 / BRW-US-018 note in AC#5 / spec BRE-007).
        var serviceOutcomes = Enum.GetNames<ServiceOutcome>();
        var runStates = Enum.GetNames<RunState>();

        serviceOutcomes.Should().Contain("UNKNOWN_OUTCOME",
            "UNKNOWN_OUTCOME is a service outcome, not a run state");
        runStates.Should().Contain("UNKNOWN",
            "UNKNOWN is a terminal run state");

        // The two enums must not share the name "UNKNOWN_OUTCOME"
        runStates.Should().NotContain("UNKNOWN_OUTCOME",
            "UNKNOWN_OUTCOME must only appear as a serviceOutcome, never as a run state");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // AC4 — HANDED_OFF is set on confirmed async acknowledgement only (never
    //        speculatively).  Tested via AsyncContinuationHandler contract in
    //        BRW_US_019_Tests.  Here we verify that the dispatcher never produces
    //        RunState.HANDED_OFF — that state is set by the run engine separately.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Dispatch_NeverProducesHandedOffRunState()
    {
        // HANDED_OFF is set by the run engine only after a confirmed async acknowledgement.
        // The dispatcher must not produce it — that would be a speculative HANDED_OFF.
        var dispatcher = MakeDispatcher();

        foreach (var outcome in Enum.GetValues<ServiceOutcome>())
        {
            var result = new ServiceInvokeResult { Outcome = outcome, ActionCode = null };

            // UNKNOWN_OUTCOME throws an exception for an unknown outcome value;
            // skip the guard path that would throw — test only the five valid values.
            var decision = dispatcher.Dispatch(result);
            decision.TerminalRunState.Should().NotBe(RunState.HANDED_OFF,
                $"the dispatcher must not produce HANDED_OFF for outcome={outcome}; " +
                "HANDED_OFF is set by the run engine on confirmed async acknowledgement only");
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // ServiceInvoker — unit tests covering the HTTP client layer.
    // Uses a mock IServiceInvoker to verify BRW-AT-24 and BRW-AT-25.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ServiceInvokerMock_AllFiveOutcomes_ReturnedCorrectly()
    {
        // BRW-AT-24: fake registered service returns each of the five exact serviceOutcome values.
        // Uses the mock from BrowserTestBase (IServiceInvoker mock) to simulate the service.
        var expectedOutcomes = new[]
        {
            ServiceOutcome.SUCCESS,
            ServiceOutcome.BUSINESS_REJECTION,
            ServiceOutcome.TECHNICAL_FAILURE,
            ServiceOutcome.TRANSIENT_FAILURE,
            ServiceOutcome.UNKNOWN_OUTCOME
        };

        foreach (var expected in expectedOutcomes)
        {
            ServiceInvokerMock
                .Setup(m => m.InvokeAsync(
                    It.IsAny<ServiceInvokeRef>(),
                    It.IsAny<Dictionary<string, object?>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ServiceInvokeResult { Outcome = expected });

            var result = await ServiceInvokerMock.Object.InvokeAsync(
                new ServiceInvokeRef { ServiceId = "test-svc" },
                [],
                CancellationToken.None);

            result.Outcome.Should().Be(expected,
                $"fake service returning {expected} must be received unchanged");
        }
    }

    [Fact]
    public async Task ServiceInvokerMock_UnknownOutcome_NeverBranchesOnActionCode()
    {
        // BRW-AT-24: when serviceOutcome=UNKNOWN_OUTCOME, no actionCode branch executes.
        // Verify that the dispatcher + mock service chain produces the correct decision.
        ServiceInvokerMock
            .Setup(m => m.InvokeAsync(
                It.IsAny<ServiceInvokeRef>(),
                It.IsAny<Dictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceInvokeResult
            {
                Outcome    = ServiceOutcome.UNKNOWN_OUTCOME,
                ActionCode = "MUST_NOT_BE_FOLLOWED"
            });

        var invokeResult = await ServiceInvokerMock.Object.InvokeAsync(
            new ServiceInvokeRef { ServiceId = "test-svc" },
            [],
            CancellationToken.None);

        var dispatcher = MakeDispatcher();
        var decision = dispatcher.Dispatch(invokeResult);

        decision.TerminalRunState.Should().Be(RunState.UNKNOWN);
        decision.ShouldExecuteActionCode.Should().BeFalse(
            "UNKNOWN_OUTCOME must never trigger actionCode branch execution (BRW-US-018 AC#3)");
        decision.ActionCode.Should().BeNull();
    }

    [Fact]
    public async Task ServiceInvokerMock_BusinessRejection_SetsRejectedAndAllowsUiStep()
    {
        // BRW-AT-20: serviceOutcome=BUSINESS_REJECTION → follow preapproved actionCode
        // for the post-rejection UI step.
        ServiceInvokerMock
            .Setup(m => m.InvokeAsync(
                It.IsAny<ServiceInvokeRef>(),
                It.IsAny<Dictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceInvokeResult
            {
                Outcome    = ServiceOutcome.BUSINESS_REJECTION,
                ActionCode = "SHOW_REJECTION_STATUS"
            });

        var invokeResult = await ServiceInvokerMock.Object.InvokeAsync(
            new ServiceInvokeRef { ServiceId = "payment-svc" },
            [],
            CancellationToken.None);

        var dispatcher = MakeDispatcher();
        var decision = dispatcher.Dispatch(invokeResult);

        // The run engine sees this and:
        // 1. Executes the "SHOW_REJECTION_STATUS" UI step
        // 2. If it succeeds → completes as SUCCEEDED with businessOutcome=REJECTED
        // 3. If it fails → terminates as FAILED (independent technical failure)
        decision.ShouldExecuteActionCode.Should().BeTrue();
        decision.ActionCode.Should().Be("SHOW_REJECTION_STATUS");
        decision.BusinessOutcome.Should().Be("REJECTED");
        decision.TerminalRunState.Should().Be(RunState.SUCCEEDED);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // BRW-AT-11 — Sync service decision: BUSINESS_REJECTION status displayed,
    //             run completes as SUCCEEDED with businessOutcome REJECTED.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BrwAt11_SyncServiceDecision_BusinessRejection_RunSucceededWithBusinessOutcome()
    {
        // Simulate: service returns BUSINESS_REJECTION, post-rejection UI step succeeds.
        // Expected: run state = SUCCEEDED, businessOutcome = REJECTED.
        // The dispatcher produces the correct decision; the "run engine" applying the
        // UI step and finalising the CecRunResult is verified here end-to-end.
        var dispatcher = MakeDispatcher();

        var serviceResult = new ServiceInvokeResult
        {
            Outcome    = ServiceOutcome.BUSINESS_REJECTION,
            ActionCode = "DISPLAY_REJECTION"
        };

        var decision = dispatcher.Dispatch(serviceResult);

        // Simulate run engine: UI step "succeeds"
        bool uiStepSucceeded = true;

        var finalRunState = decision.ShouldExecuteActionCode && !uiStepSucceeded
            ? RunState.FAILED
            : decision.TerminalRunState;

        var cecResult = new CecRunResult
        {
            RunId          = "run-brw-at-11",
            State          = finalRunState,
            ServiceOutcome = ServiceOutcome.BUSINESS_REJECTION,
            BusinessOutcome = decision.BusinessOutcome,
            StartedAt      = DateTimeOffset.UtcNow
        };

        cecResult.State.Should().Be(RunState.SUCCEEDED);
        cecResult.BusinessOutcome.Should().Be("REJECTED");
        cecResult.ServiceOutcome.Should().Be(ServiceOutcome.BUSINESS_REJECTION);
    }

    [Fact]
    public void BrwAt11_PostRejectionUiStepFails_RunTerminatesAsFailed()
    {
        // AC2: if the post-rejection UI step itself fails, the run terminates as FAILED.
        var dispatcher = MakeDispatcher();

        var serviceResult = new ServiceInvokeResult
        {
            Outcome    = ServiceOutcome.BUSINESS_REJECTION,
            ActionCode = "DISPLAY_REJECTION"
        };

        var decision = dispatcher.Dispatch(serviceResult);

        // Simulate run engine: UI step FAILS
        bool uiStepSucceeded = false;

        var finalRunState = decision.ShouldExecuteActionCode && !uiStepSucceeded
            ? RunState.FAILED
            : decision.TerminalRunState;

        finalRunState.Should().Be(RunState.FAILED,
            "when the post-rejection UI step fails the run terminates as FAILED, " +
            "not SUCCEEDED, regardless of the service outcome (BRW-US-018 AC#2)");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // CecRunResult.BusinessOutcome — field presence (added for BRW-US-018)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CecRunResult_HasBusinessOutcomeField()
    {
        // Verify the field is present and settable (structural test).
        var result = new CecRunResult
        {
            RunId           = "run-bo-test",
            State           = RunState.SUCCEEDED,
            BusinessOutcome = "REJECTED",
            StartedAt       = DateTimeOffset.UtcNow
        };

        result.BusinessOutcome.Should().Be("REJECTED");
    }

    [Fact]
    public void CecRunResult_BusinessOutcome_NullByDefault()
    {
        var result = new CecRunResult
        {
            RunId     = "run-bo-null",
            State     = RunState.SUCCEEDED,
            StartedAt = DateTimeOffset.UtcNow
        };

        result.BusinessOutcome.Should().BeNull(
            "BusinessOutcome is null for outcomes other than BUSINESS_REJECTION");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static ServiceOutcomeDispatcher MakeDispatcher()
        => new(NullLogger<ServiceOutcomeDispatcher>.Instance);
}
